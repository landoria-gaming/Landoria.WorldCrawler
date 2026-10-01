using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Landoria.WorldCrawler.Storage
{
    // Validates archive boundaries and generates paths only from trusted coordinates.
    internal static class StoreValidation
    {
        // Produces a collision-resistant world folder with visible UID and seed.
        internal static string DirectoryName(WorldIdentity world, string scope = null)
        {
            ValidateScope(scope);
            var clean = new string(world.SeedText.Select(c =>
                c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' ||
                c == '-' || c == '_' ? c : '_').Take(40).ToArray());
            if (clean.Length == 0)
            {
                clean = "empty";
            }
            return "world_" + world.Uid.ToString(CultureInfo.InvariantCulture) + "_" + clean + "_" +
                world.Seed.ToString(CultureInfo.InvariantCulture) + "_" +
                Hash(Encoding.UTF8.GetBytes(world.SeedText)).Substring(0, 12) + (scope == null ? "" : "_" + scope);
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
            if (manifest.FormatVersion != 1)
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
            if (manifest.InventoryInitialized && manifest.Selection == null)
            {
                throw new NotSupportedException("Only landmark exports are supported. Start a new landmark export.");
            }
            if (!manifest.InventoryInitialized && (manifest.Zones.Count != 0 || manifest.Selection != null))
            {
                throw new InvalidDataException("An uninitialized inventory cannot contain zones.");
            }
            ValidateProgress(manifest);
            LandmarkSelectionBuilder.Validate(manifest.Selection);
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
            if (envelope != null && envelope.FormatVersion != 1)
            {
                throw new NotSupportedException("Unsupported World Crawler zone version; existing data was preserved.");
            }
            if (envelope == null || !expected.Matches(envelope.World) ||
                envelope.X != x || envelope.Z != z || envelope.ObjectCount < 0 ||
                string.IsNullOrEmpty(envelope.CaptureVersion) || envelope.PayloadBase64 == null)
            {
                throw new InvalidDataException("Zone header, world identity, or coordinates are invalid.");
            }
            byte[] payload;
            try
            {
                payload = Convert.FromBase64String(envelope.PayloadBase64);
            }
            catch (FormatException error)
            {
                throw new InvalidDataException("Zone payload is not valid Base64.", error);
            }
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
            if (zone == null || zone.Origin != ExplorationOrigin.PointOfInterest ||
                !new[] { "pending", "loaded", "captured", "skipped", "failed" }.Contains(zone.Status))
            {
                throw new InvalidDataException("An inventory entry has invalid origin or status.");
            }
            if (!string.IsNullOrEmpty(zone.FileName) && zone.FileName != ZoneFileName(zone.X, zone.Z))
            {
                throw new InvalidDataException("An inventory filename does not match its zone coordinates.");
            }
        }

        // Ensures saved counters and return checkpoints can be interpreted safely.
        private static void ValidateProgress(WorldManifest manifest)
        {
            if (manifest.ReturnPending && (!manifest.InventoryInitialized || string.IsNullOrEmpty(manifest.CharacterId) ||
                string.IsNullOrEmpty(manifest.ReturnCharacterId) ||
                manifest.ReturnPosition == null || manifest.ReturnPosition.Length != 3 ||
                manifest.ReturnRotation == null || manifest.ReturnRotation.Length != 4 ||
                manifest.ReturnPosition.Concat(manifest.ReturnRotation).Any(value => float.IsNaN(value) || float.IsInfinity(value)) ||
                !manifest.ReturnRotation.Any(value => value != 0) ||
                manifest.ReturnCharacterId != manifest.CharacterId))
            {
                throw new InvalidDataException("The saved return point is incomplete or belongs to another character.");
            }
        }
    }
}
