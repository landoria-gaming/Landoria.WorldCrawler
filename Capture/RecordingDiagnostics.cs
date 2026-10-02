using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Landoria.WorldCrawler.Restoration.Persistence;
using UnityEngine;

namespace Landoria.WorldCrawler.Capture
{
    // Counts observation work without logging packets, chest contents or player text.
    internal sealed class RecordingDiagnostics
    {
        private long _network, _local, _changes;
        private long _reportedNetwork, _reportedLocal, _reportedProcessed, _reportedChanges, _reportedUnchanged;
        private float _lastNetwork = -1f, _lastChange = -1f;
        private string _lastObject = "none";
        private readonly Dictionary<string, int> _changedPrefabs = new Dictionary<string, int>();
        internal long Processed, Unchanged;

        // Separates actual deserialization callbacks from local sweep candidates.
        internal void Received(bool fromNetwork)
        {
            if (fromNetwork)
            {
                _network++;
                _lastNetwork = Time.realtimeSinceStartup;
            }
            else
            {
                _local++;
            }
        }

        // Records the last included content change and bounded per-prefab counts.
        internal void Changed(ZDO source, int x, int z)
        {
            _changes++;
            _lastChange = Time.realtimeSinceStartup;
            var prefab = ZNetScene.instance.GetPrefab(source.GetPrefab());
            var name = prefab == null ? source.GetPrefab().ToString(CultureInfo.InvariantCulture) : prefab.name;
            _lastObject = name + "#" + source.m_uid + "@" + x + ":" + z;
            _changedPrefabs.TryGetValue(name, out var count);
            _changedPrefabs[name] = count + 1;
        }

        // Reports interval counts; receiving a packet does not prove that export data changed.
        internal string Describe()
        {
            var top = string.Join(",", _changedPrefabs.OrderByDescending(pair => pair.Value).Take(5)
                .Select(pair => pair.Key + "=" + pair.Value));
            var result = "networkRecordsDelta=" + (_network - _reportedNetwork) +
                "; localSweepCandidatesDelta=" + (_local - _reportedLocal) +
                "; processedDelta=" + (Processed - _reportedProcessed) +
                "; acceptedChangesDelta=" + (_changes - _reportedChanges) +
                "; unchangedDelta=" + (Unchanged - _reportedUnchanged) +
                "; networkAge=" + Age(_lastNetwork) + "; acceptedChangeAge=" + Age(_lastChange) +
                "; lastChanged=" + _lastObject + "; changedPrefabs=[" + top + "]";
            _reportedNetwork = _network;
            _reportedLocal = _local;
            _reportedProcessed = Processed;
            _reportedChanges = _changes;
            _reportedUnchanged = Unchanged;
            _changedPrefabs.Clear();
            return result;
        }

        // Uses a stable English number format and distinguishes never-seen activity.
        internal static string Age(float timestamp)
        {
            return timestamp < 0f ? "never" : Seconds(Time.realtimeSinceStartup - timestamp);
        }

        // Formats elapsed time independently of the player's locale.
        internal static string Seconds(float value)
        {
            return value.ToString("F1", CultureInfo.InvariantCulture) + "s";
        }
    }
}
