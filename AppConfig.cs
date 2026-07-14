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
        public string ServerUrl    { get; set; } = "https://redfur.ech-o.net/upload";
        
        [JsonPropertyName("UpdateUrl")]
        public string UpdateUrl    { get; set; } = "https://redfur.ech-o.net/api/relay/v1/update-manifest";
        
        [JsonPropertyName("ApiKey")]
        public string ApiKey       { get; set; } = string.Empty;

        [JsonIgnore]
        public string DeviceToken  { get; set; } = string.Empty;

        [JsonPropertyName("DeviceTokenProtected")]
        public string DeviceTokenProtected
        {
            get
            {
                if (string.IsNullOrWhiteSpace(DeviceToken)) return string.Empty;
                try
                {
                    var encrypted = System.Security.Cryptography.ProtectedData.Protect(
                        System.Text.Encoding.UTF8.GetBytes(DeviceToken),
                        optionalEntropy: null,
                        System.Security.Cryptography.DataProtectionScope.CurrentUser);
                    return Convert.ToBase64String(encrypted);
                }
                catch { return string.Empty; }
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) { DeviceToken = string.Empty; return; }
                try
                {
                    var decrypted = System.Security.Cryptography.ProtectedData.Unprotect(
                        Convert.FromBase64String(value),
                        optionalEntropy: null,
                        System.Security.Cryptography.DataProtectionScope.CurrentUser);
                    DeviceToken = System.Text.Encoding.UTF8.GetString(decrypted);
                }
                catch { DeviceToken = string.Empty; }
            }
        }

        [JsonPropertyName("PairingCode")]
        public string PairingCode  { get; set; } = string.Empty;
        
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
                if (string.IsNullOrWhiteSpace(config.ServerUrl))
                    config.ServerUrl = "https://redfur.ech-o.net/upload";
                if (string.IsNullOrWhiteSpace(config.UpdateUrl))
                    config.UpdateUrl = "https://redfur.ech-o.net/api/relay/v1/update-manifest";
                
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

        public bool IsConfigured()
        {
            if (!IsSecureEndpoint(ServerUrl))
                return false;

            return string.IsNullOrWhiteSpace(UpdateUrl) || IsSecureEndpoint(UpdateUrl);
        }

        public static bool IsSecureEndpoint(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
                return false;

            if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrEmpty(uri.UserInfo);

            return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && uri.IsLoopback
                && string.IsNullOrEmpty(uri.UserInfo);
        }
    }
}