using SQLite;
using System.Diagnostics;

namespace PersonGrpcClient.Models
{
    // Models/Person.cs
   public class Person
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public int? ServerId { get; set; }
        public string LocalId { get; set; }
        public string Name { get; set; }
        public string LastName { get; set; }
        public int Age { get; set; }
        public double Weight { get; set; }
        public bool IsSynced { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastSyncAttempt { get; set; }
    }

    public enum SyncStatus
    {
        LocallySaved,
        Synced,
        PendingSync,
        SyncFailed,
        SyncInProgress
    }

    public class PersonDisplay : Person
    {
        public SyncStatus Status { get; set; }
        public int? ServerId { get; set; }
        public DateTime? LastSyncAttempt { get; set; }

        public string DisplayStatus
        {
            get
            {
                return Status switch
                {
                    SyncStatus.LocallySaved => "📱 Locally Saved",
                    SyncStatus.Synced => "✅ Synced",
                    SyncStatus.PendingSync => "🔄 Pending Sync",
                    SyncStatus.SyncFailed => "❌ Sync Failed",
                    SyncStatus.SyncInProgress => "⏳ Syncing...",
                    _ => "Unknown Status"
                };
            }
        }

        public string DetailedStatus
        {
            get
            {
                var details = new List<string>();

                switch (Status)
                {
                    case SyncStatus.LocallySaved:
                        details.Add($"Status: Not Synced");
                        details.Add($"Created: {CreatedAt:dd/MM/yyyy HH:mm}");
                        details.Add("Waiting for connection...");
                        break;

                    case SyncStatus.Synced:
                        details.Add($"Server ID: {ServerId}");
                        details.Add($"Created: {CreatedAt:dd/MM/yyyy HH:mm}");
                        if (LastSyncAttempt.HasValue)
                        {
                            details.Add($"Synced: {LastSyncAttempt.Value:dd/MM/yyyy HH:mm}");
                        }
                        break;

                    case SyncStatus.PendingSync:
                        details.Add("Status: Pending Sync");
                        details.Add($"Created: {CreatedAt:dd/MM/yyyy HH:mm}");
                        details.Add("Waiting in queue...");
                        break;

                    case SyncStatus.SyncFailed:
                        details.Add("Status: Sync Failed");
                        details.Add($"Created: {CreatedAt:dd/MM/yyyy HH:mm}");
                        if (LastSyncAttempt.HasValue)
                        {
                            details.Add($"Last Try: {LastSyncAttempt.Value:dd/MM/yyyy HH:mm}");
                        }
                        details.Add("Will retry when online");
                        break;

                    case SyncStatus.SyncInProgress:
                        details.Add("Status: Syncing...");
                        details.Add($"Created: {CreatedAt:dd/MM/yyyy HH:mm}");
                        details.Add("Please wait");
                        break;
                }

                return string.Join("\n", details);
            }
        }

        public Color StatusColor
        {
            get
            {
                return Status switch
                {
                    SyncStatus.LocallySaved => Colors.Orange,
                    SyncStatus.Synced => Colors.Green,
                    SyncStatus.PendingSync => Colors.Blue,
                    SyncStatus.SyncFailed => Colors.Red,
                    SyncStatus.SyncInProgress => Colors.Purple,
                    _ => Colors.Gray
                };
            }
        }
    }

}

