using System;
using System.Collections.Generic;

namespace Landoria.WorldCrawler.Restoration
{
    // Finds persisted source tags after Valheim assigns fresh runtime IDs on world load.
    internal sealed class RestoreIdentityIndex
    {
        private readonly string _prefix;
        private readonly List<ZDOID> _pending;
        private readonly Dictionary<string, ZDOID> _identities = new Dictionary<string, ZDOID>(StringComparer.Ordinal);
        private int _index;

        // Takes only tagged object IDs; restoration is restricted to the current local game version.
        internal RestoreIdentityIndex(string fingerprint)
        {
            _prefix = fingerprint + ":";
            _pending = ZDOExtraData.GetAllZDOIDsWithHash(ZDOExtraData.Type.String, ObjectRestorer.IdentityTag.GetStableHashCode());
        }

        // Builds the read-only index over bounded frames before preflight validates journal entries.
        internal bool Step(int budget)
        {
            while (_index < _pending.Count && budget-- > 0)
            {
                var target = ZDOMan.instance.GetZDO(_pending[_index++]);
                if (target == null)
                {
                    continue;
                }
                var tag = target.GetString(ObjectRestorer.IdentityTag, "");
                if (!tag.StartsWith(_prefix, StringComparison.Ordinal))
                {
                    continue;
                }
                if (_identities.TryGetValue(tag, out var existing) && existing != target.m_uid)
                {
                    throw new InvalidOperationException("Duplicate restored source identity: " + tag);
                }
                _identities[tag] = target.m_uid;
            }
            return _index == _pending.Count;
        }

        // Rechecks the live tag so destroyed or recycled runtime IDs never match another object.
        internal ZDO Resolve(string tag)
        {
            if (!_identities.TryGetValue(tag, out var id))
            {
                return null;
            }
            var target = ZDOMan.instance.GetZDO(id);
            return target != null && target.GetString(ObjectRestorer.IdentityTag, "") == tag ? target : null;
        }
    }
}
