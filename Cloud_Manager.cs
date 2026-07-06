using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using Google.Cloud.Firestore;

namespace SurfOS2
{
    internal class Cloud_Manager
    {
        private static FirestoreDb? _db;
        private const string ProjectId = "surfos-5b9af";

        public static void StartInBackground()
        {
            _ = Task.Run(async () =>
            {
                InitializeCloud();
                if (_db is null)
                {
                    return;
                }

                try
                {
                    await _db.Collection("test_pings")
                        .Document(Environment.MachineName)
                        .SetAsync(new
                        {
                            Timestamp = DateTime.UtcNow,
                            Status = "Online"
                        });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Cloud Startup Error]: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Initializes the global connection loop to Firestore using the local key file.
        /// </summary>
        public static void InitializeCloud(bool silent = false)
        {
            try
            {
                // 1. Locate the credential key file
                string keyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "secrets.json");

                if (!File.Exists(keyPath))
                {
                    if (!silent)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("🚨 Cloud Boot Error: 'secrets.json' was not found in the operating path.");
                        Console.WriteLine($"🔍 System is looking inside: {AppDomain.CurrentDomain.BaseDirectory}");
                        Console.WriteLine("💡 Make sure your credential key is placed inside the execution folder!");
                        Console.ResetColor();
                    }
                    return;
                }

                // 2. Set the environment variable so Google's SDK automatically registers the key
                Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", keyPath);

                // 3. Initialize the database context instance
                _db = FirestoreDb.Create(ProjectId);
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"🚨 Cloud Initialization failed: {ex.Message}");
                    Console.ResetColor();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[Cloud Initialization Error]: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Updates the user's active cloud document with a live timestamp to signal they are online.
        /// </summary>
        public static async Task RegisterUserHeartbeatAsync(string username)
        {
            if (_db == null) return;

            try
            {
                // Reference path following our architectural schema: artifacts -> surfos-app-id -> users -> unique_user
                DocumentReference userDocRef = _db
                    .Collection("artifacts")
                    .Document("surfos-app-id")
                    .Collection("users")
                    .Document($"userId_{username}");

                // Create a data payload with the current universal timestamp
                Dictionary<string, object> presenceData = new()
                {
                    { "username", username },
                    { "lastSeen", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") }
                };

                // Write the document asynchronously to the cloud (creates or overwrites)
                await userDocRef.SetAsync(presenceData, SetOptions.MergeAll);
            }
            catch (Exception ex)
            {
                // Silently handle network hiccup logs so it doesn't interrupt the TUI experience
                System.Diagnostics.Debug.WriteLine($"[Cloud Presence Error]: {ex.Message}");
            }
        }

        /// <summary>
        /// Global property accessor to execute database transactions across adjacent apps
        /// </summary>
        public static FirestoreDb? DB
        {
            get { return _db; }
        }
    }
}
