using System;
using System.Collections.Generic;
using System.IO;
using Landoria.WorldCrawler.Runtime;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Restoration
{
    // Finds the exact export recorded by a prepared local world after a game restart.
    internal static class ExportSourceResolver
    {
        // Accepts one strict series match and leaves ambiguous or missing sources untouched.
        internal static string Resolve(PreparedWorld marker)
        {
            if (string.IsNullOrEmpty(marker.SourceSeries) || !Directory.Exists(CrawlerConstants.ExportRoot))
            {
                throw Missing();
            }
            var matches = new List<string>();
            foreach (var directory in Directory.GetDirectories(CrawlerConstants.ExportRoot, "world_*"))
            {
                if (Matches(directory, marker))
                {
                    matches.Add(directory);
                }
            }
            if (matches.Count != 1)
            {
                throw matches.Count == 0 ? Missing() :
                    new InvalidOperationException("Multiple exports match this prepared world; select one with F9.");
            }
            return matches[0];
        }

        // Reads only the manifest and rejects unrelated or malformed export directories.
        private static bool Matches(string directory, PreparedWorld marker)
        {
            try
            {
                var manifest = AtomicJson.Read<WorldManifest>(Path.Combine(directory, "manifest.json"));
                StoreValidation.Manifest(manifest, marker.World);
                return manifest.CharacterId == marker.SourceCharacter &&
                    ExportSeries.Identity(manifest, directory) == marker.SourceSeries;
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException ||
                error is InvalidOperationException || error is NotSupportedException)
            {
                return false;
            }
        }

        // Explains the manual fallback when the original export is unavailable.
        private static InvalidOperationException Missing()
        {
            return new InvalidOperationException("The prepared world's original export was not found; select it with F9.");
        }
    }
}
