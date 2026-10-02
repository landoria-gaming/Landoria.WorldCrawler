using Landoria.WorldCrawler.Flight;
using Landoria.WorldCrawler.Runtime;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration
{
    // Flies visibly above the terrain without portals or coordinate jumps during restoration.
    internal sealed class RestoreFlightNavigator
    {
        private readonly FlightController _flight;
        private readonly Player _player;
        private readonly float _sprintSpeed;
        private readonly float _travelMultiplier;
        private readonly float _approachMultiplier;

        // Uses the player's native sprint speed as the observable restoration pace.
        internal RestoreFlightNavigator(FlightController flight, Player player, float travelMultiplier = 2f, float approachMultiplier = 1f)
        {
            _flight = flight;
            _player = player;
            _sprintSpeed = SprintFlightSpeed.Read(player);
            _travelMultiplier = travelMultiplier;
            _approachMultiplier = approachMultiplier;
        }

        // Uses the selected travel pace while following the surface without skipping zone centers.
        internal bool Travel(float x, float z, float deltaTime)
        {
            var current = _player.transform.position;
            var destination = new Vector3(x, 0f, z);
            var horizontal = new Vector3(current.x, 0f, current.z);
            var distance = Vector3.Distance(horizontal, destination);
            var speed = distance >= CrawlerConstants.RestoreFastTravelDistance
                ? _sprintSpeed * _travelMultiplier : _sprintSpeed * _approachMultiplier;
            _flight.Speed = Mathf.Clamp(speed, 1f, 100f);
            var step = Mathf.Clamp(_flight.Speed * deltaTime, 0.1f, 10f);
            var next = Vector3.MoveTowards(horizontal, destination, step);
            next.y = SurfaceHeight.Read(next) + CrawlerConstants.RestoreClearance;
            _flight.Tick(next, deltaTime);
            return Vector2.Distance(new Vector2(_player.transform.position.x, _player.transform.position.z),
                new Vector2(x, z)) < 0.25f;
        }
    }
}
