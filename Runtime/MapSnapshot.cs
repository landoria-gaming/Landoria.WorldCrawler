using System;
using System.Collections;
using System.Reflection;
using Landoria.WorldCrawler.Inventory;

namespace Landoria.WorldCrawler.Runtime
{
    // Copies the loaded map on the main thread for a frozen, resumable inventory.
    internal sealed class MapSnapshot
    {
        private readonly BitArray _personal;
        private readonly BitArray _shared;
        private readonly int _size;
        private readonly float _pixelSize;

        // Takes an independent snapshot so flight cannot expand this inventory.
        private MapSnapshot(Minimap map)
        {
            _personal = ReadBits(map, "m_explored");
            _shared = ReadBits(map, "m_exploredOthers");
            _size = map.m_textureSize;
            _pixelSize = map.m_pixelSize;
        }

        // Requires saved exploration to have been loaded before taking a snapshot.
        public static MapSnapshot Read()
        {
            var map = Minimap.instance;
            if (map == null || !(bool)Field("m_hasGenerated").GetValue(map))
            {
                throw new InvalidOperationException("Wait for the map to finish loading before pressing F8.");
            }
            return new MapSnapshot(map);
        }

        // Converts either supported map representation to a copied bit array.
        private static BitArray ReadBits(Minimap map, string name)
        {
            var value = Field(name).GetValue(map);
            if (value is bool[] booleans) { return new BitArray(booleans); }
            if (value is BitArray bits) { return new BitArray(bits); }
            throw new NotSupportedException("Unexpected minimap data representation: " + name);
        }

        // Resolves checked private minimap fields without binding to their varying types.
        private static FieldInfo Field(string name)
        {
            return typeof(Minimap).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new MissingFieldException(typeof(Minimap).FullName, name);
        }

        // Builds a pure-data inventory on a worker thread.
        public InventoryResult Build()
        {
            return ExploredZoneInventory.Build(_personal, _shared, _size, _pixelSize);
        }
    }
}
