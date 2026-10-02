using System;
using UnityEngine;

namespace Landoria.WorldCrawler.Flight
{
    // Lands at the current horizontal location without returning to a route's starting point.
    internal sealed class FlightLanding
    {
        private readonly FlightController _flight;
        private readonly Player _player;
        private readonly float _started = Time.unscaledTime;

        // Captures the active flight without selecting any old checkpoint.
        internal FlightLanding(FlightController flight, Player player)
        {
            _flight = flight;
            _player = player;
        }

        // Waits for local terrain, then releases physics just above its loaded surface.
        internal bool Step(float deltaTime)
        {
            if (_flight == null || !_flight.Active)
            {
                return true;
            }
            if (Time.unscaledTime - _started > 180f)
            {
                throw new TimeoutException("Local landing timed out; no return was attempted.");
            }
            var point = _player.transform.position;
            var terrain = Heightmap.FindHeightmap(point);
            if (!ZNetScene.instance.IsAreaReady(point) || terrain == null || terrain.HaveQueuedRebuild())
            {
                _flight.Tick(point, deltaTime);
                return false;
            }
            var height = SurfaceHeight.Read(point);
            if (ZoneSystem.instance.GetSolidHeight(point, out var solid, 1000))
            {
                height = Mathf.Max(height, solid);
            }
            point.y = height + 0.5f;
            if (!_flight.Tick(point, deltaTime))
            {
                return false;
            }
            _flight.End();
            return true;
        }
    }
}
