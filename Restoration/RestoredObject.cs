namespace Landoria.WorldCrawler.Restoration
{
    // Caches a native runtime identity without writing an imported-object ledger.
    internal sealed class RestoredObject
    {
        public string Source;
        public string TargetUser;
        public uint TargetId;
        public int Prefab;
    }
}
