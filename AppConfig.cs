using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RedfurSync
{
    public class AppConfig
    {
        // Network variables
        public string ServerUrl    { get; set; } = "http://47.135.77.158:3000/upload";
        public string UpdateUrl    { get; set; } = "http://47.135.77.158:3000/update/check";
        public string ApiKey       { get; set; } = "872615399f313ef9920a4b4a51df66d51f2c179ee4c3c70fb289df7178479180";
        public string DisplayName  { get; set; } = "Redfur Trader";
        
        // Polling and application state behavior variables
        /// <summary>
        /// Delay before dispatching file change events (in milliseconds)
        /// </summary>
        public int    DebounceMs   { get; set; } = 4000;
        
        /// <summary>
        /// Enable or disable launching the application at Windows startup
        /// </summary>
        public bool   RunOnStartup { get; set; } = true;
        
        /// <summary>
        /// Stores the timestamp to avoid redundant update prompt alerts
        /// </summary>
        public DateTime LastUpdatePrompt { get; set; } = DateTime.MinValue;
        
        // ── Fissal's remembered visual state ──
        /// <summary>
        /// Selects the rendering style (Low, Medium, High) modifying CPU load vs Graphical complexity
        /// </summary>
        public UploadProgressForm.AppConfig.FidelityMode VisualFidelity { get; set; } = UploadProgressForm.AppConfig.FidelityMode.Medium;
        private static readonly JsonSerializerOptions _opts = new() 
        { 
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static string ConfigDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FissalCogworkCourier");

        public static string ConfigPath { get; } = Path.Combine(ConfigDirectory, "config.json");
        public static AppConfig Instance { get; } = Load();

        public static AppConfig Load()
        {
            Directory.CreateDirectory(ConfigDirectory);
            if (!File.Exists(ConfigPath)) { var d = new AppConfig(); Save(d); return d; }
            try { return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), _opts) ?? new(); }
            catch { return new(); }
        }

        public static void Save(AppConfig cfg)
        {
            Directory.CreateDirectory(ConfigDirectory);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg, _opts));
        }

        public bool IsConfigured() =>
            !string.IsNullOrWhiteSpace(ServerUrl)  && ServerUrl  != "http://YOUR_SERVER_URL/upload" &&
            !string.IsNullOrWhiteSpace(ApiKey)     && ApiKey     != "YOUR_API_KEY_HERE";
    }
}