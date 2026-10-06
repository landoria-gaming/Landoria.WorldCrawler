using System;
using System.Collections.Generic;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration.Objects
{
    // Finds source-world timestamps that would delay restored objects in the destination world.
    internal static class RestoredClockRepair
    {
        // Returns the number of faulty fields and optionally resets them on one tagged object.
        internal static int Inspect(ZDO zdo, GameObject prefab, long now, bool repair)
        {
            var fields = new List<int>();
            if (prefab.GetComponent<Smelter>() != null || prefab.GetComponent<CookingStation>() != null ||
                prefab.GetComponent<Fermenter>() != null || prefab.GetComponent<ShieldGenerator>() != null)
            {
                fields.Add(ZDOVars.s_startTime);
            }
            if (prefab.GetComponent<Plant>() != null || prefab.GetComponent<Vine>() != null)
            {
                fields.Add(ZDOVars.s_plantTime);
            }
            if (prefab.GetComponent<Vine>() != null) fields.Add(ZDOVars.s_growStart);
            if (prefab.GetComponent<Pickable>() != null) fields.Add(ZDOVars.s_pickedTime);
            if (prefab.GetComponent<WispSpawner>() != null) fields.Add(ZDOVars.s_lastSpawn);
            if (prefab.GetComponent<ShipConstructor>() != null || prefab.GetComponent<LootSpawner>() != null)
            {
                fields.Add(ZDOVars.s_spawnTime);
            }
            if (prefab.GetComponent<CreatureSpawner>() != null) fields.Add(ZDOVars.s_aliveTime);
            var found = 0;
            foreach (var key in fields)
            {
                if (zdo.GetLong(key, 0L) <= now) continue;
                found++;
                if (repair) zdo.Set(key, now);
            }
            return found + InspectProgress(zdo, prefab, repair);
        }

        // Clears negative processing progress already written by a restored machine.
        private static int InspectProgress(ZDO zdo, GameObject prefab, bool repair)
        {
            var found = 0;
            if (prefab.GetComponent<Smelter>() != null && zdo.GetFloat(ZDOVars.s_accTime) < 0f)
            {
                found++;
                if (repair) zdo.Set(ZDOVars.s_accTime, 0f);
            }
            var station = prefab.GetComponent<CookingStation>();
            if (station == null) return found;
            for (var slot = 0; slot < station.m_slots.Length; slot++)
            {
                var key = "slot" + slot;
                if (zdo.GetFloat(key) >= 0f) continue;
                found++;
                if (repair) zdo.Set(key, 0f);
            }
            return found;
        }
    }
}
