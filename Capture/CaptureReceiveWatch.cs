using System;
using System.Collections.Generic;
using UnityEngine;

namespace Landoria.WorldCrawler.Capture
{
    // Tracks actual received objects for the active sector without treating all network traffic as loading.
    internal sealed class CaptureReceiveWatch : IDisposable
    {
        private static CaptureReceiveWatch _current;
        private readonly int _x;
        private readonly int _z;
        private readonly HashSet<ZDOID> _seen = new HashSet<ZDOID>();
        private readonly CaptureReceivePolicy _policy = new CaptureReceivePolicy();
        internal float LastChange { get; private set; }
        internal string Error { get; private set; }

        // Identifies persistent state whose revisions must stabilize before accepting a snapshot.
        internal bool TracksUpdates(int hash) { return _policy.For(hash) == 2; }

        // Installs a single observation-only receiver for the current capture.
        internal CaptureReceiveWatch(int x, int z)
        {
            if (_current != null) { throw new InvalidOperationException("A capture receiver is already active."); }
            _x = x; _z = z;
            LastChange = Time.realtimeSinceStartup;
            _current = this;
        }

        // Contains observer failures so the mod cannot interrupt the game's packet processing.
        internal static void Received(ZDO source)
        {
            var watch = _current;
            if (watch == null) { return; }
            try { watch.Observe(source); }
            catch (Exception error) { watch.Error = error.Message; }
        }

        // Resets stability for new objects and important state updates, not ticking fires or creatures.
        private void Observe(ZDO source)
        {
            if (source == null || !source.IsValid() || !source.Persistent ||
                !CaptureTransform.InZone(source.GetPosition(), _x, _z)) { return; }
            var policy = _policy.For(source.GetPrefab());
            if (policy == 0) { return; }
            if (_seen.Add(source.m_uid) || policy == 2) { LastChange = Time.realtimeSinceStartup; }
        }

        // Removes only this session's receiver when a zone finishes, pauses or fails.
        public void Dispose()
        {
            if (ReferenceEquals(_current, this)) { _current = null; }
        }
    }
}
