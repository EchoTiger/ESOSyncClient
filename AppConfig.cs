using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RedfurSync
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class AppConfig
    {
        // Network variables
        [JsonPropertyName("ServerUrl")]
        public string ServerUrl    { get; set; } = "http://47.135.77.158:3000/upload";
        
        [JsonPropertyName("UpdateUrl")]
        public string UpdateUrl    { get; set; } = "http://47.135.77.158:3000/update/check";
        
        [JsonPropertyName("ApiKey")]
        public string ApiKey       { get; set; } = "872615399f313ef9920a4b4a51df66d51f2c179ee4c3c70fb289df7178479180";
        
        [JsonPropertyName("DisplayName")]
        public string DisplayName  { get; set; } = "Redfur Trader";
        
        // Polling and application state behavior variables
        [JsonPropertyName("DebounceMs")]
        public int    DebounceMs   { get; set; } = 4000;
        
        [JsonPropertyName("MaxLogsKept")]
        public int    MaxLogsKept  { get; set; } = 8;
        
        [JsonPropertyName("RunOnStartup")]
        public bool   RunOnStartup { get; set; } = true;
        
        [JsonPropertyName("LastUpdatePrompt")]
        public DateTime LastUpdatePrompt { get; set; } = DateTime.MinValue;
        
        // ── Fissal's remembered visual state ──
        [JsonPropertyName("VisualFidelity")]
        public UploadProgressForm.AppConfig.FidelityMode VisualFidelity { get; set; } = UploadProgressForm.AppConfig.FidelityMode.Medium;
        
        private static readonly JsonSerializerOptions _opts = new() 
        { 
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
            IncludeFields = true // An extra claw-hold just to be certain
        };

        public static string ConfigDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FissalCogworkCourier");

        public static string ConfigPath { get; } = Path.Combine(ConfigDirectory, "config.json");
        
        private static AppConfig? _instance;
        private static readonly object _fileLock = new object();

        // ── A singular, unified mind ──
        public static AppConfig Instance 
        {
            get 
            {
                lock (_fileLock) 
                {
                    if (_instance == null) _instance = LoadLocked();
                    return _instance;
                }
            }
        }

        private static AppConfig LoadLocked()
        {
            Directory.CreateDirectory(ConfigDirectory);
            if (!File.Exists(ConfigPath)) 
            { 
                var d = new AppConfig(); 
                SaveInternal(d); 
                return d; 
            }
            
            try 
            { 
                string json = File.ReadAllText(ConfigPath);
                if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
                {
                    var d = new AppConfig();
                    SaveInternal(d);
                    return d;
                }
                var config = JsonSerializer.Deserialize<AppConfig>(json, _opts) ?? new AppConfig();
                
                // Immediately re-save so defaults are stamped through the fog
                SaveInternal(config);
                
                return config;
            }
            catch 
            { 
                // If the file is deeply corrupted, she resets gracefully
                var d = new AppConfig();
                SaveInternal(d);
                return d; 
            }
        }

        public void Save()
        {
            lock (_fileLock)
            {
                SaveInternal(this);
            }
        }

        private static void SaveInternal(AppConfig cfg)
        {
            try 
            {
                Directory.CreateDirectory(ConfigDirectory);
                string json = JsonSerializer.Serialize(cfg, _opts);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Config Error] Fissal's claws slipped while writing memory: {ex.Message}");
            }
        }

        public bool IsConfigured() =>
            !string.IsNullOrWhiteSpace(ServerUrl)  && ServerUrl  != "http://YOUR_SERVER_URL/upload" &&
            !string.IsNullOrWhiteSpace(ApiKey)     && ApiKey     != "YOUR_API_KEY_HERE";
    }
}