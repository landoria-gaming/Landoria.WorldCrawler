using System.Globalization;
using Landoria.WorldCrawler.Capture;

namespace Landoria.WorldCrawler.Restoration
{
    // Finishes original cross-zone links once, without rewriting later player changes.
    internal sealed partial class ObjectRestorer
    {
        // Stores unfinished original link information on the native object, not in a ledger.
        private static void RememberConnection(ZDO target, CapturedObject source)
        {
            target.Set("WorldCrawler.connectionType", source.ConnectionType);
            target.Set("WorldCrawler.connectionTarget", source.ConnectionType == 0 ? "" :
                source.ConnectionTargetUser + ":" + source.ConnectionTargetId.ToString(CultureInfo.InvariantCulture));
            target.Set("WorldCrawler.connectionReady", source.ConnectionType == 0);
        }

        // Resolves the saved original endpoint only until the object's first link succeeds.
        public bool Connect(CapturedObject source)
        {
            var target = Resolve(source);
            if (target == null || target.GetBool("WorldCrawler.connectionReady", true))
            {
                return true;
            }
            var key = target.GetString("WorldCrawler.connectionTarget", "");
            var endpoint = ResolveKey(key);
            if (endpoint == null)
            {
                return false;
            }
            EnsureSafe(target, target.GetPrefab());
            EnsureSafe(endpoint, endpoint.GetPrefab());
            var type = (ZDOExtraData.ConnectionType)target.GetInt("WorldCrawler.connectionType", 0);
            target.SetConnection(type, endpoint.m_uid);
            target.Set("WorldCrawler.connectionReady", true);
            return true;
        }
    }
}
