using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Landoria.WorldCrawler.Capture;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration
{
    // Restores persistent non-creature data using durable source identity tags and exact matching.
    internal sealed class ObjectRestorer
    {
        internal const string IdentityTag = "WorldCrawler.source";
        private readonly string _fingerprint;
        private readonly RestoreState _state;
        private readonly Dictionary<string, RestoredObject> _map;
        private readonly List<ZDO> _zone = new List<ZDO>();
        private readonly HashSet<ZDOID> _claimed = new HashSet<ZDOID>();
        private readonly CaptureApi _api = new CaptureApi();
        private static readonly MethodInfo RemoveView = typeof(ZNetScene).GetMethod("OnZDODestroyed",
            BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(ZDO) }, null);

        // Shares a durable mapping with the journal while keeping lookups efficient.
        public ObjectRestorer(string fingerprint, RestoreState state)
        {
            _fingerprint = fingerprint; _state = state;
            _map = state.Objects.ToDictionary(v => v.Source, StringComparer.Ordinal);
            if (RemoveView == null) { throw new MissingMethodException("ZNetScene.OnZDODestroyed(ZDO)"); }
        }

        // Preflights prefab availability and independent actor protection before mutations begin.
        public static void Validate(CapturedObject source)
        {
            var prefab = ZNetScene.instance.GetPrefab(source.PrefabHash);
            if (prefab == null || prefab.name != source.PrefabName)
            { throw new InvalidOperationException("Missing or renamed source prefab: " + source.PrefabName); }
            if (RestoreProtection.Protected(prefab))
            { throw new InvalidOperationException("Protected player or creature record was rejected: " + source.PrefabName); }
            var norm = source.Rotation.Sum(v => v * v);
            if (Math.Abs(norm - 1f) > 0.05f) { throw new InvalidOperationException("Invalid source object rotation."); }
            if (!CaptureTransform.InZone(Vector(source.Position), source.ZoneX, source.ZoneZ))
            { throw new InvalidOperationException("An object's position lies outside its captured sector."); }
        }

        // Takes target candidates only from the loaded destination sector.
        public void BeginZone(int x, int z)
        { _api.FindObjects(new Vector3(x * 64f, 0f, z * 64f), _zone); _claimed.Clear(); }

        // Deletes only unclaimed, world-generated natural resources from a fully observed source sector.
        public int RemoveAbsentNaturalResources(bool completeObservation)
        {
            if (!completeObservation) { return 0; }
            var removed = 0;
            foreach (var target in _zone.ToList())
            {
                if (target == null || !target.IsValid() || _claimed.Contains(target.m_uid)
                    || target.GetLong("creator", 0L) != 0L
                    || !string.IsNullOrEmpty(target.GetString(IdentityTag, ""))) { continue; }
                var prefab = ZNetScene.instance.GetPrefab(target.GetPrefab());
                if (!NaturalResource(prefab) || RestoreProtection.Protected(prefab)) { continue; }
                var instance = ZNetScene.instance.FindInstance(target);
                if (instance != null && RestoreProtection.Protected(instance.gameObject)) { continue; }
                ZDOMan.instance.DestroyZDO(target);
                removed++;
            }
            return removed;
        }

        // Restricts destructive reconciliation to mined/picked natural nodes with stable native components.
        private static bool NaturalResource(GameObject prefab)
        {
            return prefab != null && (prefab.GetComponent<MineRock>() != null
                || prefab.GetComponent<MineRock5>() != null || prefab.GetComponent<TreeBase>() != null);
        }

        // Restores once by ID, falling back to tagged or unambiguous exact generated matches.
        public ZDO Restore(CapturedObject source, string version)
        {
            Validate(source);
            var target = Resolve(source) ?? Match(source);
            if (target == null)
            {
                target = ZDOMan.instance.CreateNewZDO(Vector(source.Position), source.PrefabHash);
                _zone.Add(target);
            }
            EnsureSafe(target, source.PrefabHash);
            _claimed.Add(target.m_uid);
            var previous = new ZPackage(); target.Serialize(previous);
            var position = target.GetPosition(); var rotation = target.GetRotation(); var owner = target.GetOwner();
            try
            {
                Refresh(target);
                ZDOExtraData.Release(target, target.m_uid);
                target.Deserialize(new ZPackage(source.RawDataBase64));
                target.SetOwner(ZDOMan.GetSessionID());
                LegacyMigration.Apply(target, source, version);
                target.SetConnection(ZDOExtraData.ConnectionType.None, ZDOID.None);
                target.SetPosition(Vector(source.Position)); target.SetRotation(Rotation(source.Rotation));
                if (source.LocalScale != null) { target.Set("scale", Vector(source.LocalScale)); }
                if (source.LocalScale != null) { target.Set("WorldCrawler.scale", Vector(source.LocalScale)); }
                target.Set(IdentityTag, Tag(source));
                target.Set("WorldCrawler.pending", true);
                Record(source, target);
                return target;
            }
            catch
            {
                ZDOExtraData.Release(target, target.m_uid); target.Deserialize(new ZPackage(previous.GetArray()));
                target.SetPosition(position); target.SetRotation(rotation); target.SetOwner(owner); throw;
            }
        }

        // Resolves only mappings whose saved destination still has the correct durable tag.
        public ZDO Resolve(CapturedObject source)
        {
            if (!_map.TryGetValue(ExportArchive.Key(source), out var mapped)) { return null; }
            var target = ZDOMan.instance.GetZDO(new ZDOID(long.Parse(mapped.TargetUser, CultureInfo.InvariantCulture), mapped.TargetId));
            return target != null && target.GetPrefab() == source.PrefabHash && target.GetString(IdentityTag, "") == Tag(source)
                ? target : null;
        }

        // Restores connections only to other proven imported objects, never to source IDs by accident.
        public bool Connect(CapturedObject source)
        {
            var target = Resolve(source);
            if (target == null || source.ConnectionType == 0) { return true; }
            var key = source.ConnectionTargetUser + ":" + source.ConnectionTargetId;
            if (!_map.TryGetValue(key, out var mapped)) { return false; }
            var endpoint = ZDOMan.instance.GetZDO(new ZDOID(long.Parse(mapped.TargetUser, CultureInfo.InvariantCulture), mapped.TargetId));
            if (endpoint == null || endpoint.GetString(IdentityTag, "") != _fingerprint + ":" + key) { return false; }
            EnsureSafe(target, source.PrefabHash);
            EnsureSafe(endpoint, mapped.Prefab);
            target.SetConnection((ZDOExtraData.ConnectionType)source.ConnectionType, endpoint.m_uid);
            return true;
        }

        // Refuses ambiguous matches rather than updating an arbitrary nearby structure.
        private ZDO Match(CapturedObject source)
        {
            var tagged = _zone.Where(z => z.GetString(IdentityTag, "") == Tag(source)).ToList();
            if (tagged.Count > 1) { throw new InvalidOperationException("Duplicate restored source identity in target zone."); }
            if (tagged.Count == 1) { return tagged[0]; }
            var position = Vector(source.Position); var rotation = Rotation(source.Rotation);
            var candidates = _zone.Where(z => !_claimed.Contains(z.m_uid) && z.GetPrefab() == source.PrefabHash &&
                string.IsNullOrEmpty(z.GetString(IdentityTag, "")) &&
                Vector3.Distance(z.GetPosition(), position) < 0.02f && Quaternion.Angle(z.GetRotation(), rotation) < 0.2f).ToList();
            if (candidates.Count > 1) { throw new InvalidOperationException("Ambiguous target match for " + source.PrefabName); }
            return candidates.FirstOrDefault();
        }

        // Rechecks the real target before every write or view refresh.
        private static void EnsureSafe(ZDO target, int prefab)
        {
            var instance = ZNetScene.instance.FindInstance(target);
            if (target.GetPrefab() != prefab || RestoreProtection.Protected(ZNetScene.instance.GetPrefab(prefab)) ||
                instance != null && RestoreProtection.Protected(instance.gameObject))
            { throw new InvalidOperationException("Protected or changed target object; restoration stopped."); }
        }

        // Removes only the local view using the game's unload path; the persistent ZDO remains.
        public static void Refresh(ZDO target)
        {
            EnsureSafe(target, target.GetPrefab());
            RemoveView.Invoke(ZNetScene.instance, new object[] { target });
        }

        // Updates the journal mapping after a successful object mutation.
        private void Record(CapturedObject source, ZDO target)
        {
            var key = ExportArchive.Key(source);
            if (!_map.TryGetValue(key, out var record))
            { record = new RestoredObject { Source = key }; _map.Add(key, record); _state.Objects.Add(record); }
            record.TargetUser = target.m_uid.UserID.ToString(CultureInfo.InvariantCulture);
            record.TargetId = target.m_uid.ID; record.Prefab = source.PrefabHash;
        }

        // Namespaces source IDs by the immutable export fingerprint.
        private string Tag(CapturedObject source) { return _fingerprint + ":" + ExportArchive.Key(source); }

        // Converts validated capture coordinates to Unity values.
        public static Vector3 Vector(float[] value) { return new Vector3(value[0], value[1], value[2]); }

        // Converts validated capture orientation to Unity values.
        public static Quaternion Rotation(float[] value) { return new Quaternion(value[0], value[1], value[2], value[3]); }
    }
}
