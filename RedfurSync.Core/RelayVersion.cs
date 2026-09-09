using System;
using System.Reflection;

namespace RedfurSync
{
    public static class RelayVersion
    {
        private static string? _cachedVersion;

        public static string Current
        {
            get
            {
                if (_cachedVersion != null) return _cachedVersion;

                // Check entry assembly first (RedfurSync.exe), then fallback to Core assembly
                var entry = Assembly.GetEntryAssembly();
                var ver = entry?.GetName().Version;

                if (ver == null || (ver.Major <= 1 && ver.Minor == 0 && ver.Build == 0))
                {
                    ver = typeof(RelayVersion).Assembly.GetName().Version;
                }

                _cachedVersion = ver != null ? $"{ver.Major}.{ver.Minor}.{Math.Max(0, ver.Build)}" : "1.4.3";
                return _cachedVersion;
            }
        }

        public static void ResetCacheForTesting()
        {
            _cachedVersion = null;
        }

        public static bool IsServerNewer(string? serverVersionStr, string? localVersionStr)
        {
            if (string.IsNullOrWhiteSpace(serverVersionStr) || string.IsNullOrWhiteSpace(localVersionStr))
                return false;

            var cleanServer = NormalizeVersionString(serverVersionStr);
            var cleanLocal = NormalizeVersionString(localVersionStr);

            if (Version.TryParse(cleanServer, out var serverVer) && Version.TryParse(cleanLocal, out var localVer))
            {
                return serverVer > localVer;
            }

            return false;
        }

        public static string NormalizeVersionString(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "0.0.0";
            var s = raw.Trim().TrimStart('v', 'V');
            int plus = s.IndexOf('+');
            if (plus >= 0) s = s[..plus];
            int dash = s.IndexOf('-');
            if (dash >= 0) s = s[..dash];
            return s.Trim();
        }
    }
}
