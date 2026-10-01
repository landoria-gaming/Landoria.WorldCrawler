using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Uses the game's normal portal-target request for already-known links, never a server-wide scan.
    internal sealed class LandmarkPortalResolver
    {
        private const int MaximumTargets = 256;
        private readonly ZDOMan _manager;
        private readonly ZNet _network;
        private readonly long _worldUid;
        private readonly Dictionary<ZDOID, int> _pending = new Dictionary<ZDOID, int>();
        private readonly Queue<ZDOID> _requests = new Queue<ZDOID>();
        private readonly float _started;
        private float _nextBatch;
        private bool _done;
        internal List<string> Warnings { get; } = new List<string>();

        // Freezes eligible target IDs so requests cannot recursively expand beyond observed links.
        internal LandmarkPortalResolver()
        {
            _manager = ZDOMan.instance ?? throw new InvalidOperationException("The portal cache is not ready.");
            _network = ZNet.instance ?? throw new InvalidOperationException("The network session is not ready.");
            _worldUid = ZNet.World?.m_uid ?? throw new InvalidOperationException("No connected world.");
            _started = Time.realtimeSinceStartup;
            if (_network.IsServer())
            {
                _done = true;
                return;
            }
            CollectTargets();
        }

        // Advances bounded requests on the main thread and finishes after replies or a short timeout.
        internal bool Step()
        {
            if (_done)
            {
                return true;
            }
            if (ZDOMan.instance != _manager || ZNet.instance != _network || ZNet.World?.m_uid != _worldUid
                || ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected)
            {
                throw new InvalidOperationException("The world connection changed while resolving portal destinations.");
            }
            foreach (var id in _pending.Keys.ToArray())
            {
                if (LandmarkPortalSource.ValidPortal(_manager.GetZDO(id)))
                {
                    _pending.Remove(id);
                }
            }
            if (_pending.Count == 0)
            {
                _done = true;
                return true;
            }
            if (Time.realtimeSinceStartup - _started >= 10f)
            {
                return Finish();
            }
            if (Time.realtimeSinceStartup >= _nextBatch)
            {
                RequestBatch();
            }
            return false;
        }

        // Deduplicates missing native endpoint IDs and bounds memory and request volume.
        private void CollectTargets()
        {
            var overflow = false;
            foreach (var portal in LandmarkPortalSource.Portals())
            {
                if (!LandmarkPortalSource.ValidPortal(portal))
                {
                    continue;
                }
                var target = portal.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal);
                if (target == ZDOID.None || _pending.ContainsKey(target)
                    || LandmarkPortalSource.ValidPortal(_manager.GetZDO(target)))
                {
                    continue;
                }
                if (_pending.Count == MaximumTargets)
                {
                    overflow = true;
                    continue;
                }
                _pending.Add(target, 0);
                _requests.Enqueue(target);
            }
            if (overflow)
            {
                Warnings.Add("Portal endpoint requests were capped at 256 known links; additional endpoints remain unresolved.");
            }
        }

        // Sends no more than four requests per batch, forty per second, and two per target.
        private void RequestBatch()
        {
            _nextBatch = Time.realtimeSinceStartup + 0.1f;
            var count = Math.Min(4, _requests.Count);
            for (var index = 0; index < count; index++)
            {
                var id = _requests.Dequeue();
                if (!_pending.TryGetValue(id, out var attempts))
                {
                    continue;
                }
                if (attempts > 0 && Time.realtimeSinceStartup - _started < 2f)
                {
                    _requests.Enqueue(id);
                    continue;
                }
                _manager.RequestZDO(id);
                _pending[id] = attempts + 1;
                if (attempts == 0)
                {
                    _requests.Enqueue(id);
                }
            }
        }

        // Keeps unresolved targets visible in the inventory report instead of guessing coordinates.
        private bool Finish()
        {
            Warnings.Add(_pending.Count + " known portal endpoint(s) did not provide a valid position "
                + "within ten seconds and remain outside the selected route.");
            _done = true;
            return true;
        }
    }
}
