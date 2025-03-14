using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PersonGrpcClient.Models;
using PersonGrpcClient.Services;

namespace PersonGrpcClient.ViewModels
{
    public class MainPageViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseService _databaseService;
        private readonly GrpcClientService _grpcClient;
        private readonly IConnectivity _connectivity;
        private readonly IDispatcher _dispatcher;
        private readonly IDispatcherTimer _timer;

        private string _name;
        private string _lastName;
        private int? _age;
        private double? _weight;
        private bool _isBusy;
        private string _connectionStatusText;
        private Color _connectionStatusColor;
        private ObservableCollection<PersonDisplay> _syncedPeople;
        private ObservableCollection<PersonDisplay> _pendingPeople;
        private bool _isCheckingServer;

        public event PropertyChangedEventHandler PropertyChanged;

        public MainPageViewModel(
            DatabaseService databaseService,
            GrpcClientService grpcClient,
            IConnectivity connectivity,
            IDispatcher dispatcher)
        {
            _databaseService = databaseService;
            _grpcClient = grpcClient;
            _connectivity = connectivity;
            _dispatcher = dispatcher;

            SyncedPeople = new ObservableCollection<PersonDisplay>();
            PendingPeople = new ObservableCollection<PersonDisplay>();

            SaveCommand = new Command(async () => await SavePersonAsync());
            ClearDataCommand = new Command(async () => await ClearDataAsync());
            SyncNowCommand = new Command(async () => await SyncPendingDataAsync(), () => HasPendingRecords && !IsBusy);

            _connectivity.ConnectivityChanged += OnConnectivityChanged;
            UpdateConnectionStatus();
            LoadSavedPeople();

            // Iniciar o timer para verificação periódica
            _timer = Application.Current.Dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(30);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        #region Properties

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged();
            }
        }

        public string LastName
        {
            get => _lastName;
            set
            {
                _lastName = value;
                OnPropertyChanged();
            }
        }

        public int? Age
        {
            get => _age;
            set
            {
                _age = value;
                OnPropertyChanged();
            }
        }

        public double? Weight
        {
            get => _weight;
            set
            {
                _weight = value;
                OnPropertyChanged();
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotBusy));
                (SyncNowCommand as Command)?.ChangeCanExecute();
            }
        }

        public bool IsNotBusy => !IsBusy;

        public string ConnectionStatusText
        {
            get => _connectionStatusText;
            set
            {
                _connectionStatusText = value;
                OnPropertyChanged();
            }
        }

        public Color ConnectionStatusColor
        {
            get => _connectionStatusColor;
            set
            {
                _connectionStatusColor = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<PersonDisplay> SyncedPeople
        {
            get => _syncedPeople;
            set
            {
                _syncedPeople = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<PersonDisplay> PendingPeople
        {
            get => _pendingPeople;
            set
            {
                _pendingPeople = value;
                _pendingPeople.CollectionChanged += (s, e) =>
                {
                    OnPropertyChanged(nameof(HasPendingRecords));
                    (SyncNowCommand as Command)?.ChangeCanExecute();
                };
                OnPropertyChanged();
            }
        }

        public bool HasPendingRecords => PendingPeople?.Any() == true;

        public ICommand SaveCommand { get; }
        public ICommand ClearDataCommand { get; }
        public ICommand SyncNowCommand { get; }

        #endregion

        #region Methods

        private async Task SavePersonAsync()
        {
            if (IsBusy) return;

            var validationErrors = new List<string>();

            if (string.IsNullOrWhiteSpace(Name))
            {
                validationErrors.Add("First Name is required");
            }

            if (string.IsNullOrWhiteSpace(LastName))
            {
                validationErrors.Add("Last Name is required");
            }

            if (!Age.HasValue || Age <= 0)
            {
                validationErrors.Add("Age must be greater than 0");
            }

            if (!Weight.HasValue || Weight <= 0)
            {
                validationErrors.Add("Weight must be greater than 0");
            }

            if (validationErrors.Any())
            {
                await Application.Current.MainPage.DisplayAlert(
                    "Validation Error",
                    string.Join("\n", validationErrors),
                    "OK");
                return;
            }

            var connectionStatus = _connectivity.NetworkAccess == NetworkAccess.Internet
                ? "Data will be saved directly to the server"
                : "Data will be saved locally and synced when online";

            bool shouldSave = await Application.Current.MainPage.DisplayAlert(
                "Confirm Save",
                $"Please confirm the following information:\n\n" +
                $"First Name: {Name}\n" +
                $"Last Name: {LastName}\n" +
                $"Age: {Age}\n" +
                $"Weight: {Weight:F1} kg\n\n" +
                $"Status: {connectionStatus}",
                "Save",
                "Cancel");

            if (!shouldSave)
            {
                return;
            }

            try
            {
                IsBusy = true;

                var person = new Person
                {
                    Name = Name,
                    LastName = LastName,
                    Age = Age.Value,
                    Weight = Weight.Value,
                    CreatedAt = DateTime.Now,
                    LastSyncAttempt = null
                };

                if (_connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    try
                    {
                        var response = await _grpcClient.SavePersonAsync(person);
                        if (response.Saved)
                        {
                            person.IsSynced = true;
                            person.ServerId = response.Id;
                            person.LastSyncAttempt = DateTime.Now;
                            await _databaseService.SavePersonLocallyAsync(person);

                            var displayPerson = new PersonDisplay
                            {
                                Id = person.Id,
                                ServerId = response.Id,
                                Name = person.Name,
                                LastName = person.LastName,
                                Age = person.Age,
                                Weight = person.Weight,
                                Status = SyncStatus.Synced,
                                CreatedAt = person.CreatedAt,
                                LastSyncAttempt = person.LastSyncAttempt
                            };

                            _dispatcher.Dispatch(() =>
                            {
                                SyncedPeople.Add(displayPerson);
                            });

                            await Application.Current.MainPage.DisplayAlert(
                                "Success",
                                $"Person saved successfully with ID: {response.Id}",
                                "OK");
                            ClearForm();
                        }
                    }
                    catch (Grpc.Core.RpcException ex)
                    {
                        Debug.WriteLine($"gRPC Error: {ex.Message}");

                        var errorMessage = ex.Status.StatusCode switch
                        {
                            Grpc.Core.StatusCode.Unavailable =>
                                "Unable to connect to the server. Data will be saved locally and synced later.",
                            Grpc.Core.StatusCode.DeadlineExceeded =>
                                "Server took too long to respond. Data will be saved locally.",
                            Grpc.Core.StatusCode.Internal =>
                                "Server error occurred. Data will be saved locally.",
                            _ => "Communication error with server. Data will be saved locally."
                        };

                        await Application.Current.MainPage.DisplayAlert(
                            "Server Connection Error",
                            errorMessage,
                            "OK");

                        await SaveLocally(person);
                    }
                }
                else
                {
                    await SaveLocally(person);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"General Error: {ex.Message}");
                await Application.Current.MainPage.DisplayAlert(
                    "Error",
                    "An unexpected error occurred. Please try again.",
                    "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SaveLocally(Person person)
        {
            await _databaseService.SavePersonLocallyAsync(person);

            var displayPerson = new PersonDisplay
            {
                Id = person.Id,
                Name = person.Name,
                LastName = person.LastName,
                Age = person.Age,
                Weight = person.Weight,
                Status = SyncStatus.LocallySaved,
                CreatedAt = person.CreatedAt,
                LastSyncAttempt = person.LastSyncAttempt
            };

            _dispatcher.Dispatch(() =>
            {
                PendingPeople.Add(displayPerson);
            });

            await Application.Current.MainPage.DisplayAlert(
                "Saved Locally",
                "Data has been saved locally and will be synced when connection is restored.",
                "OK");
            ClearForm();
        }

        private async Task LoadSavedPeople()
        {
            var people = await _databaseService.GetAllPeopleAsync();
            var displayPeople = people.Select(p => new PersonDisplay
            {
                Id = p.Id,
                ServerId = p.ServerId,
                Name = p.Name,
                LastName = p.LastName,
                Age = p.Age,
                Weight = p.Weight,
                Status = p.IsSynced ? SyncStatus.Synced : SyncStatus.LocallySaved,
                CreatedAt = p.CreatedAt,
                LastSyncAttempt = p.LastSyncAttempt
            }).ToList();

            _dispatcher.Dispatch(() =>
            {
                SyncedPeople.Clear();
                PendingPeople.Clear();

                foreach (var person in displayPeople)
                {
                    if (person.Status == SyncStatus.Synced)
                        SyncedPeople.Add(person);
                    else
                        PendingPeople.Add(person);
                }
            });
        }

        private void ClearForm()
        {
            Name = string.Empty;
            LastName = string.Empty;
            Age = null;
            Weight = null;
        }

        private async void OnConnectivityChanged(object sender, ConnectivityChangedEventArgs e)
        {
            UpdateConnectionStatus();
            if (e.NetworkAccess == NetworkAccess.Internet && HasPendingRecords && !_isCheckingServer)
            {
                try
                {
                    _isCheckingServer = true;
                    await SyncPendingDataAsync();
                }
                finally
                {
                    _isCheckingServer = false;
                }
            }
        }

        private async Task SyncPendingDataAsync()
        {
            if (IsBusy || !HasPendingRecords) return;

            try
            {
                IsBusy = true;
                var processedIds = new HashSet<int>();
                var failedSync = new List<string>();

                foreach (var person in PendingPeople.ToList())
                {
                    try
                    {
                        if (processedIds.Contains(person.Id))
                            continue;

                        UpdatePersonStatus(person.Id, SyncStatus.SyncInProgress);
                        Debug.WriteLine($"Syncing person ID {person.Id}");

                        try
                        {
                            var dbPerson = await _databaseService.GetPersonAsync(person.Id);
                            if (dbPerson == null) continue;

                            var response = await _grpcClient.SavePersonAsync(dbPerson);
                            if (response.Saved)
                            {
                                DateTime syncTime = DateTime.Now;
                                await _databaseService.MarkAsSyncedAsync(person.Id, response.Id, syncTime);
                                processedIds.Add(person.Id);

                                _dispatcher.Dispatch(() =>
                                {
                                    PendingPeople.Remove(person);
                                    person.Status = SyncStatus.Synced;
                                    person.ServerId = response.Id;
                                    person.LastSyncAttempt = syncTime;
                                    SyncedPeople.Add(person);
                                });

                                Debug.WriteLine($"Successfully synced person {person.Id}");
                            }
                            else
                            {
                                failedSync.Add($"{person.Name} {person.LastName} - Server rejected the data");
                                UpdatePersonStatus(person.Id, SyncStatus.SyncFailed);
                            }
                        }
                        catch (Grpc.Core.RpcException ex)
                        {
                            var errorDetail = ex.Status.StatusCode switch
                            {
                                Grpc.Core.StatusCode.Unavailable => "Server unavailable",
                                Grpc.Core.StatusCode.DeadlineExceeded => "Request timeout",
                                Grpc.Core.StatusCode.Internal => "Server error",
                                _ => $"Communication error: {ex.Status.Detail}"
                            };

                            failedSync.Add($"{person.Name} {person.LastName} - {errorDetail}");
                            UpdatePersonStatus(person.Id, SyncStatus.SyncFailed);
                            Debug.WriteLine($"gRPC error syncing person {person.Id}: {errorDetail}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failedSync.Add($"{person.Name} {person.LastName} - Unexpected error");
                        UpdatePersonStatus(person.Id, SyncStatus.SyncFailed);
                        Debug.WriteLine($"Error syncing person {person.Id}: {ex.Message}");
                    }
                }

                if (processedIds.Any() || failedSync.Any())
                {
                    await ShowSyncResults(processedIds.Count, failedSync);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Sync Error: {ex.Message}");
                await Application.Current.MainPage.DisplayAlert(
                    "Sync Error",
                    "Failed to sync data. Please check your connection and try again.",
                    "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ShowSyncResults(int successCount, List<string> failedSync)
        {
            var message = new System.Text.StringBuilder();

            if (successCount > 0)
            {
                message.AppendLine($"Successfully synced {successCount} records");
                message.AppendLine($"Sync completed at: {DateTime.Now:g}");
            }

            if (failedSync.Any())
            {
                if (successCount > 0) message.AppendLine();
                message.AppendLine("Failed to sync:");
                foreach (var failure in failedSync)
                {
                    message.AppendLine($"- {failure}");
                }
            }

            await Application.Current.MainPage.DisplayAlert(
                failedSync.Any() ?
                    (successCount > 0 ? "Sync Partially Complete" : "Sync Failed")
                    : "Sync Complete",
                message.ToString(),
                "OK");
        }

        private void UpdatePersonStatus(int localId, SyncStatus status, int? serverId = null, DateTime? syncTime = null)
        {
            var person = PendingPeople.FirstOrDefault(p => p.Id == localId);
            if (person != null)
            {
                _dispatcher.Dispatch(() =>
                {
                    person.Status = status;
                    person.ServerId = serverId;
                    person.LastSyncAttempt = syncTime ?? DateTime.Now;

                    if (status == SyncStatus.Synced && serverId.HasValue)
                    {
                        PendingPeople.Remove(person);
                        SyncedPeople.Add(person);
                    }
                    else
                    {
                        var index = PendingPeople.IndexOf(person);
                        if (index >= 0)
                        {
                            PendingPeople.RemoveAt(index);
                            PendingPeople.Insert(index, person);
                        }
                    }
                });
            }
        }

        private async Task ClearDataAsync()
        {
            try
            {
                bool answer = await Application.Current.MainPage.DisplayAlert(
                    "Confirm Delete",
                    "Are you sure you want to clear all local data?",
                    "Yes",
                    "No");

                if (answer)
                {
                    await _databaseService.ClearAllDataAsync();
                    _dispatcher.Dispatch(() =>
                    {
                        SyncedPeople.Clear();
                        PendingPeople.Clear();
                    });
                    await Application.Current.MainPage.DisplayAlert(
                        "Success",
                        "All local data has been cleared",
                        "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error clearing data: {ex.Message}");
                await Application.Current.MainPage.DisplayAlert(
                    "Error",
                    $"Failed to clear data: {ex.Message}",
                    "OK");
            }
        }

        private void UpdateConnectionStatus()
        {
            ConnectionStatusText = _connectivity.NetworkAccess == NetworkAccess.Internet
                ? "🌐 Online"
                : "📴 Offline";

            ConnectionStatusColor = _connectivity.NetworkAccess == NetworkAccess.Internet
                ? Colors.Green
                : Colors.Red;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async void Timer_Tick(object sender, EventArgs e)
        {
            if (_connectivity.NetworkAccess == NetworkAccess.Internet && HasPendingRecords && !IsBusy)
            {
                await SyncPendingDataAsync();
            }
        }

        public void Dispose()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= Timer_Tick;
            }
        }
        #endregion
    }
}