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
        
        [JsonPropertyName("AppScale")]
        public float  AppScale { get; set; } = 1.0f;
        
        [JsonPropertyName("LastUpdatePrompt")]
        public DateTime LastUpdatePrompt { get; set; } = DateTime.MinValue;
        
        [JsonIgnore]
        public UploadProgressForm.AppConfig.FidelityMode VisualFidelity { get; set; } = UploadProgressForm.AppConfig.FidelityMode.Medium;

        [JsonPropertyName("VisualFidelity")]
        public int VisualFidelityInt 
        { 
            get => (int)VisualFidelity; 
            set 
            {
                if (Enum.IsDefined(typeof(UploadProgressForm.AppConfig.FidelityMode), value) && value != 0)
                    VisualFidelity = (UploadProgressForm.AppConfig.FidelityMode)value;
                else
                    VisualFidelity = UploadProgressForm.AppConfig.FidelityMode.Medium;
            }
        }
        
    private static readonly JsonSerializerOptions _opts = new() 
    { 
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        IncludeFields = true 
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
                
                // Immediately re-save so defaults are stamped
                SaveInternal(config);
                
                return config;
            }
            catch (Exception ex)
            { 
                System.Windows.Forms.MessageBox.Show($"Fissal's memory clouded:\n\n{ex.Message}\n\n{ex.StackTrace}", "Load Error");
                
                try 
                {
                    if (File.Exists(ConfigPath))
                    {
                        File.Copy(ConfigPath, ConfigPath + ".corrupted.bak", true);
                    }
                } 
                catch { } 

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
                System.Windows.Forms.MessageBox.Show($"Fissal's claws slipped while writing memory:\n\n{ex.Message}\n\n{ex.StackTrace}", "Save Error");
            }
        }

        public bool IsConfigured() =>
            !string.IsNullOrWhiteSpace(ServerUrl)  && ServerUrl  != "http://YOUR_SERVER_URL/upload" &&
            !string.IsNullOrWhiteSpace(ApiKey)     && ApiKey     != "YOUR_API_KEY_HERE";
    }
}