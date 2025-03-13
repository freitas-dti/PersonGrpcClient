using SQLite;

namespace PersonGrpcClient.Models
{
    // Models/Person.cs
   public class Person
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public int? ServerId { get; set; }  // ID do servidor após sincronização
        public string Name { get; set; }
        public string LastName { get; set; }
        public int Age { get; set; }
        public double Weight { get; set; }
        public bool IsSynced { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public enum SyncStatus
    {
        LocallySaved,
        Synced,
        Failed
    }

    public class PersonDisplay : Person
    {
        public SyncStatus Status { get; set; }

        public string DisplayStatus => Status switch
        {
            SyncStatus.LocallySaved => "📱 Saved Locally (Pending Sync)",
            SyncStatus.Synced => $"✅ Synced (Server ID: {ServerId})",
            SyncStatus.Failed => "❌ Sync Failed",
            _ => "Unknown Status"
        };
    }

}

