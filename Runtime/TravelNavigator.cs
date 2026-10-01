using System;
using System.Collections.Generic;
using Landoria.WorldCrawler.Flight;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Prefers useful native portal links and otherwise flies above sampled terrain.
    internal sealed class TravelNavigator
    {
        private readonly FlightController _flight;
        private readonly Player _player;
        private readonly float _clearance;
        private readonly float _speed;
        private readonly float _cruiseSpeed;
        private readonly float _cruiseThreshold;
        private readonly bool _preferPortals;
        private readonly bool _allowCoordinateJumps;
        private readonly float _jumpThreshold;
        private readonly HashSet<ZDOID> _failedPortals = new HashSet<ZDOID>();
        private float _altitude;
        private Vector2 _destination;
        private bool _hasDestination;
        private PortalRoute _portal;
        private Vector3? _portalLanding;
        private bool _descending;
        private float _portalWait;
        private bool _jumpAttempted;
        private bool _forceCoordinateJump;
        public bool NativeTransitActive => _flight.NativeTransitActive;

        // Keeps navigation independent of version-specific zone value types.
        public TravelNavigator(FlightController flight, Player player, float clearance,
            bool preferPortals = true, float cruiseSpeed = 60f, float cruiseThreshold = 500f,
            bool allowCoordinateJumps = true, float jumpThreshold = 500f)
        {
            _flight = flight;
            _player = player;
            _clearance = Mathf.Clamp(clearance, 40f, 200f);
            _speed = Mathf.Clamp(flight.Speed, 1f, 100f);
            _cruiseSpeed = Mathf.Clamp(cruiseSpeed, _speed, 100f);
            _cruiseThreshold = Mathf.Clamp(cruiseThreshold, 128f, 10000f);
            _preferPortals = preferPortals;
            _allowCoordinateJumps = allowCoordinateJumps;
            _jumpThreshold = Mathf.Clamp(jumpThreshold, 128f, 10000f);
            _altitude = player.transform.position.y;
        }

        // Finishes any native transition before accepting a changed destination or pause-return leg.
        public bool Travel(float x, float z, float deltaTime, bool forceCoordinateJump = false)
        {
            if (_flight.NativeTransitActive)
            {
                if (!_flight.TickNativeTransit()) { return false; }
                if (!_flight.NativeTransitSucceeded && _portal != null) { _failedPortals.Add(_portal.Source); }
                _portal = null;
                _jumpAttempted = true;
                _altitude = _player.transform.position.y;
            }
            var destination = new Vector2(x, z);
            if (!_hasDestination || _destination != destination || _forceCoordinateJump != forceCoordinateJump)
            {
                _hasDestination = true;
                _destination = destination;
                _portalLanding = null;
                _descending = false;
                _portalWait = 0f;
                _jumpAttempted = false;
                _forceCoordinateJump = forceCoordinateJump;
                _portal = _preferPortals && !forceCoordinateJump
                    ? PortalRoutePlanner.Choose(_player.transform.position, new Vector3(x, 0, z), _failedPortals) : null;
            }
            if (_portal != null) { AdvancePortal(deltaTime); return false; }
            if (TryCoordinateJump(x, z, forceCoordinateJump)) { return false; }
            return Fly(x, z, deltaTime);
        }

        // Tries at most one authorized native jump for a distant known itinerary endpoint.
        private bool TryCoordinateJump(float x, float z, bool forced)
        {
            var current = _player.transform.position;
            var distance = Vector2.Distance(new Vector2(current.x, current.z), new Vector2(x, z));
            if (!PortalRouteMetric.ShouldJump(_allowCoordinateJumps, _jumpAttempted, distance, _jumpThreshold, forced))
            { return false; }
            _jumpAttempted = true;
            var target = new Vector3(x, Mathf.Max(30f, Height(new Vector3(x, 0f, z))) + _clearance, z);
            if (!_flight.BeginCoordinateTransit(target)) { return false; }
            HudNotification.Show(forced ? "Teleporting to the starting point; waiting for loading..."
                : "Teleporting to the next known zone; waiting for loading...");
            return true;
        }

        // Samples ahead and climbs at ordinary speed before permitting faster horizontal travel.
        private bool Fly(float x, float z, float deltaTime)
        {
            var current = _player.transform.position;
            var ground = new Vector3(x, 0f, z);
            var horizontal = new Vector3(current.x, 0f, current.z);
            var next = Vector3.MoveTowards(horizontal, ground, 32f);
            var middle = (horizontal + next) * 0.5f;
            var terrain = Mathf.Max(Height(next), Height(middle), Height(horizontal));
            _altitude = Mathf.Max(_altitude, terrain + _clearance);
            if (current.y < _altitude - 0.5f)
            {
                _flight.Speed = _speed;
                _flight.Tick(new Vector3(current.x, _altitude, current.z), deltaTime);
                return false;
            }
            _flight.Speed = Vector3.Distance(horizontal, ground) >= _cruiseThreshold ? _cruiseSpeed : _speed;
            _flight.Tick(new Vector3(next.x, _altitude, next.z), deltaTime);
            return Vector2.Distance(new Vector2(_player.transform.position.x, _player.transform.position.z),
                new Vector2(x, z)) < 0.25f;
        }

        // Loads a real source portal before descending to a safe point just outside its trigger.
        private void AdvancePortal(float deltaTime)
        {
            if (!_portal.TryResolve(out var source, out var target)) { RejectPortal("its link changed"); return; }
            if (!_portalLanding.HasValue)
            {
                if (!Fly(source.GetPosition().x, source.GetPosition().z, deltaTime)) { return; }
                if (_portalWait == 0f) { _portalWait = Time.time; }
                _flight.Tick(_player.transform.position, deltaTime);
                var view = ZNetScene.instance.FindInstance(source);
                var portal = view == null ? null : view.GetComponent<TeleportWorld>();
                if (portal != null && PortalApproach.TryPosition(portal, out var landing)) { _portalLanding = landing; }
                else if (Time.time - _portalWait > 20f) { RejectPortal("its approach was not safely loaded"); }
                return;
            }
            var point = _portalLanding.Value;
            if (!_descending)
            {
                if (!Fly(point.x, point.z, deltaTime)) { return; }
                _descending = true;
            }
            _flight.Speed = _speed;
            if (!_flight.Tick(point, deltaTime)) { return; }
            EnterPortal(source, target);
        }

        // Delegates passage to the game's real portal so carried items and world rules still apply.
        private void EnterPortal(ZDO source, ZDO target)
        {
            var view = ZNetScene.instance.FindInstance(source);
            var portal = view == null ? null : view.GetComponent<TeleportWorld>();
            if (portal == null || !portal.isActiveAndEnabled || float.IsNaN(portal.m_exitDistance) ||
                float.IsInfinity(portal.m_exitDistance) || Mathf.Abs(portal.m_exitDistance) > 20f)
            { RejectPortal("its live instance was unavailable"); return; }
            var exit = target.GetPosition() + target.GetRotation() * Vector3.forward * portal.m_exitDistance + Vector3.up;
            if (!_flight.BeginPortalTransit(portal, exit))
            { RejectPortal("native portal rules refused passage"); return; }
            HudNotification.Show("Using an existing portal, then continuing the route...");
        }

        // Falls back to normal flight without repeatedly retrying a blocked source during this run.
        private void RejectPortal(string reason)
        {
            _failedPortals.Add(_portal.Source);
            _portal = null;
            Debug.LogWarning("World Crawler: continuing by flight because " + reason + ".");
        }

        // Moves to a bounded observation height once the destination sector has loaded.
        public bool Observe(float x, float z, float deltaTime)
        {
            _flight.Speed = _speed;
            var target = new Vector3(x, Mathf.Max(30f, Height(new Vector3(x, 0f, z))) + _clearance, z);
            return _flight.Tick(target, deltaTime);
        }

        // Uses the connected world's generator instead of assuming a constant flight height.
        private static float Height(Vector3 point)
        {
            if (WorldGenerator.instance == null) { throw new InvalidOperationException("World generator unavailable."); }
            var height = WorldGenerator.instance.GetHeight(point.x, point.z);
            if (float.IsNaN(height) || float.IsInfinity(height))
            {
                throw new InvalidOperationException("Invalid terrain height during flight.");
            }
            return height;
        }
    }
}
