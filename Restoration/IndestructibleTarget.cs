using System;
using System.Linq;
using Landoria.WorldCrawler.Capture;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration
{
    // Describes one checked generated decoration and its durable native record.
    internal sealed class IndestructibleTarget
    {
        private readonly ZNetView _view;
        private readonly ZDO _data;
        private readonly GameObject _root;
        private readonly int _locationHash;
        internal string Name { get; }
        internal Vector3 Position { get; }
        internal bool Removed { get; private set; }
        internal ZDOID Id => _data.m_uid;

        // Measures a listed decoration from the player's current position.
        internal float Distance(Vector3 position) => Vector3.Distance(Position, position);

        // Retains exact live references so list numbers cannot select a different object later.
        private IndestructibleTarget(ZNetView view, ZDO data, GameObject root, string name, int locationHash)
        {
            _view = view;
            _data = data;
            _root = root;
            Name = name;
            Position = data.GetPosition();
            _locationHash = locationHash;
        }

        // Lists only loaded, generated decorations with a durable identity and solid static geometry.
        internal static IndestructibleTarget Read(ZNetView view, CaptureApi api, CleanupSourceIndex source,
            Vector3 center, float radius)
        {
            var data = view.GetZDO();
            if (!Generated(data) || Vector3.Distance(center, data.GetPosition()) > radius)
            {
                return null;
            }
            var proxy = view.GetComponent<LocationProxy>();
            var hash = proxy == null ? 0 : data.GetInt("location", 0);
            var root = proxy == null ? view.gameObject : api.GetLocationInstance(proxy);
            if (root == null || proxy != null && hash == 0)
            {
                return null;
            }
            var name = proxy == null ? Utils.GetPrefabName(view.gameObject) : api.GetLocationName(hash);
            return !Importable(data, source, hash, name) && Safe(root, view, name) ?
                new IndestructibleTarget(view, data, root, name, hash) : null;
        }

        // Protects all globally exported types, even when this particular object has not been imported yet.
        private static bool Importable(ZDO data, CleanupSourceIndex source, int hash, string name)
        {
            var prefab = ZNetScene.instance.GetPrefab(data.GetPrefab());
            return source == null || prefab == null || source.Known(data.GetPrefab(), prefab.name, hash, name);
        }

        // Never offers player-created objects, imported source objects, or invalid records.
        private static bool Generated(ZDO data)
        {
            return data != null && data.IsValid() && data.Persistent && data.GetLong("creator", 0L) == 0 &&
                !data.GetBool("tamed", false) && string.IsNullOrEmpty(data.GetString(ObjectRestorer.IdentityTag, ""));
        }

        // Allows known stone monuments while rejecting actors, functional structures and independent child records.
        private static bool Safe(GameObject root, ZNetView view, string name)
        {
            if (root == null || !root.activeInHierarchy ||
                root.GetComponentsInChildren<Transform>(true).Any(node => CaptureExclusionPolicy.Classify(node.gameObject) != null))
            {
                return false;
            }
            if (StoneSite(name))
            {
                return root.GetComponentsInChildren<Collider>().Any(collider => collider.enabled && !collider.isTrigger);
            }
            if (root.GetComponentsInChildren<ZNetView>(true).Any(child => child != view && child.GetZDO() != null))
            {
                return false;
            }
            var scripts = root.GetComponentsInChildren<MonoBehaviour>(true);
            if (scripts.Any(Functional))
            {
                return false;
            }
            return scripts.All(script => Decorative(script) || script is IDestructible) &&
                root.GetComponentsInChildren<Collider>().Any(collider => collider.enabled && !collider.isTrigger);
        }

        // Excludes gameplay structures even when their root also looks like static scenery.
        private static bool Functional(MonoBehaviour script)
        {
            return script == null || script is Interactable || script is Piece || script is Trader || script is DungeonGenerator ||
                script is TerrainComp || script is TerrainModifier || script is CreatureSpawner ||
                script is TeleportWorld || script is Container || script is ItemStand || script is ArmorStand;
        }

        // Recognizes the game's stone monuments without treating arbitrary locations as scenery.
        private static bool StoneSite(string name)
        {
            return name == "StoneCircle" || new[] { "StoneHenge", "Dolmen", "ShipSetting" }.Any(prefix =>
                name.StartsWith(prefix, StringComparison.Ordinal) && name.Length > prefix.Length &&
                name.Substring(prefix.Length).All(char.IsDigit));
        }

        // Allows other purely decorative sites while refusing unknown functional scripts.
        private static bool Decorative(MonoBehaviour component)
        {
            switch (component.GetType().Name)
            {
                case "Location":
                case "ZNetView":
                case "RandomSpawn":
                case "RandomRotation":
                case "RandomScale":
                case "SnapToGround":
                case "StaticPhysics":
                case "LodFadeIn":
                    return true;
                default:
                    return false;
            }
        }

        // Removes only the previously listed object after rechecking its identity and live hierarchy.
        internal void Delete(CaptureApi api, CleanupSourceIndex source, Vector3 player, float radius)
        {
            if (Removed || _view == null || _root == null || _view.GetZDO() != _data || !Generated(_data) ||
                Distance(player) > radius || Vector3.Distance(Position, _data.GetPosition()) > 0.1f ||
                !Safe(_root, _view, Name))
            {
                throw new InvalidOperationException("This entry is gone, too far away, or no longer safe. Run indestructible list again.");
            }
            if (Importable(_data, source, _locationHash, Name))
            {
                throw new InvalidOperationException("This type is present in the export and cannot be deleted with this command.");
            }
            var liveHash = _data.GetInt("location", 0);
            if (_locationHash != 0 && liveHash != _locationHash)
            {
                throw new InvalidOperationException("The live location hash changed from " + _locationHash + " to " + liveHash +
                    ". Nothing was deleted; run indestructible list again.");
            }
            string rejection;
            if (_locationHash != 0 && !api.RemoveLocationRegistration(Position, _locationHash, out rejection))
            {
                throw new InvalidOperationException(rejection + " Nothing was deleted.");
            }
            DeleteHierarchy();
            Removed = true;
        }

        // Deletes every durable member of a known generated site before removing its proxy.
        private void DeleteHierarchy()
        {
            var children = _root.GetComponentsInChildren<ZNetView>(true)
                .Where(child => child != null && child != _view && child.GetZDO() != null).ToArray();
            foreach (var child in children)
            {
                child.GetZDO().SetOwner(ZDOMan.GetSessionID());
                ZNetScene.instance.Destroy(child.gameObject);
            }
            _data.SetOwner(ZDOMan.GetSessionID());
            ZNetScene.instance.Destroy(_view.gameObject);
        }
    }
}
