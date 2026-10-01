namespace Landoria.WorldCrawler.Restoration
{
    // Separates ordinary physics movement from a misplaced static structure.
    internal static class RestorePositionPolicy
    {
        internal const float FixedTolerance = 0.1f;

        // Uses object behavior, never a rigidbody's temporary ownership or kinematic state.
        internal static bool IsMovable(bool itemDrop, bool placedItem, bool syncPosition)
        {
            return !placedItem && (itemDrop || syncPosition);
        }

        // Rejects invalid coordinates for every object and displacement for fixed objects only.
        internal static bool Accepts(float distance, bool movable)
        {
            return !float.IsNaN(distance) && !float.IsInfinity(distance) && distance >= 0f
                && (movable || distance <= FixedTolerance);
        }
    }
}
