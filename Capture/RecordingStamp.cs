namespace Landoria.WorldCrawler.Capture
{
    // Retains only small identity metadata after the large raw payload has been flushed.
    internal sealed class RecordingStamp
    {
        internal int X, Z, Prefab;
        internal uint Revision;
        internal string Fingerprint;
        internal bool HadInstance;
        internal UnityEngine.Vector3 Position;
        internal float[] LocalScale;
    }
}
