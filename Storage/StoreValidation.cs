using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Landoria.WorldCrawler.Storage
{
    // Validates archive boundaries and generates paths only from trusted coordinates.
    internal static class StoreValidation
    {
        // Uses the world name and UID as the visible export directory.
        internal static string DirectoryName(WorldIdentity world, string scope = null)
        {
            ValidateScope(scope);
            var invalid = Path.GetInvalidFileNameChars();
            var clean = new string(world.Name.Select(value => invalid.Contains(value) ||
                value == '/' || value == '\\' || char.IsControl(value) ? '_' : value).Take(60).ToArray())
                .Trim(' ', '.');
            if (clean.Length == 0)
            {
                clean = "World";
            }
            return clean + "_" + world.Uid.ToString(CultureInfo.InvariantCulture) +
                (scope == null ? "" : "_" + scope);
        }

        // Allows an optional short ASCII suffix without filesystem separators or traversal syntax.
        private static void ValidateScope(string scope)
        {
            if (scope != null && (scope.Length == 0 || scope.Length > 60 || scope.Any(value =>
                !(value >= 'a' && value <= 'z' || value >= 'A' && value <= 'Z' ||
                  value >= '0' && value <= '9' || value == '_' || value == '-'))))
            {
                throw new ArgumentException("The export scope must use 1 to 60 ASCII letters, digits, underscores or hyphens.", nameof(scope));
            }
        }

        // Ignores manifest paths and constructs one safe basename for a zone.
        internal static string ZoneFileName(int x, int z)
        {
            return "zone_" + x.ToString(CultureInfo.InvariantCulture) + "_" +
                z.ToString(CultureInfo.InvariantCulture) + ".worldcrawler.json";
        }

        // Computes a portable lowercase SHA-256 digest.
        internal static string Hash(byte[] bytes)
        {
            using (var algorithm = SHA256.Create())
            {
                return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            }
        }

        // Rejects malformed manifests before any path or capture is trusted.
        internal static void Manifest(WorldManifest manifest, WorldIdentity expected)
        {
            if (manifest == null || manifest.World == null || manifest.Zones == null)
            {
                throw new InvalidDataException("Invalid World Crawler manifest.");
            }
            if (manifest.FormatVersion != 1 && manifest.FormatVersion != 2)
            {
                throw new NotSupportedException("Unsupported World Crawler manifest version.");
            }
            if (!expected.Matches(manifest.World))
            {
                throw new InvalidOperationException("World UID, seed, or generation version differs from the saved crawl.");
            }
            if (manifest.InventoryInitialized && string.IsNullOrEmpty(manifest.CharacterId))
            {
                throw new InvalidDataException("The inventory has no source character identity.");
            }
            if (manifest.ReceivedPrefabs != null && !manifest.ReceivedPrefabs.SequenceEqual(
                manifest.ReceivedPrefabs.Where(name => !string.IsNullOrEmpty(name))
                    .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(name => name, StringComparer.Ordinal)))
            {
                throw new InvalidDataException("Received prefab names must be distinct and sorted.");
            }
            var coordinates = new HashSet<long>();
            foreach (var zone in manifest.Zones)
            {
                ValidateEntry(zone);
                if (!coordinates.Add(((long)zone.X << 32) | (uint)zone.Z))
                {
                    throw new InvalidDataException("The inventory contains duplicate zone coordinates.");
                }
            }
        }

        // Checks metadata and payload bytes before accepting a zone as captured.
        internal static byte[] Envelope(ZoneEnvelope envelope, WorldIdentity expected, int x, int z)
        {
            if (envelope != null && envelope.FormatVersion != 2)
            {
                throw new NotSupportedException("Unsupported World Crawler zone version; existing data was preserved.");
            }
            if (envelope == null || !expected.Matches(envelope.World) ||
                envelope.X != x || envelope.Z != z || envelope.ObjectCount < 0 ||
                string.IsNullOrEmpty(envelope.CaptureVersion) || envelope.Payload == null)
            {
                throw new InvalidDataException("Zone header, world identity, or coordinates are invalid.");
            }
            var payload = envelope.Payload.Encode();
            if (payload.Length != envelope.PayloadLength || !string.Equals(Hash(payload), envelope.Checksum,
                StringComparison.Ordinal))
            {
                throw new InvalidDataException("Zone payload length or SHA-256 checksum does not match.");
            }
            return payload;
        }

        // Validates each inventory record without resolving untrusted file paths.
        private static void ValidateEntry(ZoneEntry zone)
        {
            if (zone == null || (zone.Origin != ExplorationOrigin.PointOfInterest && zone.Origin != ExplorationOrigin.Received) ||
                !new[] { "pending", "loaded", "captured", "skipped", "failed" }.Contains(zone.Status))
            {
                throw new InvalidDataException("An inventory entry has invalid origin or status.");
            }
            if (!string.IsNullOrEmpty(zone.FileName) && zone.FileName != ZoneFileName(zone.X, zone.Z))
            {
                throw new InvalidDataException("An inventory filename does not match its zone coordinates.");
            }
        }

    }
}
