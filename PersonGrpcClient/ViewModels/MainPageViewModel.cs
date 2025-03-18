using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
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
        private bool _autoSyncEnabled = false; // Controle para sincronização automática

        // paginação
        private int _currentPage = 1;
        private int _pageSize =20;
        private int _totalRecords;
        private string _paginationInfo;

        public ICommand SyncAllDataCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand PreviousPageCommand { get; }
        public ICommand RandomizeDataCommand { get; }
        public ICommand SyncChangesCommand { get; }

        
        //REST
        private readonly RestClientService _restClient;
        public ICommand SyncAllRestCommand { get; }
        public ICommand SyncChangesRestCommand { get; }


        public event PropertyChangedEventHandler PropertyChanged;

        public MainPageViewModel(
            DatabaseService databaseService,
            RestClientService restClient,
            GrpcClientService grpcClient,
            IConnectivity connectivity,
            IDispatcher dispatcher)
        {
            _databaseService = databaseService;
            _grpcClient = grpcClient;
            _connectivity = connectivity;
            _dispatcher = dispatcher;
            _restClient = restClient;

            SyncedPeople = new ObservableCollection<PersonDisplay>();
            PendingPeople = new ObservableCollection<PersonDisplay>();

            SaveCommand = new Command(async () => await SavePersonAsync());
            ClearDataCommand = new Command(async () => await ClearDataAsync());
            SyncNowCommand = new Command(async () => await SyncPendingDataAsync(), () => HasPendingRecords && !IsBusy);

            NextPageCommand = new Command(async () => await LoadNextPageAsync(), () => CanGoNext);
            PreviousPageCommand = new Command(async () => await LoadPreviousPageAsync(), () => CanGoPrevious);

            SyncAllDataCommand = new Command(async () => await SyncAllDataAsync());
            RandomizeDataCommand = new Command(async () => await RandomizeDataAsync());
            SyncChangesCommand = new Command(async () => await SyncChangesAsync());

            // Carregar dados iniciais
            Task.Run(async () => await InitializeDataAsync());

            //REST
            SyncAllRestCommand = new Command(async () => await SyncAllRestAsync());
            SyncChangesRestCommand = new Command(async () => await SyncChangesRestAsync());

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

        public string PaginationInfo
        {
            get => _paginationInfo;
            set
            {
                _paginationInfo = value;
                OnPropertyChanged();
            }
        }
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

            // Sincronização automática desabilitada para o cenário atual
            if (_autoSyncEnabled) // Só executará quando reativarmos o formulário
            {
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
            if (_autoSyncEnabled  && _connectivity.NetworkAccess == NetworkAccess.Internet && HasPendingRecords && !IsBusy)
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

        private async Task SyncAllDataAsync()
        {
            if (IsBusy) return;

            try
            {
                IsBusy = true;
                Debug.WriteLine("Starting SyncAllDataAsync");

                var shouldSync = await Application.Current.MainPage.DisplayAlert(
                    "Full Sync",
                    "This will download all records from the server. Continue?",
                    "Yes", "No");

                if (!shouldSync) return;

                var totalStartTime = DateTime.Now;

                // Tempo para obter dados do PostgreSQL
                Debug.WriteLine("Starting to fetch data from server");
                var fetchStartTime = DateTime.Now;
                var serverPeople = await _grpcClient.GetAllPeopleFromServerAsync();
                var fetchDuration = DateTime.Now - fetchStartTime;
                Debug.WriteLine($"Received {serverPeople.Count} records from server in {fetchDuration.TotalSeconds:F2} seconds");

                if (serverPeople.Any())
                {
                    // Tempo para converter e salvar no SQLite
                    Debug.WriteLine("Converting and saving to SQLite");
                    var saveStartTime = DateTime.Now;

                    var localPeople = serverPeople.Select(p => new Person
                    {
                        Name = p.Name,
                        LastName = p.LastName,
                        Age = p.Age,
                        Weight = p.Weight,
                        ServerId = p.Id,
                        IsSynced = true,
                        CreatedAt = !string.IsNullOrEmpty(p.CreatedAt)
                            ? DateTime.Parse(p.CreatedAt)
                            : DateTime.Now,
                        LastSyncAttempt = !string.IsNullOrEmpty(p.SyncedAt)
                            ? DateTime.Parse(p.SyncedAt)
                            : DateTime.Now
                    }).ToList();

                    await _databaseService.ClearAllDataAsync();
                    await _databaseService.BulkInsertPeopleAsync(localPeople);

                    var saveDuration = DateTime.Now - saveStartTime;
                    Debug.WriteLine($"Saved {localPeople.Count} records to SQLite in {saveDuration.TotalSeconds:F2} seconds");

                    var totalTime = DateTime.Now - totalStartTime;

                    // Recarrega a primeira página
                    await LoadPageAsync(1);

                    // Prepara relatório detalhado
                    var report = new StringBuilder();
                    report.AppendLine($"Sync Complete:");
                    report.AppendLine($"Total records: {serverPeople.Count}");
                    report.AppendLine();
                    report.AppendLine($"Fetch from PostgreSQL: {fetchDuration.TotalSeconds:F2} seconds");
                    report.AppendLine($"Save to SQLite: {saveDuration.TotalSeconds:F2} seconds");
                    report.AppendLine($"Total time: {totalTime.TotalSeconds:F2} seconds");
                    report.AppendLine();
                    report.AppendLine($"Average fetch time: {(fetchDuration.TotalMilliseconds / serverPeople.Count):F2} ms/record");
                    report.AppendLine($"Average save time: {(saveDuration.TotalMilliseconds / serverPeople.Count):F2} ms/record");
                    report.AppendLine($"Throughput: {serverPeople.Count / totalTime.TotalSeconds:F2} records/second");

                    Debug.WriteLine(report.ToString());

                    await Application.Current.MainPage.DisplayAlert(
                        "Sync Complete",
                        report.ToString(),
                        "OK");
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert(
                        "Sync Complete",
                        "No records found on server",
                        "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in SyncAllDataAsync: {ex.Message}");
                await Application.Current.MainPage.DisplayAlert(
                    "Sync Error",
                    $"Failed to synchronize data from server: {ex.Message}",
                    "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task LoadPageAsync(int page)
        {
            try
            {
                IsBusy = true;

                var (items, totalCount) = await _databaseService.GetPaginatedPeopleAsync(page, _pageSize);

                _totalRecords = totalCount;
                var totalPages = (int)Math.Ceiling(totalCount / (double)_pageSize);

                _dispatcher.Dispatch(() =>
                {
                    SyncedPeople.Clear();
                    foreach (var person in items)
                    {
                        var displayPerson = new PersonDisplay
                        {
                            Id = person.Id,
                            ServerId = person.ServerId,
                            Name = person.Name,
                            LastName = person.LastName,
                            Age = person.Age,
                            Weight = person.Weight,
                            Status = person.IsSynced ? SyncStatus.Synced : SyncStatus.LocallySaved,
                            CreatedAt = person.CreatedAt,
                            LastSyncAttempt = person.LastSyncAttempt
                        };
                        SyncedPeople.Add(displayPerson);
                    }

                    _currentPage = page;
                    PaginationInfo = $"Page {_currentPage} of {totalPages} (Total: {totalCount})";

                    OnPropertyChanged(nameof(CanGoNext));
                    OnPropertyChanged(nameof(CanGoPrevious));
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading page {page}: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task LoadNextPageAsync()
        {
            if (CanGoNext)
            {
                await LoadPageAsync(_currentPage + 1);
            }
        }

        private async Task LoadPreviousPageAsync()
        {
            if (CanGoPrevious)
            {
                await LoadPageAsync(_currentPage - 1);
            }
        }

        // Também podemos adicionar propriedades para controlar a habilitação dos botões
        public bool CanGoNext => !IsBusy && _currentPage < Math.Ceiling(_totalRecords / (double)_pageSize);
        public bool CanGoPrevious => !IsBusy && _currentPage > 1;

        private async Task RandomizeDataAsync()
        {
            if (IsBusy) return;

            try
            {
                IsBusy = true;

                var shouldRandomize = await Application.Current.MainPage.DisplayAlert(
                    "Confirm Randomize",
                    "This will modify all existing records with random data. Continue?",
                    "Yes", "No");

                if (!shouldRandomize) return;

                var totalStartTime = DateTime.Now;

                // Atualizar dados randomicamente
                Debug.WriteLine("Starting randomization...");
                var updatedPeople = await _databaseService.UpdatePeopleRandomlyAsync();
                var randomizeDuration = DateTime.Now - totalStartTime;

                // Recarregar a página atual
                var reloadStart = DateTime.Now;
                await LoadPageAsync(_currentPage);
                var reloadDuration = DateTime.Now - reloadStart;

                var totalDuration = DateTime.Now - totalStartTime;

                // Preparar relatório detalhado
                var report = new StringBuilder();
                report.AppendLine($"Randomization Complete:");
                report.AppendLine($"Total records: {updatedPeople.Count}");
                report.AppendLine();
                report.AppendLine($"Randomization time: {randomizeDuration.TotalSeconds:F2} seconds");
                report.AppendLine($"UI reload time: {reloadDuration.TotalSeconds:F2} seconds");
                report.AppendLine($"Total time: {totalDuration.TotalSeconds:F2} seconds");
                report.AppendLine();
                report.AppendLine($"Average time per record: {randomizeDuration.TotalMilliseconds / updatedPeople.Count:F2}ms");
                report.AppendLine();
                report.AppendLine("Click 'Sync Changes' to persist to server.");

                Debug.WriteLine(report.ToString());

                await Application.Current.MainPage.DisplayAlert(
                    "Randomization Complete",
                    report.ToString(),
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in RandomizeDataAsync: {ex.Message}");
                await Application.Current.MainPage.DisplayAlert(
                    "Error",
                    "Failed to randomize data. Please try again.",
                    "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SyncChangesAsync()
        {
            if (IsBusy) return;

            try
            {
                IsBusy = true;

                var unsyncedPeople = await _databaseService.GetUnsyncedPeopleAsync();
                Debug.WriteLine($"Found {unsyncedPeople.Count} unsynced records");

                if (!unsyncedPeople.Any())
                {
                    await Application.Current.MainPage.DisplayAlert(
                        "No Changes",
                        "There are no changes to sync.",
                        "OK");
                    return;
                }

                if (_connectivity.NetworkAccess != NetworkAccess.Internet)
                {
                    await Application.Current.MainPage.DisplayAlert(
                        "No Internet Connection",
                        "You're offline. Changes will be synced when internet connection is restored.",
                        "OK");
                    return;
                }

                var shouldSync = await Application.Current.MainPage.DisplayAlert(
                    "Confirm Sync",
                    $"There are {unsyncedPeople.Count} records to sync. Continue?",
                    "Yes", "No");

                if (!shouldSync) return;

                var totalStartTime = DateTime.Now;
                var successCount = 0;
                var failureCount = 0;
                var totalDuration = TimeSpan.Zero;

                // Criar lotes menores para processamento mais eficiente
                const int batchSize = 50;
                var batches = unsyncedPeople
                    .Select((x, i) => new { Index = i, Value = x })
                    .GroupBy(x => x.Index / batchSize)
                    .Select(g => g.Select(x => x.Value).ToList())
                    .ToList();

                Debug.WriteLine($"Processing {batches.Count} batches of {batchSize} records each");

                // Processar os lotes em paralelo com limite de concorrência
                using var semaphore = new SemaphoreSlim(5); // Limitar a 5 chamadas simultâneas
                var tasks = new List<Task>();

                foreach (var batch in batches)
                {
                    await semaphore.WaitAsync();

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var batchStartTime = DateTime.Now;
                            var batchSuccess = 0;
                            var batchUpdates = new List<(int Id, DateTime SyncTime)>();

                            foreach (var person in batch)
                            {
                                try
                                {
                                    var response = await _grpcClient.UpdatePersonAsync(person);
                                    if (response.Saved)
                                    {
                                        batchSuccess++;
                                        batchUpdates.Add((person.Id, DateTime.Now));
                                    }
                                    else
                                    {
                                        Debug.WriteLine($"Failed to update person {person.Id}: {response.Message}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Error updating person {person.Id}: {ex.Message}");
                                }
                            }

                            // Atualizar registros em lote no SQLite
                            if (batchUpdates.Any())
                            {
                                await _databaseService.MarkMultipleAsSyncedAsync(
                                    batchUpdates.Select(x => x.Id).ToList(),
                                    DateTime.Now);
                            }

                            Interlocked.Add(ref successCount, batchSuccess);
                            Interlocked.Add(ref failureCount, batch.Count - batchSuccess);

                            var batchDuration = DateTime.Now - batchStartTime;
                            Debug.WriteLine($"Batch completed: {batchSuccess}/{batch.Count} in {batchDuration.TotalSeconds:F2}s");
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }

                // Aguardar todas as tarefas completarem
                await Task.WhenAll(tasks);

                var totalTime = DateTime.Now - totalStartTime;

                // Atualizar a interface
                await LoadPageAsync(_currentPage);

                // Preparar relatório detalhado
                var report = new StringBuilder();
                report.AppendLine($"Sync Complete:");
                report.AppendLine($"Total records processed: {unsyncedPeople.Count}");
                report.AppendLine($"Successful: {successCount}");
                report.AppendLine($"Failed: {failureCount}");
                report.AppendLine();
                report.AppendLine($"Total time: {totalTime.TotalSeconds:F2} seconds");
                report.AppendLine($"Average time per record: {totalTime.TotalMilliseconds / unsyncedPeople.Count:F2} ms");
                report.AppendLine($"Throughput: {successCount / totalTime.TotalSeconds:F2} records/second");

                Debug.WriteLine(report.ToString());

                await Application.Current.MainPage.DisplayAlert(
                    successCount == unsyncedPeople.Count ? "Sync Complete" : "Sync Partially Complete",
                    report.ToString(),
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in SyncChangesAsync: {ex.Message}");
                await Application.Current.MainPage.DisplayAlert(
                    "Sync Error",
                    "An error occurred while trying to sync changes.",
                    "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task InitializeDataAsync()
        {
            try
            {
                IsBusy = true;
                Debug.WriteLine("Loading initial data...");

                // Carregar primeira página
                await LoadPageAsync(1);

                Debug.WriteLine("Initial data loaded successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading initial data: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        //REST
        private async Task SyncAllRestAsync()
        {
            if (IsBusy) return;

            try
            {
                IsBusy = true;

                var shouldSync = await Application.Current.MainPage.DisplayAlert(
                    "REST Full Sync",
                    "This will download all records from the server using REST. Continue?",
                    "Yes", "No");

                if (!shouldSync) return;

                var totalStartTime = DateTime.Now;

                // Tempo para obter dados via REST
                Debug.WriteLine("Starting to fetch data from REST server");
                var fetchStartTime = DateTime.Now;
                var (serverPeople, fetchDuration) = await _restClient.GetAllPeopleAsync();
                Debug.WriteLine($"Received {serverPeople.Count} records from server");

                if (serverPeople.Any())
                {
                    // Tempo para salvar no SQLite
                    Debug.WriteLine("Converting and saving to SQLite");
                    var saveStartTime = DateTime.Now;

                    await _databaseService.ClearAllDataAsync();
                    await _databaseService.BulkInsertPeopleAsync(serverPeople);

                    var saveDuration = DateTime.Now - saveStartTime;
                    var totalTime = DateTime.Now - totalStartTime;

                    // Recarrega a primeira página
                    await LoadPageAsync(1);

                    // Prepara relatório detalhado
                    var report = new StringBuilder();
                    report.AppendLine($"REST Sync Complete:");
                    report.AppendLine($"Total records: {serverPeople.Count}");
                    report.AppendLine();
                    report.AppendLine($"Fetch from REST API: {fetchDuration.TotalSeconds:F2} seconds");
                    report.AppendLine($"Save to SQLite: {saveDuration.TotalSeconds:F2} seconds");
                    report.AppendLine($"Total time: {totalTime.TotalSeconds:F2} seconds");
                    report.AppendLine();
                    report.AppendLine($"Average fetch time: {(fetchDuration.TotalMilliseconds / serverPeople.Count):F2} ms/record");
                    report.AppendLine($"Average save time: {(saveDuration.TotalMilliseconds / serverPeople.Count):F2} ms/record");
                    report.AppendLine($"Throughput: {serverPeople.Count / totalTime.TotalSeconds:F2} records/second");

                    Debug.WriteLine(report.ToString());

                    await Application.Current.MainPage.DisplayAlert(
                        "REST Sync Complete",
                        report.ToString(),
                        "OK");
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert(
                        "REST Sync Complete",
                        "No records found on server",
                        "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in REST sync: {ex.Message}");
                await Application.Current.MainPage.DisplayAlert(
                    "REST Sync Error",
                    $"Failed to synchronize data from server: {ex.Message}",
                    "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SyncChangesRestAsync()
        {
            if (IsBusy) return;

            try
            {
                IsBusy = true;

                var unsyncedPeople = await _databaseService.GetUnsyncedPeopleAsync();
                Debug.WriteLine($"Found {unsyncedPeople.Count} unsynced records");

                if (!unsyncedPeople.Any())
                {
                    await Application.Current.MainPage.DisplayAlert(
                        "No Changes",
                        "There are no changes to sync.",
                        "OK");
                    return;
                }

                if (_connectivity.NetworkAccess != NetworkAccess.Internet)
                {
                    await Application.Current.MainPage.DisplayAlert(
                        "No Internet Connection",
                        "You're offline. Changes will be synced when internet connection is restored.",
                        "OK");
                    return;
                }

                var shouldSync = await Application.Current.MainPage.DisplayAlert(
                    "Confirm REST Sync",
                    $"There are {unsyncedPeople.Count} records to sync. Continue?",
                    "Yes", "No");

                if (!shouldSync) return;

                var totalStartTime = DateTime.Now;
                var successCount = 0;
                var failureCount = 0;
                var totalDuration = TimeSpan.Zero;

                // Criar lotes para melhor performance
                var batchSize = 100;
                var batches = unsyncedPeople
                    .Select((x, i) => new { Index = i, Value = x })
                    .GroupBy(x => x.Index / batchSize)
                    .Select(g => g.Select(x => x.Value).ToList())
                    .ToList();

                Debug.WriteLine($"Starting REST sync of {batches.Count} batches of {batchSize} records each");

                var batchNumber = 1;
                foreach (var batch in batches)
                {
                    var batchStartTime = DateTime.Now;
                    var batchSuccess = 0;

                    foreach (var person in batch)
                    {
                        try
                        {
                            var (response, duration) = await _restClient.UpdatePersonAsync(person);

                            person.IsSynced = true;
                            person.LastSyncAttempt = DateTime.Now;
                            await _databaseService.UpdatePersonAsync(person);
                            successCount++;
                            batchSuccess++;
                            totalDuration += duration;
                        }
                        catch (Exception ex)
                        {
                            failureCount++;
                            Debug.WriteLine($"Error syncing person {person.Id}: {ex.Message}");
                        }
                    }

                    var batchDuration = DateTime.Now - batchStartTime;
                    Debug.WriteLine($"Batch {batchNumber}/{batches.Count} completed: " +
                                  $"{batchSuccess}/{batch.Count} successful in {batchDuration.TotalSeconds:F2}s");
                    batchNumber++;
                }

                var totalTime = DateTime.Now - totalStartTime;
                var averageTime = successCount > 0 ? totalDuration.TotalMilliseconds / successCount : 0;

                // Atualiza a interface
                await LoadPageAsync(_currentPage);

                // Prepara relatório detalhado
                var report = new StringBuilder();
                report.AppendLine($"REST Sync Complete:");
                report.AppendLine($"Total records processed: {unsyncedPeople.Count}");
                report.AppendLine($"Successful: {successCount}");
                report.AppendLine($"Failed: {failureCount}");
                report.AppendLine();
                report.AppendLine($"Total time: {totalTime.TotalSeconds:F2} seconds");
                report.AppendLine($"Average time per record: {averageTime:F2} ms");
                report.AppendLine($"Throughput: {successCount / totalTime.TotalSeconds:F2} records/second");

                Debug.WriteLine(report.ToString());

                await Application.Current.MainPage.DisplayAlert(
                    "REST Sync Results",
                    report.ToString(),
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in REST sync changes: {ex.Message}");
                await Application.Current.MainPage.DisplayAlert(
                    "Sync Error",
                    "An error occurred while trying to sync changes.",
                    "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion
    }
}