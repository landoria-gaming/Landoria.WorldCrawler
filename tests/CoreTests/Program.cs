using System;

namespace Landoria.WorldCrawler.Tests
{
    // Runs the focused data-integrity checks as a package-free console program.
    internal static class Program
    {
        // Returns a failing exit code when any important export invariant regresses.
        private static int Main()
        {
            try
            {
                SupportedGameVersionsTests.Run();
                CaptureStabilityTests.Run();
                InventoryTests.Run();
                LandmarkInventoryTests.Run();
                LandmarkStorageTests.Run();
                SelectionRecoveryTests.Run();
                PortalRouteTests.Run();
                ExportMapOverlayTests.Run();
                StorageTests.Run();
                CapturePayloadTests.Run();
                PayloadStorageTests.Run();
                RestorationTests.Run();
                Console.WriteLine("World Crawler core checks passed: landmark geometry and storage, export integrity, maps, recovery, large zones, restore archive, deduplication, native metadata, backups and restore journal.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }
    }
}
