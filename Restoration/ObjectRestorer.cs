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
    internal sealed partial class ObjectRestorer
    {
        internal const string IdentityTag = "WorldCrawler.source";
        private readonly string _fingerprint;
        private readonly RestoreState _state;
        private readonly Dictionary<string, RestoredObject> _map;
        private readonly RestoreIdentityIndex _identities;
        private readonly Action<string> _warning;
        private readonly List<ZDO> _zone = new List<ZDO>();
        private readonly HashSet<ZDOID> _claimed = new HashSet<ZDOID>();
        private readonly HashSet<string> _applied = new HashSet<string>();
        private readonly CaptureApi _api = new CaptureApi();
        private readonly GeneratedCleanup _cleanup;
        private static readonly MethodInfo RemoveView = typeof(ZNetScene).GetMethod("OnZDODestroyed",
            BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(ZDO) }, null);

        // Shares a durable mapping with the journal while keeping lookups efficient.
        public ObjectRestorer(string fingerprint, RestoreState state, CleanupSourceIndex source, Action<string> warning)
        {
            _fingerprint = fingerprint;
            _state = state;
            _map = state.Objects.ToDictionary(v => v.Source, StringComparer.Ordinal);
            _identities = new RestoreIdentityIndex();
            _warning = warning;
            _cleanup = new GeneratedCleanup(source, warning);
            if (RemoveView == null)
            {
                throw new MissingMethodException("ZNetScene.OnZDODestroyed(ZDO)");
            }
        }

        // Finishes the durable tag index before checking progress saved in a previous session.
        public bool IndexIdentities()
        {
            return _identities.Step(256);
        }

        // Preflights prefab availability and independent actor protection before mutations begin.
        public static void Validate(CapturedObject source)
        {
            var prefab = ZNetScene.instance.GetPrefab(source.PrefabHash);
            if (prefab == null || prefab.name != source.PrefabName)
            {
                throw new InvalidOperationException("Missing or renamed source prefab: " + source.PrefabName);
            }
            if (RestoreProtection.Protected(prefab))
            {
                throw new InvalidOperationException("Protected player or creature record was rejected: " + source.PrefabName);
            }
            var norm = source.Rotation.Sum(v => v * v);
            if (Math.Abs(norm - 1f) > 0.05f)
            {
                throw new InvalidOperationException("Invalid source object rotation.");
            }
            if (!CaptureTransform.InZone(Vector(source.Position), source.ZoneX, source.ZoneZ))
            {
                throw new InvalidOperationException("An object's position lies outside its captured sector.");
            }
        }

        // Takes target candidates only from the loaded destination sector.
        public void BeginZone(int x, int z)
        {
            _api.FindObjects(new Vector3(x * 64f, 0f, z * 64f), _zone);
            var seen = new HashSet<ZDOID>();
            _zone.RemoveAll(target => target == null || !seen.Add(target.m_uid));
            _claimed.Clear();
            _applied.Clear();
        }

        // Counts only objects first applied during this zone visit.
        internal bool WasApplied(CapturedObject source)
        {
            return _applied.Contains(ExportArchive.Key(source));
        }

        // Waits until the engine has committed queued source-authoritative removals.
        internal bool DeletionsPending()
        {
            return _cleanup.DeletionsPending();
        }

        // Uses one source-aware cleanup policy for both restoration and manual repair.
        public int CleanupZone(int x, int z)
        {
            _api.FindObjects(new Vector3(x * 64f, 0f, z * 64f), _zone);
            return _cleanup.Run(x, z, _zone, _claimed);
        }

        // Reconciles same-type merchant sites after their exported source site has loaded.
        public int CleanupMerchantDuplicates(CapturedObject source, GameObject locationRoot)
        {
            return _cleanup.RunMerchant(source, locationRoot);
        }

        // Restores once by ID, falling back to tagged or unambiguous exact generated matches.
        public ZDO Restore(CapturedObject source, string version)
        {
            Validate(source);
            var target = Resolve(source);
            if (target != null)
            {
                _claimed.Add(target.m_uid);
                return target;
            }
            target = Match(source);
            var created = target == null;
            target = target ?? CreateTarget(source);
            try
            {
                EnsureSafe(target, source.PrefabHash);
                ApplyWithRollback(target, source, version);
                _applied.Add(ExportArchive.Key(source));
                _claimed.Add(target.m_uid);
                return target;
            }
            catch (Exception error)
            {
                if (created)
                {
                    _zone.Remove(target);
                    target.SetOwner(ZDOMan.GetSessionID());
                    target.Persistent = false;
                    ZDOMan.instance.DestroyZDO(target);
                }
                throw new InvalidOperationException("Could not restore " + source.PrefabName +
                    "; source=" + ExportArchive.Key(source) + "; target=" + target.m_uid +
                    "; newlyCreated=" + created + ". " + error.Message, error);
            }
        }

        // Initializes the prefab explicitly, just as ZNetView does after allocating a native ZDO.
        private ZDO CreateTarget(CapturedObject source)
        {
            var target = ZDOMan.instance.CreateNewZDO(Vector(source.Position), source.PrefabHash);
            target.SetPrefab(source.PrefabHash);
            _zone.Add(target);
            return target;
        }

        // Rolls back native state when applying or converting the captured data fails.
        private void ApplyWithRollback(ZDO target, CapturedObject source, string version)
        {
            var previous = new ZPackage();
            target.Serialize(previous);
            var position = target.GetPosition();
            var rotation = target.GetRotation();
            var owner = target.GetOwner();
            try
            {
                Apply(target, source, version);
            }
            catch
            {
                ZDOExtraData.Release(target, target.m_uid);
                target.Deserialize(new ZPackage(previous.GetArray()));
                target.SetPosition(position);
                target.SetRotation(rotation);
                target.SetOwner(owner);
                throw;
            }
        }

        // Applies captured state inside the caller's rollback boundary.
        private void Apply(ZDO target, CapturedObject source, string version)
        {
            Refresh(target);
            ZDOExtraData.Release(target, target.m_uid);
            var data = new ZPackage(source.RawDataBase64);
            target.Deserialize(data);
            if (target.GetPrefab() != source.PrefabHash || !target.Persistent || data.GetPos() != data.Size())
            {
                throw new InvalidOperationException("The captured ZDO was not fully decoded with its expected persistent prefab.");
            }
            target.SetOwner(ZDOMan.GetSessionID());
            LegacyMigration.Apply(target, source, version);
            target.SetPosition(Vector(source.Position));
            target.SetRotation(Rotation(source.Rotation));
            if (source.LocalScale != null)
            {
                target.Set("scale", Vector(source.LocalScale));
                target.Set("WorldCrawler.scale", Vector(source.LocalScale));
            }
            target.Set(IdentityTag, Tag(source));
            target.Set("WorldCrawler.pending", true);
            target.Set("WorldCrawler.initialized", false);
            RememberConnection(target, source);
            Record(source, target);
        }

        // Resolves only mappings whose saved destination still has the correct durable tag.
        public ZDO Resolve(CapturedObject source)
        {
            var target = ResolveKey(ExportArchive.Key(source));
            if (target != null)
            {
                Record(source, target);
                return target;
            }
            return null;
        }

        // Treats session IDs as a cache and falls back to the immutable source tag after reload.
        private ZDO ResolveKey(string key)
        {
            if (_map.TryGetValue(key, out var mapped))
            {
                var target = ZDOMan.instance.GetZDO(new ZDOID(long.Parse(mapped.TargetUser, CultureInfo.InvariantCulture), mapped.TargetId));
                if (target != null && RestoreIdentityIndex.SourceKey(target.GetString(IdentityTag, "")) == key)
                {
                    return target;
                }
            }
            return _identities.Resolve(key);
        }

        // Reuses proven equal generated copies while rejecting genuinely ambiguous structures.
        private ZDO Match(CapturedObject source)
        {
            var position = Vector(source.Position);
            var rotation = Rotation(source.Rotation);
            var savedRotation = GeneratedObjectMatch.SavedRotation(rotation);
            var candidates = _zone.Where(z => !_claimed.Contains(z.m_uid) && z.GetPrefab() == source.PrefabHash &&
                string.IsNullOrEmpty(z.GetString(IdentityTag, "")) &&
                Vector3.Distance(z.GetPosition(), position) < 0.02f &&
                (Quaternion.Angle(z.GetRotation(), rotation) < 0.2f ||
                Quaternion.Angle(z.GetRotation(), savedRotation) < 0.2f)).ToList();
            return GeneratedObjectMatch.Choose(source, candidates, _warning);
        }

        // Rechecks the real target before every write or view refresh.
        private static void EnsureSafe(ZDO target, int prefab)
        {
            var instance = ZNetScene.instance.FindInstance(target);
            var prefabObject = ZNetScene.instance.GetPrefab(prefab);
            var protectedPrefab = RestoreProtection.Protected(prefabObject);
            var protectedInstance = instance != null && RestoreProtection.Protected(instance.gameObject);
            if (target.GetPrefab() != prefab || protectedPrefab || protectedInstance)
            {
                var name = prefabObject == null ? prefab.ToString() : prefabObject.name;
                throw new InvalidOperationException("Protected or changed target object; prefab=" + name +
                    "; target=" + target.m_uid + "; actualPrefab=" + target.GetPrefab() +
                    "; protectedPrefab=" + protectedPrefab + "; protectedInstance=" + protectedInstance + ".");
            }
        }

        // Removes only the local view using the game's unload path; the persistent ZDO remains.
        public static void Refresh(ZDO target)
        {
            EnsureSafe(target, target.GetPrefab());
            RemoveView.Invoke(ZNetScene.instance, new object[] { target });
        }

        // Caches runtime identities only; the native source tag survives game restarts.
        private void Record(CapturedObject source, ZDO target)
        {
            var key = ExportArchive.Key(source);
            if (!_map.TryGetValue(key, out var record))
            {
                record = new RestoredObject { Source = key };
                _map.Add(key, record);
                _state.Objects.Add(record);
            }
            record.TargetUser = target.m_uid.UserID.ToString(CultureInfo.InvariantCulture);
            record.TargetId = target.m_uid.ID;
            record.Prefab = target.GetPrefab();
        }

        // Namespaces original source IDs by the world UID, not the changing export contents.
        private string Tag(CapturedObject source)
        {
            return _fingerprint + ":" + ExportArchive.Key(source);
        }

        // Converts validated capture coordinates to Unity values.
        public static Vector3 Vector(float[] value)
        {
            return new Vector3(value[0], value[1], value[2]);
        }

        // Converts validated capture orientation to Unity values.
        public static Quaternion Rotation(float[] value)
        {
            return new Quaternion(value[0], value[1], value[2], value[3]);
        }
    }
}
