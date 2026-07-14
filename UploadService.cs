using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RedfurSync
{
    public class UploadService : IDisposable
    {
        private readonly HttpClient _syncHttp;
        private readonly HttpClient _updateHttp;
        private readonly AppConfig  _config;

        public string? LastError { get; private set; }

        public async Task<UpdatePayload?> CheckForUpdateAsync(string currentVersion)
        {
            try
            {
                if (!IsTrustedUpdateUri(_config.UpdateUrl, _config.UpdateUrl)) return null;
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var response = await _updateHttp.GetAsync(_config.UpdateUrl, cts.Token);
                
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                var payload = JsonSerializer.Deserialize<UpdatePayload>(json, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });
                
                if (payload != null && !string.IsNullOrWhiteSpace(payload.Version))
                {
                    // Parse both strings into proper Version objects
                    bool isServerVerValid = Version.TryParse(payload.Version, out Version serverVersion);
                    bool isLocalVerValid = Version.TryParse(currentVersion, out Version localVersion);

                    if (isServerVerValid && isLocalVerValid)
                    {
                        // Now it truly checks if the server is offering a BIGGER number
                        if (serverVersion > localVersion && IsValidUpdatePayload(payload))
                        {
                            return payload;
                        }
                    }
                    else if (payload.Version != currentVersion && IsValidUpdatePayload(payload))
                    {
                        // A soft fallback just in case non-standard strings (like "1.1a") are used
                        return payload;
                    }
                }
            }
            catch (Exception ex) 
            { 
                Console.WriteLine($"[Update Error] ✖ Error: {ex.Message}"); 
            }
            return null;
        }

        public async Task<bool> DownloadUpdateAsync(UploadJob job)
        {
            try
            {
                LastError = null;
                if (!IsTrustedUpdateUri(job.DownloadUrl, _config.UpdateUrl) || !IsValidSha256(job.UpdateSha256))
                    throw new InvalidOperationException("Update manifest did not provide a trusted HTTPS download and SHA-256 hash.");

                using var response = await _updateHttp.GetAsync(job.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, job.Cts.Token);
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;
                if (totalBytes.HasValue && (totalBytes.Value <= 0 || totalBytes.Value > 500L * 1024 * 1024))
                    throw new InvalidOperationException("Update size is outside the allowed limit.");
                await using var contentStream = await response.Content.ReadAsStreamAsync(job.Cts.Token);
                
                await using var fileStream = new FileStream(job.FilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, job.Cts.Token)) != 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, job.Cts.Token);
                    hasher.AppendData(buffer, 0, bytesRead);
                    totalRead += bytesRead;
                    if (totalRead > 500L * 1024 * 1024) throw new InvalidOperationException("Update exceeds the allowed size.");
                    if (totalBytes.HasValue)
                    {
                        job.Progress = (float)totalRead / totalBytes.Value;
                    }
                }

                var actualHash = Convert.ToHexString(hasher.GetHashAndReset());
                if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actualHash), Convert.FromHexString(job.UpdateSha256)))
                {
                    throw new InvalidOperationException("Downloaded update hash did not match the signed manifest.");
                }

                job.Progress = 1f;
                return true;
            }
            catch (Exception ex)
            {
                LastError = job.ErrorMessage = $"Download failed: {ex.Message}";
                return false;
            }
        }

        public UploadService(AppConfig config)
        {
            _config = config;
            _syncHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            _updateHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        }

        public async Task<(bool ok, string message)> PingAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                using var response = await _syncHttp.SendAsync(CreateSyncRequest(HttpMethod.Get, BuildHealthUri()), cts.Token);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    return (false, "Server reached but API key was rejected (401)");

                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    return (false, "Server reached but this client is not allowed (403)");

                if (!response.IsSuccessStatusCode)
                    return (false, $"Relay service is unhealthy: {(int)response.StatusCode} {response.ReasonPhrase}");

                return (true, "Connected — relay service is healthy");
            }
            catch (TaskCanceledException)
            {
                return (false, "Connection timed out — server did not respond in 8 s");
            }
            catch (HttpRequestException ex)
            {
                return (false, $"Cannot reach server: {ex.Message}");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private Uri BuildHealthUri()
        {
            if (!string.IsNullOrWhiteSpace(_config.DeviceToken)) return BuildRelayUri("/health");
            if (!Uri.TryCreate(_config.ServerUrl, UriKind.Absolute, out var endpoint))
                throw new InvalidOperationException("The configured server URL is invalid.");

            var builder = new UriBuilder(endpoint)
            {
                Path = endpoint.AbsolutePath.TrimEnd('/') + "/health"
            };

            return builder.Uri;
        }

        public async Task<(bool ok, string message)> PairAsync()
        {
            if (!string.IsNullOrWhiteSpace(_config.DeviceToken)) return (true, "Already paired");
            if (!string.IsNullOrWhiteSpace(_config.ApiKey)) return (true, "Legacy API key mode");
            if (string.IsNullOrWhiteSpace(_config.PairingCode)) return (false, "A pairing code is required.");
            try
            {
                var payload = JsonSerializer.Serialize(new { code = _config.PairingCode, deviceName = Environment.MachineName });
                using var request = new HttpRequestMessage(HttpMethod.Post, BuildRelayUri("/pair"))
                {
                    Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
                };
                using var response = await _syncHttp.SendAsync(request);
                if (!response.IsSuccessStatusCode) return (false, "Pairing code was rejected or expired.");
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (!json.RootElement.TryGetProperty("token", out var token) || string.IsNullOrWhiteSpace(token.GetString()))
                    return (false, "Pairing response did not include a device token.");
                _config.DeviceToken = token.GetString()!;
                _config.PairingCode = string.Empty;
                _config.Save();
                return (true, "Device paired");
            }
            catch (Exception ex)
            {
                return (false, $"Pairing failed: {ex.Message}");
            }
        }

        private Uri BuildUploadUri() => !string.IsNullOrWhiteSpace(_config.DeviceToken)
            ? BuildRelayUri("/files")
            : new Uri(_config.ServerUrl, UriKind.Absolute);

        private Uri BuildRelayUri(string suffix)
        {
            if (!Uri.TryCreate(_config.ServerUrl, UriKind.Absolute, out var endpoint))
                throw new InvalidOperationException("The configured server URL is invalid.");
            return new UriBuilder(endpoint.Scheme, endpoint.Host, endpoint.IsDefaultPort ? -1 : endpoint.Port, $"/api/relay/v1{suffix}").Uri;
        }

        private HttpRequestMessage CreateSyncRequest(HttpMethod method, Uri uri, HttpContent? content = null)
        {
            var request = new HttpRequestMessage(method, uri) { Content = content };
            if (!string.IsNullOrWhiteSpace(_config.DeviceToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.DeviceToken);
            else if (!string.IsNullOrWhiteSpace(_config.ApiKey))
                request.Headers.Add("X-Api-Key", _config.ApiKey);
            return request;
        }

        private static bool IsValidSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

        private static bool IsTrustedUpdateUri(string candidate, string manifestUrl)
        {
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var target) || !Uri.TryCreate(manifestUrl, UriKind.Absolute, out var manifest)) return false;
            return target.Scheme == Uri.UriSchemeHttps && manifest.Scheme == Uri.UriSchemeHttps && target.Host.Equals(manifest.Host, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsValidUpdatePayload(UpdatePayload payload) =>
            payload.SizeBytes > 0 && payload.SizeBytes <= 500L * 1024 * 1024 && IsValidSha256(payload.Sha256) && IsTrustedUpdateUri(payload.DownloadUrl, AppConfig.Instance.UpdateUrl);

        public async Task<bool> UploadAsync(UploadJob job)
        {
            try
            {
                LastError = null;

                var fileInfo = new FileInfo(job.FilePath);
                if (!fileInfo.Exists)
                {
                    LastError = job.ErrorMessage = "File not found on disk";
                    return false;
                }

                await using var fileStream = new FileStream(
                    job.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                await using var progressStream = new ProgressStream(
                    fileStream, fileInfo.Length, p => job.Progress = p);

                using var form = new MultipartFormDataContent();

                var streamContent = new StreamContent(progressStream);
                streamContent.Headers.ContentType      = new MediaTypeHeaderValue("application/octet-stream");
                streamContent.Headers.ContentLength    = fileInfo.Length;

                form.Add(streamContent, "file", job.FileName);
                
                if (string.IsNullOrWhiteSpace(_config.DeviceToken))
                {
                    string finalName = AppConfig.Instance.DisplayName;
                    if (string.IsNullOrWhiteSpace(finalName)) finalName = "Redfur Trader";
                    form.Add(new StringContent(finalName), "displayName");
                }

                using var request = CreateSyncRequest(HttpMethod.Post, BuildUploadUri(), form);
                var response = await _syncHttp.SendAsync(request, job.Cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    LastError = job.ErrorMessage =
                        $"Server returned {(int)response.StatusCode} {response.ReasonPhrase}" +
                        (string.IsNullOrWhiteSpace(body) ? "" : $": {body.Trim()}");
                    return false;
                }

                job.Progress = 1f;
                return true;
            }
            catch (OperationCanceledException)
            {
                if (job.Cts.Token.IsCancellationRequested)
                {
                    LastError = job.ErrorMessage = "Cancelled by user";
                }
                else
                {
                    LastError = job.ErrorMessage = "Upload timed out. The connection may be unstable or too slow.";
                }
                return false;
            }
            catch (IOException ex)
            {
                LastError = job.ErrorMessage = $"File IO error (possibly locked): {ex.Message}";
                return false;
            }
            catch (HttpRequestException ex)
            {
                LastError = job.ErrorMessage =
                    $"Connection failed: {ex.Message}" +
                    (ex.InnerException != null ? $" ({ex.InnerException.Message})" : "");
                return false;
            }
            catch (Exception ex)
            {
                LastError = job.ErrorMessage = ex.Message;
                return false;
            }
        }

        public void Dispose()
        {
            _syncHttp.Dispose();
            _updateHttp.Dispose();
        }
    }
}