using System;
using System.Globalization;
using System.Linq;
using Landoria.WorldCrawler.Inventory;
using UnityEngine;

namespace Landoria.WorldCrawler.Runtime
{
    // Copies only client-known points of interest before any automatic travel begins.
    internal static class LandmarkSnapshot
    {
        // Runs on the main thread and returns detached data safe for background planning.
        internal static LandmarkInventory Read()
        {
            if (Minimap.instance == null || ZoneSystem.instance == null || ZDOMan.instance == null
                || Game.instance == null || !LandmarkMapSource.Ready(Minimap.instance))
            {
                throw new InvalidOperationException("Wait for the world and map to finish loading before pressing F8.");
            }
            var result = new LandmarkInventory();
            LandmarkPortalSource.Read(result);
            LandmarkMapSource.Read(result);
            result.Points = result.Points.OrderBy(point => point.Kind, StringComparer.Ordinal)
                .ThenBy(point => point.Id, StringComparer.Ordinal).ToList();
            return result;
        }

        // Adds a finite observation and combines provenance when the same native point repeats.
        internal static void Add(LandmarkInventory result, string id, string kind, string name,
            string source, Vector3 position)
        {
            if (!Finite(position.x) || !Finite(position.y) || !Finite(position.z))
            {
                result.Warnings.Add("An invalid " + kind + " position was omitted from the landmark inventory.");
                return;
            }
            var existing = result.Points.FirstOrDefault(point => point.Id == id);
            if (existing != null)
            {
                if (!existing.Source.Split('+').Contains(source))
                {
                    existing.Source += "+" + source;
                }
                return;
            }
            result.Points.Add(new LandmarkPoint
            {
                Id = id,
                Kind = kind,
                Name = name ?? "",
                Source = source,
                X = position.x,
                Y = position.y,
                Z = position.z
            });
        }

        // Uses actual coordinates rather than translated labels to identify saved map points.
        internal static string PositionId(string kind, Vector3 position)
        {
            return kind + ":" + position.x.ToString("R", CultureInfo.InvariantCulture)
                + ":" + position.z.ToString("R", CultureInfo.InvariantCulture);
        }

        // Rejects invalid coordinates without depending on newer framework helpers.
        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
