using System;
using System.Collections.Generic;
using Landoria.WorldCrawler.Capture;
using UnityEngine;

namespace Landoria.WorldCrawler.Flight
{
    // Stops controlled travel while nearby useful records are still arriving.
    internal sealed class ReceiveMotionGate : IDisposable
    {
        private static ReceiveMotionGate _current;
        private readonly Player _player;
        private readonly CaptureReceivePolicy _policy = new CaptureReceivePolicy();
        private readonly HashSet<ZDOID> _seen = new HashSet<ZDOID>();
        private Vector2 _area;
        private bool _hasArea;
        private float _lastReceived = float.NegativeInfinity;
        private float _blockedSince = -1f;
        private readonly float _timeout;
        private string _error;

        // Attaches to one active export or restoration flight at a time.
        internal ReceiveMotionGate(Player player, float timeout)
        {
            if (_current != null)
            {
                throw new InvalidOperationException("A receive motion gate is already active.");
            }
            _player = player;
            _timeout = timeout;
            _current = this;
        }

        // Records arrivals without letting observer errors escape into packet processing.
        internal static void Received(ZDO source)
        {
            var gate = _current;
            if (gate == null)
            {
                return;
            }
            try
            {
                gate.Observe(source);
            }
            catch (Exception error)
            {
                gate._error = error.Message;
            }
        }

        // Ignores distant portal-cache records and repeated volatile updates outside the capture objective.
        private void Observe(ZDO source)
        {
            if (_player == null || source == null || !source.IsValid() || !source.Persistent)
            {
                return;
            }
            var position = _player.transform.position;
            var point = source.GetPosition();
            if (Mathf.Abs(point.x - position.x) > 128f || Mathf.Abs(point.z - position.z) > 128f)
            {
                return;
            }
            var area = new Vector2(Mathf.Floor((position.x + 32f) / 64f), Mathf.Floor((position.z + 32f) / 64f));
            if (!_hasArea || _area != area)
            {
                _seen.Clear();
                _area = area;
                _hasArea = true;
            }
            var policy = _policy.For(source.GetPrefab());
            if (policy != 0 && (_seen.Add(source.m_uid) || policy == 2))
            {
                _lastReceived = Time.realtimeSinceStartup;
            }
        }

        // Extends the quiet window only when capture or restoration actually processes data.
        internal static void Worked()
        {
            if (_current != null)
            {
                _current._lastReceived = Time.realtimeSinceStartup;
            }
        }

        // Resumes after two quiet seconds and fails safely instead of holding an unattended player forever.
        internal bool Hold()
        {
            if (_error != null)
            {
                throw new InvalidOperationException("Receive motion gate failed: " + _error);
            }
            var now = Time.realtimeSinceStartup;
            var blocked = CaptureStability.PauseForReceive(now, _lastReceived) || ZNet.instance.HasBadConnection();
            if (!blocked)
            {
                _blockedSince = -1f;
                return false;
            }
            if (_blockedSince < 0f)
            {
                _blockedSince = now;
            }
            if (now - _blockedSince >= _timeout)
            {
                throw new TimeoutException("Nearby data kept arriving or the connection remained unhealthy; travel stopped.");
            }
            return true;
        }

        // Detaches this gate without affecting any successor session.
        public void Dispose()
        {
            if (ReferenceEquals(_current, this))
            {
                _current = null;
            }
        }
    }
}
