using System;
using Landoria.WorldCrawler.Storage;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Tracks controlled-flight recovery without returning to any stored location.
    internal static class SessionCheckpoint
    {
        // Records the current flight start only for diagnostics and airborne recovery.
        public static void Begin(WorldStore store, Player player)
        {
            var manifest = store.Manifest;
            {
                var position = player.transform.position;
                var rotation = player.transform.rotation;
                manifest.ReturnPosition = new[] { position.x, position.y, position.z };
                manifest.ReturnRotation = new[] { rotation.x, rotation.y, rotation.z, rotation.w };
                manifest.ReturnCharacterId = manifest.CharacterId;
            }
            manifest.ReturnPending = true;
            manifest.GameVersion = GameContext.GameVersion;
            SetState(store, "crawling", null);
        }

        // Reads legacy position metadata for diagnostics only.
        public static Vector3 Origin(WorldStore store)
        {
            var values = store.Manifest.ReturnPosition;
            return new Vector3(values[0], values[1], values[2]);
        }

        // Reads legacy orientation metadata for diagnostics only.
        public static Quaternion Rotation(WorldStore store)
        {
            var values = store.Manifest.ReturnRotation;
            return new Quaternion(values[0], values[1], values[2], values[3]);
        }

        // Saves a visible state transition without discarding the recovery location.
        public static void SetState(WorldStore store, string state, string error)
        {
            store.Manifest.CrawlState = state;
            store.Manifest.LastError = error;
            store.Save();
        }

        // Clears airborne recovery once the character has landed at its current location.
        public static void Finish(WorldStore store, bool completed)
        {
            store.Manifest.ReturnPending = false;
            SetState(store, completed ? "completed" : "paused", null);
        }
    }
}
