using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace Landoria.WorldCrawler.Restoration.Persistence
{
    // Stores only the world UID and zones with a confirmed native restoration save.
    [DataContract]
    internal sealed class RestoreProgress
    {
        [DataMember(Order = 0, IsRequired = true)] public long WorldUid;
        [DataMember(Order = 1, IsRequired = true)] public List<string> RestoredZones = new List<string>();

        // Rejects a mismatched world or malformed zone coordinates instead of resetting progress.
        internal void Validate(long uid)
        {
            if (WorldUid != uid || RestoredZones == null ||
                RestoredZones.Distinct().Count() != RestoredZones.Count || RestoredZones.Any(key => !ValidZone(key)))
            {
                throw new InvalidDataException("Invalid restoration zone progress or mismatched world UID.");
            }
        }

        // Accepts one invariant pair of sector coordinates.
        private static bool ValidZone(string key)
        {
            var parts = key?.Split(':');
            return parts?.Length == 2 &&
                int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) &&
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var z) &&
                key == x.ToString(CultureInfo.InvariantCulture) + ":" + z.ToString(CultureInfo.InvariantCulture);
        }
    }
}
