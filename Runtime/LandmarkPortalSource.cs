using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Landoria.WorldCrawler.Inventory;

namespace Landoria.WorldCrawler.Runtime
{
    // Reads the native client portal cache without mutating or generating world data.
    internal static class LandmarkPortalSource
    {
        // Copies cached portals and already-received linked endpoints, with explicit coverage limits.
        internal static void Read(LandmarkInventory result)
        {
            var pending = new Queue<ZDO>(Portals());
            var seen = new HashSet<ZDOID>();
            var missing = new HashSet<ZDOID>();
            while (pending.Count > 0)
            {
                var portal = pending.Dequeue();
                if (!ValidPortal(portal) || !seen.Add(portal.m_uid)) { continue; }
                Add(result, portal);
                var target = portal.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal);
                if (target == ZDOID.None) { continue; }
                var endpoint = ZDOMan.instance.GetZDO(target);
                if (ValidPortal(endpoint)) { pending.Enqueue(endpoint); }
                else { missing.Add(target); }
            }
            result.Warnings.Add("Portal coverage is limited to the current client's received data; "
                + "this is not a complete server portal list. Revisit missing portals and refresh the inventory.");
            if (missing.Count > 0)
            {
                result.Warnings.Add(missing.Count + " connected portal endpoint(s) have no received position "
                    + "and were not guessed or selected.");
            }
        }

        // Flattens the legacy list and current per-sector dictionary through an audited adapter.
        internal static List<ZDO> Portals()
        {
            var method = typeof(ZDOMan).GetMethod("GetPortals", BindingFlags.Instance | BindingFlags.Public,
                null, Type.EmptyTypes, null) ?? throw new MissingMethodException("ZDOMan.GetPortals");
            var value = method.Invoke(ZDOMan.instance, null);
            var result = new List<ZDO>();
            if (value is List<ZDO> list) { result.AddRange(list); }
            else if (value is IDictionary groups)
            {
                foreach (var group in groups.Values)
                {
                    if (!(group is List<ZDO> portals))
                    { throw new NotSupportedException("Unexpected portal sector collection."); }
                    result.AddRange(portals);
                }
            }
            else { throw new NotSupportedException("Unexpected native portal cache representation."); }
            return result;
        }

        // Accepts only live records whose prefab is recognized as a portal by this game version.
        internal static bool ValidPortal(ZDO portal)
        {
            return portal != null && portal.IsValid() && Game.instance.PortalPrefabHash.Contains(portal.GetPrefab());
        }

        // Keeps distinct portal instances separate even when they have the same tag or position.
        private static void Add(LandmarkInventory result, ZDO portal)
        {
            var id = "portal:" + portal.m_uid.UserID.ToString(CultureInfo.InvariantCulture)
                + ":" + portal.m_uid.ID.ToString(CultureInfo.InvariantCulture);
            LandmarkSnapshot.Add(result, id, "portal", portal.GetString("tag", ""),
                "received-portal-zdo", portal.GetPosition());
        }
    }
}
