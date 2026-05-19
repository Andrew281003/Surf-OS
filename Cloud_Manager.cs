using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using Google.Cloud.Firestore;

namespace SurfOS2
{
    internal class Cloud_Manager
    {
        // Nullable (?) added to fix the compiler warnings
        private static FirestoreDb? _db;
        private static string _projectId = "surfos-5b9af"; // Your official project ID

        /// <summary>
        /// Initializes the global connection loop to Firestore using the local key file.
        /// </summary>
        public static void InitializeCloud()
        {
            try
            {
                // 1. Locate the credential key file
                string keyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "secrets.json");

                if (!File.Exists(keyPath))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("🚨 Cloud Boot Error: 'secrets.json' was not found in the operating path.");
                    Console.WriteLine($"🔍 System is looking inside: {AppDomain.CurrentDomain.BaseDirectory}");
                    Console.WriteLine("💡 Make sure your credential key is placed inside the execution folder!");
                    Console.ResetColor();
                    return;
                }

                // 2. Set the environment variable so Google's SDK automatically registers the key
                Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", keyPath);

                // 3. Initialize the database context instance
                _db = FirestoreDb.Create(_projectId);

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("🛰️  Network Handshake: Connected successfully to the global SurfOS Cloud Database!");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"🚨 Cloud Initialization failed: {ex.Message}");
                Console.ResetColor();
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
                Dictionary<string, object> presenceData = new Dictionary<string, object>
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