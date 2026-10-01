using System;
using System.Collections.Generic;

namespace Landoria.WorldCrawler.Capture
{
    // Filters irrelevant fauna and distinguishes ticking objects from meaningful state updates.
    internal sealed class CaptureReceivePolicy
    {
        private readonly Dictionary<int, int> _policies = new Dictionary<int, int>();

        // Caches prefab classification without requiring live scene instances to exist yet.
        internal int For(int hash)
        {
            if (_policies.TryGetValue(hash, out var policy)) { return policy; }
            var prefab = ZNetScene.instance.GetPrefab(hash);
            if (prefab == null) { throw new InvalidOperationException("Received an unknown prefab: " + hash); }
            policy = CaptureExclusionPolicy.Classify(prefab) != null ? 0 : 1;
            if (policy != 0 && (prefab.GetComponent<Container>() != null || prefab.GetComponent<Sign>() != null ||
                prefab.GetComponent<ItemStand>() != null || prefab.GetComponent<ArmorStand>() != null ||
                prefab.GetComponent<TerrainComp>() != null || prefab.GetComponent<LocationProxy>() != null ||
                prefab.GetComponent<DungeonGenerator>() != null || prefab.GetComponent<MineRock>() != null ||
                prefab.GetComponent<MineRock5>() != null)) { policy = 2; }
            _policies.Add(hash, policy);
            return policy;
        }
    }
}
