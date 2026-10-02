using System.Globalization;
using Landoria.WorldCrawler.Capture;
using Landoria.WorldCrawler.Restoration;
using Landoria.WorldCrawler.Restoration.Persistence;

namespace Landoria.WorldCrawler.Restoration.Objects
{
    // Finishes original cross-zone links once, without rewriting later player changes.
    internal sealed partial class ObjectRestorer
    {
        // Stores unfinished original link information on the native object, not in a ledger.
        private static void RememberConnection(ZDO target, CapturedObject source)
        {
            var key = source.ConnectionTargetUser + ":" + source.ConnectionTargetId.ToString(CultureInfo.InvariantCulture);
            var empty = source.ConnectionType == 0 || key == "0:0";
            target.SetConnection((ZDOExtraData.ConnectionType)source.ConnectionType, ZDOID.None);
            target.Set("WorldCrawler.connectionType", source.ConnectionType);
            target.Set("WorldCrawler.connectionTarget", empty ? "" : key);
            target.Set("WorldCrawler.connectionReady", empty);
        }

        // Returns whether native data changed, never waiting for an empty or missing endpoint.
        public bool Connect(CapturedObject source, ExportArchive archive)
        {
            var target = Resolve(source);
            if (target == null || target.GetBool("WorldCrawler.connectionReady", true))
            {
                return false;
            }
            var current = target.GetConnection();
            if (current != null && current.m_target != ZDOID.None)
            {
                return CompleteConnection(target);
            }
            var type = (ZDOExtraData.ConnectionType)target.GetInt("WorldCrawler.connectionType", 0);
            var key = target.GetString("WorldCrawler.connectionTarget", "");
            if (type == ZDOExtraData.ConnectionType.None || string.IsNullOrEmpty(key) || key == "0:0")
            {
                return CompleteEmptyConnection(target, type);
            }
            return ConnectEndpoint(source, target, type, key, archive);
        }

        // Separates missing capture data from a captured destination that still needs restoration.
        private bool ConnectEndpoint(CapturedObject source, ZDO target, ZDOExtraData.ConnectionType type,
            string key, ExportArchive archive)
        {
            var destination = archive.FindObject(key);
            if (!RestoreRecordPolicy.Include(destination))
            {
                if (type == ZDOExtraData.ConnectionType.Spawned &&
                    ZNetScene.instance.GetPrefab(source.PrefabHash).GetComponent<CreatureSpawner>() != null)
                {
                    return CompleteEmptyConnection(target, type);
                }
                _warning("Link target " + key + " is absent from the restorable export; " +
                    RestoreWarnings.Describe(source) + ". If the destination still exists, export its area before retrying F10. " +
                    "Zone progress is unaffected.");
                return false;
            }
            var endpoint = ResolveKey(key);
            if (endpoint == null)
            {
                _warning("Link target " + key + " is exported but not restored; " + RestoreWarnings.Describe(source) +
                    "; destination zone=" + destination.ZoneX + ":" + destination.ZoneZ +
                    ". The link will be retried after a zone restoration; no source-zone revisit is required.");
                return false;
            }
            return ApplyConnection(source, target, type, endpoint, destination);
        }

        // Restores the original link once without taking over a destination already used by a portal.
        private bool ApplyConnection(CapturedObject source, ZDO target, ZDOExtraData.ConnectionType type,
            ZDO endpoint, CapturedObject destination)
        {
            if (target.GetPrefab() != source.PrefabHash || endpoint.GetPrefab() != destination.PrefabHash)
            {
                _warning("Link retained without changes because an imported endpoint's prefab changed; " +
                    RestoreWarnings.Describe(source));
                return false;
            }
            if (target == endpoint)
            {
                _warning("Ignored a self-referencing link; " + RestoreWarnings.Describe(source));
                return CompleteConnection(target);
            }
            EnsureSafe(target, target.GetPrefab());
            EnsureSafe(endpoint, endpoint.GetPrefab());
            if (type == ZDOExtraData.ConnectionType.Portal)
            {
                return ConnectPortal(source, target, endpoint, destination);
            }
            if (type != ZDOExtraData.ConnectionType.SyncTransform && type != ZDOExtraData.ConnectionType.Spawned)
            {
                _warning("Ignored unsupported link type " + type + "; " + RestoreWarnings.Describe(source));
                return CompleteConnection(target);
            }
            target.SetConnection(type, endpoint.m_uid);
            return CompleteConnection(target);
        }

        // Links both portal ends in the same frame, preserving local tags and established destinations.
        private bool ConnectPortal(CapturedObject source, ZDO target, ZDO endpoint, CapturedObject destination)
        {
            var other = endpoint.GetConnection();
            if (other != null && other.m_target != ZDOID.None ||
                endpoint.GetBool("WorldCrawler.connectionReady", true) ||
                target.GetString(ZDOVars.s_tag) != endpoint.GetString(ZDOVars.s_tag) ||
                destination.ConnectionType != (int)ZDOExtraData.ConnectionType.Portal ||
                destination.ConnectionTargetUser != source.SourceUser || destination.ConnectionTargetId != source.SourceId)
            {
                _warning("Original portal link was not forced: the destination is already assigned, local tags differ, " +
                    "or captured endpoints disagree; " + RestoreWarnings.Describe(source) +
                    ". Existing portal state is preserved; Valheim can pair free portals by their current tags.");
                return CompleteConnection(target);
            }
            target.SetConnection(ZDOExtraData.ConnectionType.Portal, endpoint.m_uid);
            endpoint.SetConnection(ZDOExtraData.ConnectionType.Portal, target.m_uid);
            CompleteConnection(endpoint);
            return CompleteConnection(target);
        }

        // Repairs older pending empty links while preserving the spawner's already-used state.
        private static bool CompleteEmptyConnection(ZDO target, ZDOExtraData.ConnectionType type)
        {
            EnsureSafe(target, target.GetPrefab());
            var current = target.GetConnection();
            if (current == null || current.m_type == ZDOExtraData.ConnectionType.None)
            {
                target.SetConnection(type, ZDOID.None);
            }
            return CompleteConnection(target);
        }

        // Stops future import writes to a link once it has been restored or superseded locally.
        private static bool CompleteConnection(ZDO target)
        {
            EnsureSafe(target, target.GetPrefab());
            target.Set("WorldCrawler.connectionReady", true);
            return true;
        }
    }
}
