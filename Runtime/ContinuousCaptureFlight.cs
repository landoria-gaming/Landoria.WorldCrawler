using Landoria.WorldCrawler.Flight;
using Landoria.WorldCrawler.Storage;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Flies through a capture sector but never leaves its central area before the snapshot is committed.
    internal sealed class ContinuousCaptureFlight
    {
        private readonly Player _player;
        private readonly TravelNavigator _navigation;
        private Vector2 _exit;
        internal float Speed { get; }

        // Freezes the native sprint target for this run and disables fast cruise, portals and outbound jumps.
        internal ContinuousCaptureFlight(Player player, FlightController flight, float clearance)
        {
            _player = player;
            Speed = SprintFlightSpeed.Read(player);
            flight.Speed = Speed;
            _navigation = new TravelNavigator(flight, player, clearance, false, Speed, 10000f, false);
        }

        // Starts observation on approach once the source sector is genuinely resident.
        internal bool Approach(ZoneEntry zone, float dt)
        {
            var center = new Vector3(zone.X * 64f, 0f, zone.Z * 64f);
            _navigation.Travel(center.x, center.z, dt);
            var position = _player.transform.position;
            return Vector2.Distance(new Vector2(position.x, position.z), new Vector2(center.x, center.z)) <= 48f &&
                ZoneSystem.instance.IsZoneLoaded(center);
        }

        // Aims toward the next planned zone while keeping a generous residency margin for this capture.
        internal void Begin(ZoneEntry zone, ZoneEntry next)
        {
            var center = new Vector2(zone.X * 64f, zone.Z * 64f);
            var position = new Vector2(_player.transform.position.x, _player.transform.position.z);
            var direction = next == null ? center - position : new Vector2(next.X * 64f, next.Z * 64f) - center;
            if (direction.sqrMagnitude < 0.01f) { direction = Vector2.up; }
            _exit = center + direction.normalized * 24f;
        }

        // Keeps moving during capture and disk writes; holds at the exit only while validation is pending.
        internal void Tick(float dt) { _navigation.Travel(_exit.x, _exit.y, dt); }
    }
}
