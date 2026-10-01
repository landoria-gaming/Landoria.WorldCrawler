using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Landoria.WorldCrawler.Capture
{
    // Copies persistent ZDO state using the successful Tomrer network-serialization sequence.
    internal sealed class ObjectCapture
    {
        private readonly CaptureApi _api;
        private readonly Dictionary<int, string[]> _categories = new Dictionary<int, string[]>();

        // Shares the session's validated game adapter and prefab catalogue.
        internal ObjectCapture(CaptureApi api)
        {
            _api = api;
        }

        // Takes one object observation without changing its ownership, properties, or revision.
        internal CapturedObject Read(ZDO source, int x, int z)
        {
            var prefab = ZNetScene.instance.GetPrefab(source.GetPrefab());
            if (prefab == null)
            {
                throw new InvalidOperationException("Unknown prefab " + source.GetPrefab() + " for ZDO " + source.m_uid);
            }
            var data = new ZPackage();
            source.Serialize(data);
            var instance = ZNetScene.instance.FindInstance(source);
            var result = new CapturedObject
            {
                SourceUser = source.m_uid.UserID.ToString(CultureInfo.InvariantCulture), SourceId = source.m_uid.ID,
                PrefabName = prefab.name, PrefabHash = source.GetPrefab(), ZoneX = x, ZoneZ = z,
                Position = CaptureTransform.Vector(source.GetPosition()), Rotation = CaptureTransform.Rotation(source.GetRotation()),
                LocalScale = instance == null ? null : CaptureTransform.Vector(instance.transform.localScale),
                DataRevision = source.DataRevision, ObjectType = (int)source.Type, Distant = source.Distant,
                SourceOwner = source.GetOwner().ToString(CultureInfo.InvariantCulture),
                Creator = source.GetLong("creator", 0L).ToString(CultureInfo.InvariantCulture),
                RawDataBase64 = data.GetBase64(), ObservedUtcTicks = DateTime.UtcNow.Ticks,
                Categories = Categories(prefab, source.GetPrefab())
            };
            ReadConnection(source, result);
            ReadLocation(source, prefab, result);
            return result;
        }

        // Keeps connection targets explicit as well as inside the raw source bytes.
        private static void ReadConnection(ZDO source, CapturedObject result)
        {
            var connection = source.GetConnection();
            if (connection == null)
            {
                return;
            }
            result.ConnectionType = (int)connection.m_type;
            result.ConnectionTargetUser = connection.m_target.UserID.ToString(CultureInfo.InvariantCulture);
            result.ConnectionTargetId = connection.m_target.ID;
        }

        // Captures the identity and generation seed of source location proxies.
        private void ReadLocation(ZDO source, GameObject prefab, CapturedObject result)
        {
            if (prefab.GetComponent<LocationProxy>() == null)
            {
                return;
            }
            result.LocationHash = source.GetInt("location", 0);
            result.LocationSeed = source.GetInt("seed", 0);
            result.LocationName = _api.GetLocationName(result.LocationHash);
        }

        // Labels useful data categories without reconstructing or dropping their raw fields.
        private string[] Categories(GameObject prefab, int hash)
        {
            string[] cached;
            if (_categories.TryGetValue(hash, out cached))
            {
                return cached;
            }
            var categories = new List<string>();
            foreach (var component in prefab.GetComponents<Component>())
            {
                if (component != null && IsRelevantCategory(component.GetType().Name))
                {
                    categories.Add(component.GetType().Name);
                }
            }
            if (prefab.name.StartsWith("BossStone_", StringComparison.Ordinal))
            {
                categories.Add("BossStone");
            }
            categories.Sort(StringComparer.Ordinal);
            cached = categories.ToArray();
            _categories[hash] = cached;
            return cached;
        }

        // Identifies components useful for auditing chests, terrain, displays, and generated objects.
        private static bool IsRelevantCategory(string name)
        {
            switch (name)
            {
                case "Piece": case "Container": case "Sign": case "TeleportWorld":
                case "ItemStand": case "ArmorStand": case "TerrainComp": case "TerrainModifier":
                case "Plant": case "Pickable": case "Beehive": case "Character": case "Tameable":
                case "TreeBase": case "TreeLog": case "Destructible": case "MineRock": case "MineRock5":
                case "LocationProxy": case "DungeonGenerator": case "ItemDrop":
                    return true;
                default:
                    return false;
            }
        }
    }
}
