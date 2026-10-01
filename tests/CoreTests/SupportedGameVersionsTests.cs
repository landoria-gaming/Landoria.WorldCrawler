using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Tests
{
    // Guards the supported family boundary shared by game startup and imported archives.
    internal static class SupportedGameVersionsTests
    {
        // Accepts numeric current patches while rejecting neighboring branches and ambiguous strings.
        internal static void Run()
        {
            foreach (var value in new[] { "1.0.0", "1.0.12", "1.0.16", "1.0.17", "1.0.123" })
            {
                TestSupport.Check(SupportedGameVersions.IsCurrent(value), "Current patch rejected: " + value);
                TestSupport.Check(SupportedGameVersions.CanExport(value), "Current export rejected: " + value);
            }
            TestSupport.Check(SupportedGameVersions.CanExport("0.221.12") &&
                !SupportedGameVersions.IsCurrent("0.221.12"), "Legacy export/restoration boundary changed.");
            foreach (var value in new[] { null, "", "1.0", "1.0.x", "1.0.-1", "1.0.16-beta", "1.0.16.1",
                "1.1.0", "2.0.0", "0.221.13", " 1.0.16", "1.0.16 " })
            { TestSupport.Check(!SupportedGameVersions.CanExport(value), "Unsupported version accepted: " + value); }
        }
    }
}
