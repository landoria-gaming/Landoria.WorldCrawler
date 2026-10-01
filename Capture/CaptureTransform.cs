using System;
using UnityEngine;

namespace Landoria.WorldCrawler.Capture
{
    // Converts Unity transforms into validated arrays with no Unity serialization dependency.
    internal static class CaptureTransform
    {
        // Records a finite position or scale in x, y, z order.
        internal static float[] Vector(Vector3 value)
        {
            return Validate(new[] { value.x, value.y, value.z });
        }

        // Records a finite quaternion in x, y, z, w order.
        internal static float[] Rotation(Quaternion value)
        {
            return Validate(new[] { value.x, value.y, value.z, value.w });
        }

        // Refuses corrupt transform values before they reach an export file.
        private static float[] Validate(float[] values)
        {
            foreach (var value in values)
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    throw new InvalidOperationException("A source transform contains a non-finite value.");
                }
            }
            return values;
        }

        // Uses the same centered 64-meter zone boundaries as both supported game builds.
        internal static bool InZone(Vector3 position, int x, int z)
        {
            return Math.Floor((position.x + 32.0) / 64.0) == x
                && Math.Floor((position.z + 32.0) / 64.0) == z;
        }
    }
}
