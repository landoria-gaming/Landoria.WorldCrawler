using System;

namespace Landoria.WorldCrawler.Storage
{
    // Shares version-family rules across runtime detection and stored export readers.
    internal static class SupportedGameVersions
    {
        // Accepts numeric 1.0 patch releases without accepting other branches or malformed suffixes.
        internal static bool IsCurrent(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Split('.').Length != 3)
            {
                return false;
            }
            foreach (var character in value)
            {
                if (character != '.' && (character < '0' || character > '9'))
                {
                    return false;
                }
            }
            return System.Version.TryParse(value, out var version) && version.Major == 1
                && version.Minor == 0 && version.Build >= 0;
        }

        // Keeps the verified legacy exporter alongside current-family captures.
        internal static bool CanExport(string value)
        {
            return value == "0.221.12" || IsCurrent(value);
        }
    }
}
