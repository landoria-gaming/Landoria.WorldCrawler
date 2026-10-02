using System;
using UnityEngine;

namespace Landoria.WorldCrawler.Capture
{
    // Shares accepted-change timing with one active sector validation.
    internal sealed class CaptureReceiveWatch : IDisposable
    {
        private static CaptureReceiveWatch _current;
        private readonly int _x, _z;
        internal float LastChange { get; private set; }

        // Starts a local stability window, separate from any earlier committed snapshot.
        internal CaptureReceiveWatch(int x, int z)
        {
            if (_current != null)
            {
                throw new InvalidOperationException("A capture receiver is already active.");
            }
            _x = x;
            _z = z;
            LastChange = Time.realtimeSinceStartup;
            _current = this;
        }

        // Updates only after classification and content deduplication, never for raw traffic.
        internal static void Changed(int x, int z)
        {
            if (_current != null && _current._x == x && _current._z == z)
            {
                _current.LastChange = Time.realtimeSinceStartup;
            }
        }

        // Detaches the watch when validation succeeds or is cancelled.
        public void Dispose()
        {
            if (ReferenceEquals(_current, this))
            {
                _current = null;
            }
        }
    }
}
