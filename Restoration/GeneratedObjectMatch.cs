using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Landoria.WorldCrawler.Capture;
using UnityEngine;

namespace Landoria.WorldCrawler.Restoration
{
    // Reuses generated copies whose pose matches a source object before applying authoritative data.
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

        // Selects one stable candidate; the source overwrites its state and cleanup handles extras.
        internal static ZDO Choose(CapturedObject source, List<ZDO> candidates, Action<string> warning)
        {
            if (candidates.Count < 2)
            {
                return candidates.FirstOrDefault();
            }
            var ordered = candidates.OrderBy(z => z.m_uid.UserID).ThenBy(z => z.m_uid.ID).ToList();
            var first = ordered[0];
            warning(RestoreWarnings.Describe(source) + "; " + ordered.Count
                + " generated copies at the source pose; reusing target=" + first.m_uid +
                "; unclaimed extra copies will be reconciled against the export.");
            return first;
        }
    }
}
