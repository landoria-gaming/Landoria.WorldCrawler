using System;
using System.Collections.Generic;
using System.Linq;
using Landoria.WorldCrawler.Capture;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration
{
    // Removes extra generated merchant sites only after the matching exported site exists.
    internal sealed partial class GeneratedCleanup
    {
        // Uses the live Trader component to identify a merchant without guessing prefab names.
        internal int RunMerchant(CapturedObject source, GameObject root)
        {
            if (source.LocationHash == 0 || string.IsNullOrEmpty(source.LocationName) || root == null ||
                !_source.Present(source.PrefabHash, source.PrefabName, source.Position[0],
                    source.Position[1], source.Position[2], source.LocationHash) ||
                root.GetComponentsInChildren<Trader>(true).Length == 0)
            {
                return 0;
            }
            var sites = ZoneSystem.instance.GetLocationList().Where(site =>
                site.m_location != null && site.m_location.m_prefabName == source.LocationName &&
                !_source.Present(source.PrefabHash, source.PrefabName, site.m_position.x,
                    site.m_position.y, site.m_position.z, source.LocationHash)).ToArray();
            var removed = 0;
            foreach (var site in sites)
            {
                removed += RemoveMerchantSite(source, site);
            }
            if (removed != 0)
            {
                _warning("Removed " + removed + " extra generated " + source.LocationName +
                    " merchant sites outside exported source positions.");
            }
            return removed;
        }

        // Checks the remote site's proxy and nearby merchant ZDOs before deleting anything.
        private int RemoveMerchantSite(CapturedObject source, ZoneSystem.LocationInstance site)
        {
            var objects = new List<ZDO>();
            _api.FindObjects(site.m_position, objects);
            var proxies = objects.Where(item => MerchantProxy(item, source, site.m_position)).ToArray();
            if (proxies.Length != 1 || !SafeMerchantProxy(proxies[0]))
            {
                Warn("Merchant cleanup retained an ambiguous or protected site at " + site.m_position + ".");
                return 0;
            }
            var traders = FindTraders(site.m_position, site.m_location.m_exteriorRadius);
            if (traders == null || !_api.RemoveLocationRegistration(site.m_position, source.LocationHash))
            {
                Warn("Merchant cleanup retained a site whose generated records could not be checked at " +
                    site.m_position + ".");
                return 0;
            }
            foreach (var target in proxies.Concat(traders).Distinct())
            {
                _destroyed.Add(target.m_uid);
                Destroy(target);
            }
            return 1;
        }

        // Requires the source's exact location identity and generated position.
        private static bool MerchantProxy(ZDO target, CapturedObject source, Vector3 center)
        {
            if (target == null || !target.IsValid() || target.GetInt("location", 0) != source.LocationHash ||
                Vector3.Distance(target.GetPosition(), center) > 1f)
            {
                return false;
            }
            var prefab = ZNetScene.instance.GetPrefab(target.GetPrefab());
            return prefab != null && prefab.GetComponent<LocationProxy>() != null;
        }

        // Protects player-created and already imported source objects.
        private static bool SafeMerchantProxy(ZDO target)
        {
            return target.Persistent && target.GetLong("creator", 0L) == 0L &&
                string.IsNullOrEmpty(target.GetString(ObjectRestorer.IdentityTag, ""));
        }

        // Reads only sectors inside the site's bounded footprint; unknown trader types stop cleanup.
        private List<ZDO> FindTraders(Vector3 center, float exteriorRadius)
        {
            var radius = Mathf.Clamp(exteriorRadius + 4f, 24f, 128f);
            _api.GetZone(center - new Vector3(radius, 0f, radius), out var minX, out var minZ);
            _api.GetZone(center + new Vector3(radius, 0f, radius), out var maxX, out var maxZ);
            var traders = new Dictionary<ZDOID, ZDO>();
            var objects = new List<ZDO>();
            for (var z = minZ; z <= maxZ; z++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    _api.FindObjects(new Vector3(x * 64f, 0f, z * 64f), objects);
                    foreach (var target in objects)
                    {
                        if (!CheckTrader(target, center, radius, traders))
                        {
                            return null;
                        }
                    }
                }
            }
            return traders.Values.ToList();
        }

        // Requires a captured Trader prefab and leaves any unexpected protected object intact.
        private bool CheckTrader(ZDO target, Vector3 center, float radius, Dictionary<ZDOID, ZDO> traders)
        {
            if (target == null || !target.IsValid() || !target.Persistent ||
                Vector3.Distance(target.GetPosition(), center) > radius)
            {
                return true;
            }
            var prefab = ZNetScene.instance.GetPrefab(target.GetPrefab());
            if (prefab == null || prefab.GetComponent<Trader>() == null)
            {
                return true;
            }
            if (target.GetLong("creator", 0L) != 0L ||
                !string.IsNullOrEmpty(target.GetString(ObjectRestorer.IdentityTag, "")) ||
                !_source.Known(target.GetPrefab(), prefab.name, 0, null))
            {
                return false;
            }
            traders[target.m_uid] = target;
            return true;
        }
    }
}
