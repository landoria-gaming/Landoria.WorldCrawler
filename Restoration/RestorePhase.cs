namespace Landoria.WorldCrawler.Restoration
{
    // Names the sequential import stages so disk commits cannot be mistaken for engine saves.
    internal enum RestorePhase
    {
        Idle, Preparing, Preflight, InitialSave, Backup, Travelling, Reading,
        Restoring, Connecting, RequestSave, Saving, Finalizing, Returning, Landing, Stopped
    }
}
