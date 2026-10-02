using System;

namespace Landoria.WorldCrawler.Restoration
{
    // Shares F10's source protection and save boundary with explicit local scenery deletions.
    internal sealed partial class RestorationController
    {
        internal Func<bool> SceneryDeletionsPending { get; set; }

        // Refuses destructive commands until the active session's complete export has been validated.
        internal CleanupSourceIndex ScenerySource()
        {
            if (Busy && (_session?.Archive == null || _session.Journal == null))
            {
                throw new InvalidOperationException("F10 is still preparing or closing. Wait before using indestructible.");
            }
            if (_session == null)
            {
                return null;
            }
            _session.Check();
            return _session.Archive.Cleanup;
        }

        // Includes concurrent explicit scenery changes in F10's next regular native save.
        internal void SceneryChanged()
        {
            if (Active)
            {
                _dirty = true;
            }
        }
    }
}
