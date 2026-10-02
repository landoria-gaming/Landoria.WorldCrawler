using System;
using Landoria.WorldCrawler.Capture;
using UnityEngine;

namespace Landoria.WorldCrawler.Flight
{
    // Pauses only after accepted recording changes or actual restoration mutations.
    internal sealed class ReceiveMotionGate : IDisposable
    {
        private static ReceiveMotionGate _current;
        private float _lastWork = float.NegativeInfinity;

        // Attaches one mutually exclusive operation; raw network traffic never resets this gate.
        internal ReceiveMotionGate(Player player, float timeout)
        {
            if (_current != null)
            {
                throw new InvalidOperationException("A data-work motion gate is already active.");
            }
            _current = this;
        }

        // Starts a two-second quiet period after work that changes the durable result.
        internal static void Worked()
        {
            if (_current != null)
            {
                _current._lastWork = Time.realtimeSinceStartup;
            }
        }

        // Leaves pending loading, capture, write and save locks to their owning controller.
        internal bool Hold()
        {
            return CaptureStability.PauseForReceive(Time.realtimeSinceStartup, _lastWork);
        }

        // Detaches this gate without affecting a successor operation.
        public void Dispose()
        {
            if (ReferenceEquals(_current, this))
            {
                _current = null;
            }
        }
    }
}
