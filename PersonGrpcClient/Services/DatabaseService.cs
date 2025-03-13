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

        public async Task MarkAsSyncedAsync(int localId, int serverId)
        {
            try
            {
                var person = await _database.GetAsync<Person>(localId);
                if (person != null && !person.IsSynced) // Verifica se já não está sincronizado
                {
                    person.IsSynced = true;
                    person.ServerId = serverId;
                    person.LastSyncAttempt = DateTime.Now;
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
    }
}


