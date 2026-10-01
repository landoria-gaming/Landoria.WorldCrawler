using System;
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

        // Uses the player's native sprint speed as the observable restoration pace.
        internal RestoreFlightNavigator(FlightController flight, Player player)
        {
            _flight = flight;
            _player = player;
            _sprintSpeed = SprintFlightSpeed.Read(player);
        }

        // Uses double sprint between distant zones and slows near the zone being restored.
        internal bool Travel(float x, float z, float deltaTime)
        {
            var current = _player.transform.position;
            var destination = new Vector3(x, 0f, z);
            var horizontal = new Vector3(current.x, 0f, current.z);
            var distance = Vector3.Distance(horizontal, destination);
            var speed = distance >= CrawlerConstants.RestoreFastTravelDistance
                ? _sprintSpeed * 2f : _sprintSpeed;
            _flight.Speed = Mathf.Clamp(speed, 1f, 100f);
            var step = Mathf.Clamp(_flight.Speed * deltaTime, 0.1f, 10f);
            var next = Vector3.MoveTowards(horizontal, destination, step);
            next.y = TerrainHeight(next) + CrawlerConstants.RestoreClearance;
            _flight.Tick(next, deltaTime);
            return Vector2.Distance(new Vector2(_player.transform.position.x, _player.transform.position.z),
                new Vector2(x, z)) < 0.25f;
        }

        // Uses loaded modified terrain when possible and falls back to deterministic base terrain.
        private static float TerrainHeight(Vector3 point)
        {
            var water = ZoneSystem.instance == null ? float.NegativeInfinity : ZoneSystem.instance.m_waterLevel;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(point, out var loaded))
            {
                return Mathf.Max(loaded, water);
            }
            if (WorldGenerator.instance == null)
            {
                throw new InvalidOperationException("World generator unavailable during restoration flight.");
            }
            var generated = WorldGenerator.instance.GetHeight(point.x, point.z);
            if (float.IsNaN(generated) || float.IsInfinity(generated))
            {
                throw new InvalidOperationException("Invalid terrain height during restoration flight.");
            }
            return Mathf.Max(generated, water);
        }
    }
}
