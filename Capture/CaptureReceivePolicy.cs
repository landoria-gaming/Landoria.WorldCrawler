using System;
using System.Collections.Generic;

namespace Landoria.WorldCrawler.Capture
{
    // Filters fauna before state comparison; all included object types use content fingerprints.
    internal sealed class CaptureReceivePolicy
    {
        private readonly Dictionary<int, int> _policies = new Dictionary<int, int>();

        // Caches prefab classification without requiring live scene instances to exist yet.
        internal int For(int hash)
        {
            if (_policies.TryGetValue(hash, out var policy))
            {
                return policy;
            }
            var prefab = ZNetScene.instance.GetPrefab(hash);
            if (prefab == null)
            {
                return 1;
            }
            policy = CaptureExclusionPolicy.Classify(prefab) != null ? 0 : 1;
            _policies.Add(hash, policy);
            return policy;
        }
    }
}
