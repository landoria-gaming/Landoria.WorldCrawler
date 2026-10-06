using System;
using System.Collections.Generic;
using System.Linq;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Restoration.Objects;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration.Cleanup
{
    // Describes one checked generated decoration and its durable native record.
    internal sealed class IndestructibleTarget
    {
        private readonly ZNetView _view;
        private readonly ZDO _data;
        private readonly GameObject _root;
        private readonly int _locationHash;
        private readonly string _locationName;
        private readonly GameObject _attachedObject;
        private readonly bool _attached;
        internal string Name { get; }
        internal Vector3 Position { get; private set; }
        internal bool Removed { get; private set; }
        internal ZDOID Id => _data.m_uid;

        // Measures a listed decoration from the player's current position.
        internal float Distance(Vector3 position) => Vector3.Distance(Position, position);

        // Retains exact live references so list numbers cannot select a different object later.
        private IndestructibleTarget(ZNetView view, ZDO data, GameObject root, string name, int locationHash,
            GameObject attachedObject = null)
        {
            _view = view;
            _data = data;
            _root = root;
            Name = attachedObject == null ? name : Utils.GetPrefabName(attachedObject) + " (" + name + ")";
            Position = attachedObject == null ? data.GetPosition() : attachedObject.transform.position;
            _locationHash = locationHash;
            _locationName = name;
            _attachedObject = attachedObject;
            _attached = attachedObject != null;
        }

        // Lists only loaded, generated decorations with a durable identity and solid static geometry.
        internal static IEnumerable<IndestructibleTarget> ReadAll(ZNetView view, CaptureApi api, CleanupSourceIndex source,
            Vector3 center, float radius)
        {
            var data = view.GetZDO();
            if (!Generated(data))
            {
                yield break;
            }
            var proxy = view.GetComponent<LocationProxy>();
            var hash = proxy == null ? 0 : data.GetInt("location", 0);
            var root = proxy == null ? view.gameObject : api.GetLocationInstance(proxy);
            if (root == null || proxy != null && hash == 0)
            {
                yield break;
            }
            var name = proxy == null ? Utils.GetPrefabName(view.gameObject) : api.GetLocationName(hash);
            if (proxy != null && !AttachedSource(data, source, hash, name))
            {
                foreach (var candidate in AttachedDecorationPlacement.FindAll(root))
                {
                    if (Vector3.Distance(center, candidate.transform.position) <= radius)
                    {
                        yield return new IndestructibleTarget(view, data, root, name, hash, candidate);
                    }
                }
            }
            if (Vector3.Distance(center, data.GetPosition()) > radius)
            {
                yield break;
            }
            if (!Importable(data, source, hash, name) && Safe(root, view, name))
            {
                yield return new IndestructibleTarget(view, data, root, name, hash);
            }
        }

        // Explains why a visible Vegvisir was excluded from the numbered list.
        internal static string ExplainVegvisir(Vegvisir marker, CaptureApi api, CleanupSourceIndex source)
        {
            var view = marker.GetComponentInParent<ZNetView>();
            if (view == null)
            {
                return "no independent persistent network view (part of a generated site)";
            }
            var data = view.GetZDO();
            if (data == null || !data.IsValid())
            {
                return "network record is not loaded";
            }
            if (!Generated(data))
            {
                return "protected, imported or player-created record";
            }
            var proxy = view.GetComponent<LocationProxy>();
            var hash = proxy == null ? 0 : data.GetInt("location", 0);
            var root = proxy == null ? view.gameObject : api.GetLocationInstance(proxy);
            if (root == null)
            {
                return "generated site hierarchy is not loaded";
            }
            var name = proxy == null ? Utils.GetPrefabName(view.gameObject) : api.GetLocationName(hash);
            if (proxy != null && AttachedSource(data, source, hash, name))
            {
                return "this exact generated site is present in the source export and protected";
            }
            if (proxy == null && Importable(data, source, hash, name))
            {
                return "present in the source export and protected";
            }
            if (proxy != null)
            {
                return AttachedDecorationPlacement.FindAll(root).Contains(marker.gameObject) ?
                    "eligible attached decoration; check loaded proxy discovery" :
                    "attached Vegvisir failed its own safety check (" + marker.gameObject.name + ")";
            }
            if (Safe(root, view, name))
            {
                return "eligible view; check its center distance";
            }
            var blocked = root.GetComponentsInChildren<MonoBehaviour>(true)
                .Where(script => Functional(script) && !(script is Vegvisir))
                .Select(script => script == null ? "missing script" : script.GetType().Name)
                .Distinct().ToArray();
            return "prefab " + name + ", location " + hash + ", root " + root.name +
                ", blocked components: " + (blocked.Length == 0 ? "none" : string.Join(", ", blocked));
        }

        // Protects all globally exported types, even when this particular object has not been imported yet.
        private static bool Importable(ZDO data, CleanupSourceIndex source, int hash, string name)
        {
            var prefab = ZNetScene.instance.GetPrefab(data.GetPrefab());
            if (source == null || prefab == null)
            {
                return true;
            }
            if (hash == 0 && prefab.name == "Vegvisir_GDKing")
            {
                var position = data.GetPosition();
                return source.Present(data.GetPrefab(), prefab.name, position.x, position.y, position.z, 0);
            }
            return source.Known(data.GetPrefab(), prefab.name, hash, name);
        }

        // Protects the exact source location while allowing extra sites of the same prefab elsewhere.
        private static bool AttachedSource(ZDO data, CleanupSourceIndex source, int hash, string name)
        {
            var prefab = ZNetScene.instance.GetPrefab(data.GetPrefab());
            if (source == null || prefab == null || hash == 0 || string.IsNullOrEmpty(name))
            {
                return true;
            }
            var position = data.GetPosition();
            return source.Present(data.GetPrefab(), prefab.name, position.x, position.y, position.z, hash);
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
            var elderVegvisir = name == "Vegvisir_GDKing";
            if (elderVegvisir && root == view.gameObject && root.GetComponentInChildren<Vegvisir>(true) != null)
            {
                return !scripts.Any(script => Functional(script) && !(script is Vegvisir));
            }
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
        internal string Delete(CaptureApi api, CleanupSourceIndex source, Vector3 player, float radius)
        {
            Validate(source, player, radius);
            if (_attached)
            {
                throw new InvalidOperationException("This decoration belongs to a generated site. Use move, not delete.");
            }
            var liveHash = _data.GetInt("location", 0);
            if (_locationHash != 0 && liveHash != _locationHash)
            {
                throw new InvalidOperationException("The live location hash changed from " + _locationHash + " to " + liveHash +
                    ". Nothing was deleted; run indestructible list again.");
            }
            string rejection = null;
            if (_locationHash != 0 && !api.RemoveLocationRegistration(Position, _locationHash, out rejection))
            {
                throw new InvalidOperationException(rejection + " Nothing was deleted.");
            }
            DeleteHierarchy();
            Removed = true;
            return rejection;
        }

        // Rechecks a listed object before either world mutation.
        private void Validate(CleanupSourceIndex source, Vector3 player, float radius)
        {
            if (Removed || _view == null || _root == null || _attached && _attachedObject == null ||
                _view.GetZDO() != _data || !Generated(_data))
            {
                throw new InvalidOperationException("This entry changed or is no longer loaded. Run indestructible list again.");
            }
            var livePosition = _attached ? _attachedObject.transform.position : _data.GetPosition();
            var safe = _attached ? AttachedDecorationPlacement.FindAll(_root).Contains(_attachedObject) :
                Safe(_root, _view, Name);
            if (Distance(player) > radius || Vector3.Distance(Position, livePosition) > 0.1f || !safe)
            {
                throw new InvalidOperationException("This entry changed or is too far away. Run indestructible list again.");
            }
            if (_attached ? AttachedSource(_data, source, _locationHash, _locationName) :
                Importable(_data, source, _locationHash, _locationName))
            {
                throw new InvalidOperationException("This object is present in the export and cannot be changed.");
            }
        }

        // Relocates a standalone persistent decoration after finding a free, grounded destination.
        internal Vector3 Move(CleanupSourceIndex source, Vector3 player, float radius)
        {
            Validate(source, player, radius);
            if (_attached)
            {
                return MoveAttached();
            }
            if (_locationHash != 0 || _root != _view.gameObject)
            {
                throw new InvalidOperationException("This monument is a generated site. Moving its location registry is unsupported; use delete instead.");
            }
            var destination = IndestructibleMover.Find(_root, Position);
            _data.SetOwner(ZDOMan.GetSessionID());
            _data.Set(IndestructibleMover.Marker, true);
            _data.SetPosition(destination);
            _root.transform.position = destination;
            Position = destination;
            return destination;
        }

        // Saves a site child's new world position on its persistent proxy record.
        private Vector3 MoveAttached()
        {
            var destination = IndestructibleMover.Find(_attachedObject, Position);
            var key = AttachedKey();
            _data.SetOwner(ZDOMan.GetSessionID());
            _data.Set(IndestructibleMover.Marker, true);
            _data.Set(key, destination);
            _attachedObject.transform.position = destination;
            Position = destination;
            return destination;
        }

        // Places any listed decoration on terrain at its current horizontal position.
        internal Vector3 Ground(CleanupSourceIndex source, Vector3 player, float radius)
        {
            Validate(source, player, radius);
            var root = _attached ? _attachedObject : _root;
            if (!_attached && (_locationHash != 0 || _root != _view.gameObject))
            {
                throw new InvalidOperationException("Only standalone or safely attached decorations can be grounded.");
            }
            var destination = IndestructibleMover.Ground(root, Position);
            var key = _attached ? AttachedKey() : null;
            _data.SetOwner(ZDOMan.GetSessionID());
            _data.Set(IndestructibleMover.Marker, true);
            if (!_attached)
            {
                _data.SetPosition(destination);
            }
            else
            {
                _data.Set(key, destination);
            }
            root.transform.position = destination;
            Position = destination;
            return destination;
        }

        // Rechecks the durable child path before writing a location override.
        private string AttachedKey()
        {
            return AttachedDecorationPlacement.PositionKeyFor(_view.gameObject, _attachedObject)
                ?? throw new InvalidOperationException("The decoration is no longer inside the listed location.");
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
