using SQLite;
using PersonGrpcClient.Models;
using System.Diagnostics;

namespace PersonGrpcClient.Services
{
   public class DatabaseService
    {
        private SQLiteAsyncConnection _database;

        public DatabaseService()
        {
            SetupDatabase();
        }

        private async void SetupDatabase()
        {
            if (_database != null)
                return;

            var databasePath = Path.Combine(FileSystem.AppDataDirectory, "people.db");
            _database = new SQLiteAsyncConnection(databasePath);
            await _database.CreateTableAsync<Person>();
        }

        public async Task<Person> SavePersonLocallyAsync(Person person)
        {
            if (person.CreatedAt == default)
            {
                person.CreatedAt = DateTime.Now;
            }
            await _database.InsertAsync(person);
            return person;
        }

        public async Task<Person> GetPersonAsync(int id)
        {
            return await _database.GetAsync<Person>(id);
        }

        public async Task UpdatePersonAsync(Person person)
        {
            await _database.UpdateAsync(person);
        }

        public async Task<List<Person>> GetUnsyncedPeopleAsync()
        {
            return await _database.Table<Person>()
                                .Where(p => !p.IsSynced)
                                .ToListAsync();
        }

        public async Task MarkAsSyncedAsync(int localId, int serverId, DateTime syncTime)
        {
            try
            {
                var person = await _database.GetAsync<Person>(localId);
                if (person != null && !person.IsSynced) // Verifica se já não está sincronizado
                {
                    person.IsSynced = true;
                    person.ServerId = serverId;
                    person.LastSyncAttempt = syncTime;
                    await _database.UpdateAsync(person);
                    Debug.WriteLine($"Marked person {localId} as synced with server ID {serverId}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error marking person as synced: {ex.Message}");
                throw;
            }
        }

        // Método para verificar se um registro já está sincronizado
        public async Task<bool> IsPersonSyncedAsync(int localId)
        {
            var person = await _database.GetAsync<Person>(localId);
            return person?.IsSynced ?? false;
        }

        public async Task<List<Person>> GetAllPeopleAsync()
        {
            return await _database.Table<Person>().ToListAsync();
        }

        public async Task ClearAllDataAsync()
        {
            try
            {
                await _database.DeleteAllAsync<Person>();
                Debug.WriteLine("Successfully cleared all data from SQLite");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error clearing database: {ex.Message}");
                throw;
            }
        }

        public async Task BulkInsertPeopleAsync(IEnumerable<Person> people)
        {
            try
            {
                var peopleList = people.ToList();
                Debug.WriteLine($"Starting bulk insert of {peopleList.Count} records");

                await _database.RunInTransactionAsync(tran =>
                {
                    foreach (var person in peopleList)
                    {
                        Debug.WriteLine($"Inserting - Name: {person.Name}, Created: {person.CreatedAt}");
                        tran.Insert(person);
                    }
                });

                // Verificar se os dados foram inseridos
                var count = await _database.Table<Person>().CountAsync();
                Debug.WriteLine($"Total records in database after insert: {count}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in bulk insert: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        public async Task<(List<Person> Items, int TotalCount)> GetPaginatedPeopleAsync(int page, int pageSize)
        {
            try
            {
                // Primeiro, obter o total de registros
                var totalCount = await _database.Table<Person>().CountAsync();
                Debug.WriteLine($"Total records in database: {totalCount}");

                // Calcular o offset baseado na página
                var offset = (page - 1) * pageSize;
                Debug.WriteLine($"Fetching page {page} with size {pageSize}, offset: {offset}");

                // Buscar apenas os registros da página atual
                var items = await _database.Table<Person>()
                    .OrderBy(p => p.Id)
                    .Skip(offset)
                    .Take(pageSize)
                    .ToListAsync();

                Debug.WriteLine($"Retrieved {items.Count} items for page {page}");
                return (items, totalCount);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetPaginatedPeopleAsync: {ex.Message}");
                throw;
            }
        }

        public async Task<List<Person>> UpdatePeopleRandomlyAsync()
        {
            try
            {
                var startTime = DateTime.Now;
                Debug.WriteLine("Starting random update");

                // Buscar todos os IDs primeiro (mais rápido que buscar registros completos)
                var people = await _database.Table<Person>().ToListAsync();
                var random = new Random();
                const int batchSize = 100;

                // Preparar todos os updates de uma vez
                for (int i = 0; i < people.Count; i++)
                {
                    people[i].Age = random.Next(18, 80);
                    people[i].Weight = Math.Round(random.NextDouble() * (120 - 50) + 50, 1);
                    people[i].LastSyncAttempt = null;
                    people[i].IsSynced = false;
                }

                // Executar em batches para melhor performance
                await _database.RunInTransactionAsync(tran =>
                {
                    for (int i = 0; i < people.Count; i += batchSize)
                    {
                        var batch = people.Skip(i).Take(batchSize);
                        foreach (var person in batch)
                        {
                            tran.Update(person);
                        }
                    }
                });

                var duration = DateTime.Now - startTime;
                Debug.WriteLine($"Randomized {people.Count} records in {duration.TotalSeconds:F2} seconds");
                Debug.WriteLine($"Average time per record: {duration.TotalMilliseconds / people.Count:F2}ms");

                return people;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating people randomly: {ex.Message}");
                throw;
            }
        }

        public async Task MarkMultipleAsSyncedAsync(List<int> ids, DateTime syncTime)
        {
            try
            {
                await _database.RunInTransactionAsync(tran =>
                {
                    var query = $"UPDATE Person SET IsSynced = 1, LastSyncAttempt = ? WHERE Id IN ({string.Join(",", ids)})";
                    tran.Execute(query, syncTime);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error marking multiple records as synced: {ex.Message}");
                throw;
            }
        }
    }
}


