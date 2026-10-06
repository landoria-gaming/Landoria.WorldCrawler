using System;
using System.Collections.Generic;
using System.IO;
using Landoria.WorldCrawler.Capture;
using UnityEngine;

namespace Landoria.WorldCrawler.Commands
{
    // Reads and rewrites one native terrain compiler without touching its paint data.
    internal sealed class TerrainCompilerData
    {
        private const int Width = 65;
        private readonly byte[] _header;
        private readonly byte[] _tail;
        private readonly bool[] _modified = new bool[Width * Width];
        private readonly float[] _level = new float[Width * Width];
        private readonly float[] _smooth = new float[Width * Width];
        internal ZDO Record { get; }
        internal int X { get; }
        internal int Z { get; }
        internal byte[] Original { get; }
        internal Vector3 Center => new Vector3(X * 64f, 0f, Z * 64f);

        // Validates the compiler format before any terrain change is planned.
        internal TerrainCompilerData(ZDO record, CaptureApi api)
        {
            Record = record;
            api.GetZone(record.GetPosition(), out var x, out var z);
            X = x;
            Z = z;
            if (Vector3.Distance(record.GetPosition(), Center) > 0.1f)
            {
                throw new InvalidDataException("Terrain compiler is not at its zone center.");
            }
            Original = record.GetByteArray(ZDOVars.s_TCData);
            if (Original == null)
            {
                throw new InvalidDataException("Terrain compiler has no height data in zone " + X + ":" + Z);
            }
            var bytes = Utils.Decompress(Original);
            var data = new ZPackage(bytes);
            if (data.ReadInt() != 1 || data.ReadInt() < 0)
            {
                throw new InvalidDataException("Unsupported terrain compiler version or operation count.");
            }
            data.ReadVector3();
            data.ReadSingle();
            if (data.ReadInt() != Width * Width)
            {
                throw new InvalidDataException("Unsupported terrain compiler vertex count.");
            }
            _header = Slice(bytes, 0, data.GetPos());
            ReadHeights(data);
            _tail = Slice(bytes, data.GetPos(), bytes.Length - data.GetPos());
        }

        // Reads the 65-by-65 height edits in the game's stored vertex order.
        private void ReadHeights(ZPackage data)
        {
            for (var index = 0; index < _modified.Length; index++)
            {
                _modified[index] = data.ReadBool();
                if (_modified[index])
                {
                    _level[index] = data.ReadSingle();
                    _smooth[index] = data.ReadSingle();
                }
            }
        }

        // Returns height changes along one side of the compiler.
        internal float[] Edge(char side)
        {
            var result = new float[Width];
            for (var position = 0; position < Width; position++)
            {
                var index = Index(side, position, 0);
                result[position] = _level[index] + _smooth[index];
            }
            return result;
        }

        // Tapers every selected edge difference over six inward vertices.
        internal byte[] Repair(IEnumerable<TerrainSeam> seams)
        {
            var adjustments = new float[Width * Width];
            foreach (var seam in seams)
            {
                var current = Edge(seam.Side);
                foreach (var point in seam.ExpandedPoints())
                {
                    var difference = seam.Target[point] - current[point];
                    for (var inward = 0; inward < 6; inward++)
                    {
                        adjustments[Index(seam.Side, point, inward)] += difference * (1f - inward / 6f);
                    }
                }
            }
            return Encode(adjustments);
        }

        // Writes new height values while retaining the original compiler header and paint bytes.
        private byte[] Encode(float[] adjustments)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(_header);
                for (var index = 0; index < _modified.Length; index++)
                {
                    var active = _modified[index] || Math.Abs(adjustments[index]) > 0.00001f;
                    writer.Write(active);
                    if (active)
                    {
                        writer.Write(_level[index] + adjustments[index]);
                        writer.Write(_smooth[index]);
                    }
                }
                writer.Write(_tail);
                return Utils.Compress(stream.ToArray());
            }
        }

        // Maps a side and inward distance to the game's row-major height array.
        private static int Index(char side, int position, int inward)
        {
            switch (side)
            {
                case 'N': return (64 - inward) * Width + position;
                case 'S': return inward * Width + position;
                case 'E': return position * Width + 64 - inward;
                case 'W': return position * Width + inward;
                default: throw new ArgumentOutOfRangeException(nameof(side));
            }
        }

        // Copies a byte range without changing the source package.
        private static byte[] Slice(byte[] source, int start, int length)
        {
            var result = new byte[length];
            Buffer.BlockCopy(source, start, result, 0, length);
            return result;
        }
    }
}
