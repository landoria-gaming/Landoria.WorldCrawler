using System;
using System.Collections.Generic;
using System.Linq;

namespace Landoria.WorldCrawler.Commands
{
    // Describes one verified difference between neighboring terrain compiler edges.
    internal sealed class TerrainSeam
    {
        private readonly HashSet<int> _points;
        internal TerrainCompilerData Compiler { get; }
        internal char Side { get; }
        internal float[] Target { get; }
        internal int Count => _points.Count;
        internal float Maximum { get; }

        // Keeps the border target and vertices that exceed the repair threshold.
        internal TerrainSeam(TerrainCompilerData compiler, char side, float[] current, float[] other,
            bool neighborRestored, float threshold)
        {
            Compiler = compiler;
            Side = side;
            Target = new float[65];
            _points = new HashSet<int>();
            for (var point = 0; point < 65; point++)
            {
                var difference = Math.Abs(current[point] - other[point]);
                Target[point] = neighborRestored ? (current[point] + other[point]) / 2f : other[point];
                if (difference >= threshold)
                {
                    _points.Add(point);
                    Maximum = Math.Max(Maximum, difference);
                }
            }
        }

        // Includes two neighboring vertices so the repaired edge has no lateral step.
        internal IEnumerable<int> ExpandedPoints()
        {
            var expanded = new HashSet<int>();
            foreach (var point in _points)
            {
                for (var nearby = Math.Max(0, point - 2); nearby <= Math.Min(64, point + 2); nearby++)
                {
                    expanded.Add(nearby);
                }
            }
            return expanded.OrderBy(point => point);
        }
    }
}
