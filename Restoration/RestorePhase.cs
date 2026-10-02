namespace Landoria.WorldCrawler.Restoration
{
    // Separates applied world changes from successful native-save checkpoints.
    internal enum RestorePhase
    {
        Idle, Preparing, Preflight, InitialSave, Backup, Reading,
        Restoring, Connecting, RequestSave, Saving, Finalizing, Waiting, Stopped
    }
}
