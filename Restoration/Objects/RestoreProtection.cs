using System.Collections.Generic;
using Landoria.WorldCrawler.Capture;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration.Objects
{
    // Protects living actors and prevents temporary structural collapse during a zone import.
    internal static class RestoreProtection
    {
        private static readonly HashSet<string> Held = new HashSet<string>();
        public static bool Active
        {
            get; set;
        }

        // Rejects an actor or its children without treating unrelated descendants as the parent object.
        public static bool Protected(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return true;
            }
            var current = gameObject.transform;
            while (current != null)
            {
                if (CaptureExclusionPolicy.Classify(current.gameObject) != null)
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }

        // Recognizes unfinished imported objects after a pause or a world reload.
        internal static bool Pending(ZNetView view)
        {
            var data = view == null ? null : view.GetZDO();
            return data != null && data.GetBool("WorldCrawler.pending", false);
        }

        // Retains a zone's supports while neighboring exported pieces are still being restored.
        public static void Hold(int x, int z)
        {
            Held.Add(x + ":" + z);
        }

        // Limits wear suppression to the explicitly active import sectors.
        public static bool HoldWear(Vector3 position)
        {
            return Active && Held.Contains(Mathf.FloorToInt((position.x + 32f) / 64f) + ":" +
                Mathf.FloorToInt((position.z + 32f) / 64f));
        }

        // Removes temporary protection after the controlled import stops.
        public static void Clear()
        {
            Active = false;
            Held.Clear();
        }
    }
}
