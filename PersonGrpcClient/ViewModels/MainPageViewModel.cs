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

        private string _name;
        private string _lastName;
        private int? _age;
        private double? _weight;
        private bool _isBusy;
        private string _connectionStatusText;
        private Color _connectionStatusColor;
        private ObservableCollection<PersonDisplay> _savedPeople;

        public event PropertyChangedEventHandler PropertyChanged;
        public ICommand ClearDataCommand { get; }

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

            SavedPeople = new ObservableCollection<PersonDisplay>();
            SaveCommand = new Command(async () => await SavePersonAsync());
            ClearDataCommand = new Command(async () => await ClearDataAsync()); // Adicione esta linha

            _connectivity.ConnectivityChanged += OnConnectivityChanged;
            UpdateConnectionStatus();
            LoadSavedPeople();
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

        public ObservableCollection<PersonDisplay> SavedPeople
        {
            get => _savedPeople;
            set
            {
                _savedPeople = value;
                OnPropertyChanged();
            }
        }

        public ICommand SaveCommand { get; }

        private async Task SavePersonAsync()
        {
            if (IsBusy) return;

            // Lista para armazenar mensagens de erro
            var validationErrors = new List<string>();

            // Validação do Nome
            if (string.IsNullOrWhiteSpace(Name))
            {
                validationErrors.Add("First Name is required");
            }

            // Validação do Sobrenome
            if (string.IsNullOrWhiteSpace(LastName))
            {
                validationErrors.Add("Last Name is required");
            }

            // Validação da Idade
            if (!Age.HasValue || Age <= 0)
            {
                validationErrors.Add("Age must be greater than 0");
            }

            // Validação do Peso
            if (!Weight.HasValue || Weight <= 0)
            {
                validationErrors.Add("Weight must be greater than 0");
            }


            // Se houver erros, mostra todos de uma vez
            if (validationErrors.Any())
            {
                await Application.Current.MainPage.DisplayAlert(
                    "Validation Error",
                    string.Join("\n", validationErrors),
                    "OK");
                return;
            }

            // Confirmação antes de salvar
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
                    CreatedAt = DateTime.Now,  // Garante que a data de criação está definida
                    LastSyncAttempt = null     // Inicialmente null, será definido na sincronização
                };

                if (_connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    // Salvar diretamente no servidor
                    var response = await _grpcClient.SavePersonAsync(person);
                    if (response.Saved)
                    {
                        // Salvar localmente com a referência do ID do servidor
                        person.IsSynced = true;
                        person.ServerId = response.Id;
                        person.LastSyncAttempt = DateTime.Now;  // Define a data de sincronização
                        await _databaseService.SavePersonLocallyAsync(person);

                        // Atualizar a lista com o ID do servidor
                        var displayPerson = new PersonDisplay
                        {
                            Id = person.Id,
                            ServerId = response.Id,
                            Name = person.Name,
                            LastName = person.LastName,
                            Age = person.Age,
                            Weight = person.Weight,
                            Status = SyncStatus.Synced,
                            CreatedAt = person.CreatedAt,           // Copia a data de criação
                            LastSyncAttempt = person.LastSyncAttempt // Copia a data de sincronização
                        };

                        _dispatcher.Dispatch(() =>
                        {
                            SavedPeople.Add(displayPerson);
                        });

                        await Application.Current.MainPage.DisplayAlert("Success",
                            $"Person saved successfully with ID: {response.Id}", "OK");
                        ClearForm();
                    }
                }
                else
                {
                    // Salvar localmente
                    await _databaseService.SavePersonLocallyAsync(person);

                    var displayPerson = new PersonDisplay
                    {
                        Id = person.Id,
                        Name = person.Name,
                        LastName = person.LastName,
                        Age = person.Age,
                        Weight = person.Weight,
                        Status = SyncStatus.LocallySaved,
                        CreatedAt = person.CreatedAt,           // Copiando a data de criação
                        LastSyncAttempt = person.LastSyncAttempt // Copiando a data de sincronização
                    };

                    _dispatcher.Dispatch(() =>
                    {
                        SavedPeople.Add(displayPerson);
                    });

                    await Application.Current.MainPage.DisplayAlert("Success",
                        "Person saved locally and will be synced when online", "OK");
                    ClearForm();
                }
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Error",
                    $"Failed to save person: {ex.Message}", "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }

        // Método para forçar atualização da UI após sincronização
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
                CreatedAt = p.CreatedAt,           // Incluindo a data de criação
                LastSyncAttempt = p.LastSyncAttempt // Incluindo a data de sincronização
            }).ToList();

            _dispatcher.Dispatch(() =>
            {
                SavedPeople.Clear();
                foreach (var person in displayPeople)
                {
                    SavedPeople.Add(person);
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
            if (e.NetworkAccess == NetworkAccess.Internet)
            {
                await SyncPendingDataAsync();
            }
        }

        private async Task SyncPendingDataAsync()
        {
            try
            {
                var unsyncedPeople = await _databaseService.GetUnsyncedPeopleAsync();
                if (!unsyncedPeople.Any())
                {
                    Debug.WriteLine("No unsynced data to sync");
                    return;
                }

                Debug.WriteLine($"Found {unsyncedPeople.Count} unsynced records");

                // Criar uma lista para controlar os IDs processados
                var processedIds = new HashSet<int>();

                foreach (var person in unsyncedPeople)
                {
                    try
                    {
                        // Verifica se já processou este ID ou se já está sincronizado
                        if (processedIds.Contains(person.Id) || await _databaseService.IsPersonSyncedAsync(person.Id))
                        {
                            Debug.WriteLine($"Skipping already synced/processed person with ID {person.Id}");
                            continue;
                        }

                        // Atualiza status para sincronização em andamento
                        UpdatePersonStatus(person.Id, SyncStatus.SyncInProgress);
                        Debug.WriteLine($"Starting sync for person ID {person.Id}");

                        var response = await _grpcClient.SavePersonAsync(person);
                        if (response.Saved)
                        {
                            // Define a data de sincronização como agora
                            DateTime syncTime = DateTime.Now;

                            await _databaseService.MarkAsSyncedAsync(person.Id, response.Id, syncTime);
                            processedIds.Add(person.Id);
                            UpdatePersonStatus(person.Id, SyncStatus.Synced, response.Id, syncTime);
                            Debug.WriteLine($"Successfully synced person with local ID {person.Id}, server ID {response.Id} at {syncTime}");
                        }
                        else
                        {
                            UpdatePersonStatus(person.Id, SyncStatus.SyncFailed);
                            Debug.WriteLine($"Sync failed for person ID {person.Id}");
                        }
                    }
                    catch (Exception ex)
                    {
                        UpdatePersonStatus(person.Id, SyncStatus.SyncFailed);
                        Debug.WriteLine($"Error syncing person {person.Id}: {ex.Message}");
                        // Continua com o próximo registro em caso de erro
                    }
                }

                await LoadSavedPeople();

                if (processedIds.Any())
                {
                    _dispatcher.Dispatch(async () =>
                    {
                        await Application.Current.MainPage.DisplayAlert(
                            "Sync Complete",
                            $"Successfully synced {processedIds.Count} records\n" +
                            $"Sync completed at: {DateTime.Now:g}",
                            "OK");
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Sync Error: {ex.Message}");
                _dispatcher.Dispatch(async () =>
                {
                    await Application.Current.MainPage.DisplayAlert(
                        "Sync Error",
                        $"Failed to sync data: {ex.Message}",
                        "OK");
                });
            }
        }

        private void UpdatePersonStatus(int localId, SyncStatus status, int? serverId = null, DateTime? syncTime = null)
        {
            var person = SavedPeople.FirstOrDefault(p => p.Id == localId);
            if (person != null)
            {
                _dispatcher.Dispatch(() =>
                {
                    person.Status = status;
                    person.ServerId = serverId;
                    person.LastSyncAttempt = syncTime ?? DateTime.Now;

                    // Força atualização da UI
                    var index = SavedPeople.IndexOf(person);
                    if (index >= 0)
                    {
                        SavedPeople.RemoveAt(index);
                        SavedPeople.Insert(index, person);
                    }
                });
            }
        }

        // Adicione este método para atualizar o status no banco de dados
        private async Task UpdatePersonStatusInDatabase(int localId, SyncStatus status, int? serverId = null)
        {
            try
            {
                var person = await _databaseService.GetPersonAsync(localId);
                if (person != null)
                {
                    person.IsSynced = status == SyncStatus.Synced;
                    person.ServerId = serverId;
                    person.LastSyncAttempt = DateTime.Now;
                    await _databaseService.UpdatePersonAsync(person);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating person status in database: {ex.Message}");
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
                    await LoadSavedPeople(); // Atualiza a lista
                    await Application.Current.MainPage.DisplayAlert(
                        "Success",
                        "All local data has been cleared",
                        "OK");
                }
            }
            catch (Exception ex)
            {
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
    }
}