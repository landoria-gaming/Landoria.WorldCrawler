using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Landoria.WorldCrawler.Capture;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration
{
    // Reuses exact generated copies without guessing between objects with different saved state.
    internal static class GeneratedObjectMatch
    {
        private static readonly MethodInfo WriteRotation = typeof(ZPackage).GetMethod("WriteSmallRotation", new[] { typeof(Vector3) });
        private static readonly MethodInfo ReadRotation = typeof(ZPackage).GetMethod("ReadSmallRotation", Type.EmptyTypes);

        // Requires the current native rotation codec before any restoration mutations begin.
        internal static void Validate()
        {
            if (WriteRotation == null || WriteRotation.ReturnType != typeof(void) ||
                ReadRotation == null || ReadRotation.ReturnType != typeof(Vector3))
            {
                throw new NotSupportedException("The current native saved-rotation codec is unavailable.");
            }
        }

        // Uses Valheim's own half-degree disk encoding rather than widening matching tolerances.
        internal static Quaternion SavedRotation(Quaternion rotation)
        {
            var package = new ZPackage();
            WriteRotation.Invoke(package, new object[] { rotation.eulerAngles });
            package.SetPos(0);
            return Quaternion.Euler((Vector3)ReadRotation.Invoke(package, null));
        }

        // Chooses a stable copy only when all candidates are identical unowned natural scenery.
        internal static ZDO Choose(CapturedObject source, List<ZDO> candidates, Action<string> warning)
        {
            if (candidates.Count < 2)
            {
                return candidates.FirstOrDefault();
            }
            var ordered = candidates.OrderBy(z => z.m_uid.UserID).ThenBy(z => z.m_uid.ID).ToList();
            var first = ordered[0];
            var bytes = Data(first);
            if (!IsScenery(source) || ordered.Any(z => z.GetLong("creator", 0L) != 0L ||
                z.GetConnectionType() != ZDOExtraData.ConnectionType.None ||
                z.GetPosition() != first.GetPosition() || !Data(z).SequenceEqual(bytes)))
            {
                throw new InvalidOperationException("Ambiguous non-equivalent targets; " + RestoreWarnings.Describe(source)
                    + "; candidates=" + string.Join(",", ordered.Select(z => z.m_uid.ToString())));
            }
            warning(RestoreWarnings.Describe(source) + "; " + ordered.Count
                + " identical generated copies; reusing target=" + first.m_uid + "; extra copies left unchanged for review.");
            return first;
        }

        // Excludes player buildings, interactive storage, drops, locations and connected objects.
        private static bool IsScenery(CapturedObject source)
        {
            var prefab = ZNetScene.instance.GetPrefab(source.PrefabHash);
            return source.Creator == "0" && source.ConnectionType == 0 && prefab != null &&
                (prefab.GetComponent<Destructible>() != null || prefab.GetComponent<TreeBase>() != null ||
                prefab.GetComponent<MineRock>() != null || prefab.GetComponent<MineRock5>() != null) &&
                prefab.GetComponent<Piece>() == null && prefab.GetComponent<Container>() == null &&
                prefab.GetComponent<ItemDrop>() == null && prefab.GetComponent<LocationProxy>() == null &&
                !RestoreProtection.Protected(prefab);
        }

        // Compares the full native network payload, including scale, health and custom data.
        private static byte[] Data(ZDO target)
        {
            var package = new ZPackage();
            target.Serialize(package);
            return package.GetArray();
        }
    }
}
