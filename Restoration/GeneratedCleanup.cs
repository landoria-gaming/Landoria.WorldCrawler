using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Landoria.WorldCrawler.Capture;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration
{
    // Reconciles known generated scenery without inferring absence outside captured sectors.
    internal sealed partial class GeneratedCleanup
    {
        private readonly CleanupSourceIndex _source;
        private readonly Action<string> _warning;
        private readonly CaptureApi _api = new CaptureApi();
        private readonly HashSet<string> _reported = new HashSet<string>();
        private readonly HashSet<int> _vegetation;
        private readonly HashSet<ZDOID> _destroyed = new HashSet<ZDOID>();
        private static readonly MethodInfo RemoveView = typeof(ZNetScene).GetMethod("OnZDODestroyed",
            BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(ZDO) }, null);

        // Uses the native generation catalogue and validated export as two independent allowlists.
        internal GeneratedCleanup(CleanupSourceIndex source, Action<string> warning)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _warning = warning;
            _vegetation = new HashSet<int>(ZoneSystem.instance.m_vegetation.Where(v => v.m_prefab != null)
                .Select(v => v.m_prefab.name.GetStableHashCode()));
            if (RemoveView == null)
            {
                throw new MissingMethodException("ZNetScene.OnZDODestroyed(ZDO)");
            }
        }

        // Builds a complete checked deletion plan before mutating the current sector.
        internal int Run(int x, int z, List<ZDO> zone, HashSet<ZDOID> claimed)
        {
            if (!_source.Complete(x, z))
            {
                Warn("Cleanup skipped: zone " + x + ":" + z + " has no complete source capture.");
                return 0;
            }
            var plan = new HashSet<ZDO>();
            var registrations = new List<ZDO>();
            foreach (var target in zone.Where(v => v != null && v.IsValid()).ToList())
            {
                var prefab = ZNetScene.instance.GetPrefab(target.GetPrefab());
                if (prefab != null && prefab.GetComponent<LocationProxy>() != null && Eligible(target, claimed))
                {
                    PlanLocation(target, zone, claimed, plan, registrations);
                }
                else if (Scenery(prefab) && Eligible(target, claimed))
                {
                    plan.Add(target);
                }
            }
            foreach (var location in registrations)
            {
                _api.RemoveLocationRegistration(location.GetPosition(), location.GetInt("location", 0));
            }
            Report(plan, x, z);
            foreach (var target in plan)
            {
                _destroyed.Add(target.m_uid);
                Destroy(target);
            }
            return plan.Count;
        }

        // Waits for explicit native destruction to finish before a zone can be checkpointed.
        internal bool DeletionsPending()
        {
            _destroyed.RemoveWhere(id => ZDOMan.instance.GetZDO(id) == null);
            return _destroyed.Count != 0;
        }

        // Protects actors, player-owned data, imported objects and untagged source-position matches.
        private bool Eligible(ZDO target, HashSet<ZDOID> claimed)
        {
            if (target == null || !target.IsValid() || !target.Persistent || claimed.Contains(target.m_uid) ||
                target.GetLong("creator", 0L) != 0L || target.GetBool("tamed", false) ||
                !string.IsNullOrEmpty(target.GetString(ObjectRestorer.IdentityTag, "")))
            {
                return false;
            }
            var prefab = ZNetScene.instance.GetPrefab(target.GetPrefab());
            if (prefab == null || Protected(prefab) || ProtectedView(target))
            {
                return false;
            }
            var location = prefab.GetComponent<LocationProxy>() == null ? 0 : target.GetInt("location", 0);
            if (location == 0 && prefab.GetComponent<LocationProxy>() != null)
            {
                Warn("Cleanup retained a location with no readable identity.");
                return false;
            }
            var name = location == 0 ? null : LocationName(location);
            if (!_source.Known(target.GetPrefab(), prefab.name, location, name))
            {
                Warn("Cleanup retained type absent from the export: " + (name ?? prefab.name) + ".");
                return false;
            }
            var position = target.GetPosition();
            return !_source.Present(target.GetPrefab(), prefab.name, position.x, position.y, position.z, location);
        }

        // Plans a generated site only when its full footprint and static hierarchy are safe.
        private void PlanLocation(ZDO target, List<ZDO> zone, HashSet<ZDOID> claimed,
            HashSet<ZDO> plan, List<ZDO> registrations)
        {
            var proxy = ZNetScene.instance.FindInstance(target)?.GetComponent<LocationProxy>();
            var root = proxy == null ? null : _api.GetLocationInstance(proxy);
            var location = root == null ? null : root.GetComponent<Location>();
            var center = target.GetPosition();
            if (location == null || !_source.Covers(center.x, center.z, location.GetMaxRadius() + 2f) ||
                !SafeHierarchy(root))
            {
                Warn("Cleanup retained location at " + center + ": unloaded, protected, or outside complete captures.");
                return;
            }
            var radius = location.GetMaxRadius() + 2f;
            var members = new HashSet<int>(root.GetComponentsInChildren<ZNetView>(true)
                .Select(view => Utils.GetPrefabName(view.gameObject).GetStableHashCode()));
            foreach (var child in zone)
            {
                if (child != target && child != null && child.IsValid() && members.Contains(child.GetPrefab()) &&
                    Vector3.Distance(child.GetPosition(), center) <= radius && Eligible(child, claimed))
                {
                    plan.Add(child);
                }
            }
            plan.Add(target);
            registrations.Add(target);
        }

        // Refuses deleting a parent whose live children include independent or protected network objects.
        private bool SafeHierarchy(GameObject root)
        {
            return root.GetComponentsInChildren<Character>(true).Length == 0 &&
                root.GetComponentsInChildren<ZNetView>(true).All(view => view.GetZDO() == null) &&
                root.GetComponentsInChildren<Transform>(true).All(node =>
                    CaptureExclusionPolicy.Classify(node.gameObject) == null && _source.KnownScene(node.name,
                        node.GetComponents<Component>().Select(c => c == null ? "<missing>" : c.GetType().FullName)));
        }

        // Extends natural cleanup to fixed native vegetation, including indestructible rocks and scenery.
        private bool Scenery(GameObject prefab)
        {
            if (prefab == null || Protected(prefab) || prefab.GetComponent<LocationProxy>() != null ||
                prefab.GetComponent<Piece>() != null || prefab.GetComponent<Container>() != null)
            {
                return false;
            }
            return _vegetation.Contains(prefab.name.GetStableHashCode()) || prefab.GetComponent<MineRock>() != null ||
                prefab.GetComponent<MineRock5>() != null || prefab.GetComponent<TreeBase>() != null;
        }

        // Keeps actors, terrain edits, planted crops and loose inventory outside destructive cleanup.
        private static bool Protected(GameObject prefab)
        {
            var sync = prefab.GetComponent<ZSyncTransform>();
            return RestoreProtection.Protected(prefab) || prefab.GetComponent<ItemDrop>() != null ||
                prefab.GetComponent<TerrainComp>() != null || prefab.GetComponent<TerrainModifier>() != null ||
                prefab.GetComponent<Plant>() != null || sync != null && sync.m_syncPosition;
        }

        // Checks the actual instantiated object as well as its catalogue prefab.
        private static bool ProtectedView(ZDO target)
        {
            var view = ZNetScene.instance.FindInstance(target);
            return view != null && (RestoreProtection.Protected(view.gameObject) ||
                view.GetComponentInChildren<Player>(true) != null);
        }

        // Treats unknown location hashes as non-deletable instead of guessing their identity.
        private string LocationName(int hash)
        {
            try
            {
                return _api.GetLocationName(hash);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        // Unloads a checked live view and removes its durable record without producing loot.
        private static void Destroy(ZDO target)
        {
            if (ZNetScene.instance.FindInstance(target) != null)
            {
                RemoveView.Invoke(ZNetScene.instance, new object[] { target });
            }
            if (target.IsValid())
            {
                ZDOMan.instance.DestroyZDO(target);
            }
        }

        // Logs one short per-zone deletion summary for later inspection.
        private void Report(HashSet<ZDO> plan, int x, int z)
        {
            if (plan.Count > 0)
            {
                var counts = plan.GroupBy(v => ZNetScene.instance.GetPrefab(v.GetPrefab()).name)
                    .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Count() + " " + g.Key);
                _warning("Cleanup removing " + plan.Count + " known generated objects in " + x + ":" + z +
                    ": " + string.Join(", ", counts) + ".");
            }
        }

        // Avoids flooding the log with repeated reasons for preserving the same object type.
        private void Warn(string message)
        {
            if (_reported.Add(message))
            {
                _warning(message);
            }
        }
    }
}
