using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Landoria.WorldCrawler.Restoration.Persistence;

namespace Landoria.WorldCrawler.Capture
{
    // Validates portable capture files without invoking Unity or deserializing game objects.
    internal static class SnapshotValidation
    {
        // Rejects incomplete envelopes, duplicate records, and unsupported interpretation flags.
        internal static void Validate(ZoneSnapshot snapshot)
        {
            if (snapshot.PayloadVersion < 1 || snapshot.PayloadVersion > 3)
            {
                throw new NotSupportedException("The zone payload version is unsupported.");
            }
            if (snapshot.AbsenceAuthoritative || snapshot.ObservationQuality !=
                (snapshot.PayloadVersion == 3 ? "received-object-cache" : "stable-client-observation"))
            {
                throw new InvalidDataException("Unsupported zone payload version or observation quality.");
            }
            if (snapshot.Objects == null || snapshot.SceneNodes == null || snapshot.PrefabCounts == null
                || snapshot.CategoryCounts == null || snapshot.ExclusionCounts == null || snapshot.Limitations == null
                || snapshot.ExcludedCategories == null || snapshot.StartedUtcTicks <= 0
                || snapshot.FinishedUtcTicks < snapshot.StartedUtcTicks || (snapshot.PayloadVersion < 3 &&
                (snapshot.ObservationPasses < 2 || !snapshot.TerrainReady)) || !Finite(snapshot.DwellSeconds) || !Finite(snapshot.StableSeconds)
                || snapshot.DwellSeconds < 0 || snapshot.StableSeconds < 0 || snapshot.InteriorObjectCount < 0
                || snapshot.DungeonExpected && !snapshot.DungeonEvidenceComplete)
            {
                throw new InvalidDataException("Zone observation metadata is missing or invalid.");
            }
            ValidateCoverage(snapshot);
            ValidateRemovals(snapshot);
            ValidateObjects(snapshot);
            ValidateNodes(snapshot.SceneNodes);
            ValidateCounts(snapshot.PrefabCounts);
            ValidateCounts(snapshot.CategoryCounts);
            ValidateCounts(snapshot.ExclusionCounts);
            var required = new[] { "Player", "Character", "Fish", "RandomFlyingBird" };
            if (required.Any(category => !snapshot.ExcludedCategories.Contains(category)))
            {
                throw new InvalidDataException("The zone payload lacks its required fauna protection policy.");
            }
        }

        // Keeps old quality flags distinct from the new explicit near/scene/deletion evidence.
        private static void ValidateCoverage(ZoneSnapshot snapshot)
        {
            if (snapshot.PayloadVersion == 3 && (snapshot.NearCoverageValidated || snapshot.InstancesValidated ||
                snapshot.SceneValidated || snapshot.NaturalAbsenceComplete || snapshot.Deletions == null || snapshot.Departures == null))
            {
                throw new InvalidDataException("Passive received data cannot claim full-zone coverage.");
            }
            if (snapshot.PayloadVersion == 2 && (!snapshot.NearCoverageValidated || !snapshot.InstancesValidated ||
                !snapshot.SceneValidated || snapshot.StableSeconds < 2f || snapshot.Deletions == null || snapshot.Departures == null ||
                snapshot.Departures.Any(item => item == null || !Identifier(item.SourceUser) || item.ObservedUtcTicks <= 0) ||
                snapshot.Deletions.Any(item => item == null || !Identifier(item.SourceUser) || item.ObservedUtcTicks <= 0)))
            {
                throw new InvalidDataException("The recording lacks full local coverage or valid deletion evidence.");
            }
        }

        // Requires explicit valid source identities for every deletion or sector crossing.
        private static void ValidateRemovals(ZoneSnapshot snapshot)
        {
            if (snapshot.PayloadVersion < 2)
            {
                return;
            }
            if (snapshot.Deletions == null || snapshot.Departures == null ||
                snapshot.Deletions.Any(v => v == null || !Identifier(v.SourceUser) || v.ObservedUtcTicks <= 0) ||
                snapshot.Departures.Any(v => v == null || !Identifier(v.SourceUser) || v.ObservedUtcTicks <= 0))
            {
                throw new InvalidDataException("Invalid explicit recording deletion or departure.");
            }
        }

        // Checks source identifiers, normalized metadata, and raw persistent-object headers.
        private static void ValidateObjects(ZoneSnapshot snapshot)
        {
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in snapshot.Objects)
            {
                if (item == null || string.IsNullOrEmpty(item.PrefabName) || string.IsNullOrEmpty(item.RawDataBase64)
                    || item.Categories == null || item.Categories.Any(string.IsNullOrEmpty)
                    || !Identifier(item.SourceUser) || !identifiers.Add(item.SourceUser + ":" + item.SourceId)
                    || item.ZoneX != snapshot.ZoneX || item.ZoneZ != snapshot.ZoneZ)
                {
                    throw new InvalidDataException("A captured object has missing, duplicate, or mismatched metadata.");
                }
                Transform(item.Position, 3);
                Transform(item.Rotation, 4);
                if (item.LocalScale != null)
                {
                    Transform(item.LocalScale, 3);
                }
                var raw = Convert.FromBase64String(item.RawDataBase64);
                if (raw.Length < 6 || (raw[1] & 1) == 0 || BitConverter.ToInt32(raw, 2) != item.PrefabHash)
                {
                    throw new InvalidDataException("A raw ZDO header disagrees with its persistent-object metadata.");
                }
                if (item.ConnectionType != 0 && !Identifier(item.ConnectionTargetUser))
                {
                    throw new InvalidDataException("A captured connection has an invalid target identifier.");
                }
            }
        }

        // Checks scene addresses and transform arrays independently of game assets.
        private static void ValidateNodes(IEnumerable<CapturedSceneNode> nodes)
        {
            var addresses = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.Root) || node.Path == null || node.Name == null
                    || node.Components == null || !addresses.Add(node.Root + ":" + node.Path))
                {
                    throw new InvalidDataException("A scene node has missing or duplicate metadata.");
                }
                Transform(node.Position, 3);
                Transform(node.Rotation, 4);
                Transform(node.LocalPosition, 3);
                Transform(node.LocalRotation, 4);
                Transform(node.LocalScale, 3);
            }
        }

        // Rejects corrupt summary records while allowing genuinely empty categories.
        private static void ValidateCounts(IEnumerable<CaptureCount> counts)
        {
            var labels = new HashSet<string>(StringComparer.Ordinal);
            foreach (var count in counts)
            {
                if (count == null || string.IsNullOrEmpty(count.Name) || count.Count < 0 || !labels.Add(count.Name))
                {
                    throw new InvalidDataException("A capture summary contains an invalid count.");
                }
            }
        }

        // Requires exact transform dimensions and finite components.
        private static void Transform(float[] values, int size)
        {
            if (values == null || values.Length != size || values.Any(value => !Finite(value)))
            {
                throw new InvalidDataException("A captured transform is missing or non-finite.");
            }
        }

        // Preserves 64-bit network identifiers as invariant strings in JSON.
        private static bool Identifier(string value)
        {
            long parsed;
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
        }

        // Excludes NaN and infinity from portable numeric fields.
        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
