using System;
using System.Collections.Generic;
using System.Globalization;

namespace Landoria.WorldCrawler.Restoration
{
    // Finds persisted source tags after Valheim assigns fresh runtime IDs on world load.
    internal sealed class RestoreIdentityIndex
    {
        private readonly List<ZDOID> _pending;
        private readonly Dictionary<string, ZDOID> _identities = new Dictionary<string, ZDOID>(StringComparer.Ordinal);
        private int _index;

        // Takes only tagged object IDs; restoration is restricted to the current local game version.
        internal RestoreIdentityIndex()
        {
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
                var tag = SourceKey(target.GetString(ObjectRestorer.IdentityTag, ""));
                if (tag == null)
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
            return target != null && SourceKey(target.GetString(ObjectRestorer.IdentityTag, "")) == tag ? target : null;
        }

        // Extracts original identity from both UID-prefixed and older fingerprint-prefixed tags.
        internal static string SourceKey(string tag)
        {
            var parts = tag?.Split(':');
            if (parts?.Length != 3 || string.IsNullOrEmpty(parts[0]) ||
                !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var user) ||
                !uint.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                return null;
            }
            return user.ToString(CultureInfo.InvariantCulture) + ":" + id.ToString(CultureInfo.InvariantCulture);
        }
    }
}
