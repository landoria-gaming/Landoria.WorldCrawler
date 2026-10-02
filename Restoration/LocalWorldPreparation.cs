using System;
using System.Linq;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Restoration
{
    // Attaches a user-created local world to an export by changing only its UID.
    internal static class LocalWorldPreparation
    {
        // Returns an OK-only instruction or result; no world is created by the mod.
        internal static string Prepare(ExportArchive archive)
        {
            LatestWorldApi.RequireCurrent();
            if (Player.m_localPlayer != null || FejdStartup.instance == null)
            {
                throw new InvalidOperationException("Prepare the local world from the main menu.");
            }
            var source = archive.Manifest.World;
            NativeWorldMetadata.ValidateName(source.Name);
            var worlds = LatestWorldApi.LocalWorlds();
            if (worlds.Any(world => world.m_dataError != World.SaveDataError.None))
            {
                throw new InvalidOperationException("An unreadable local world prevents reliable UID collision checks.");
            }
            var matches = worlds.Where(world => SameNameAndSeed(world, source)).ToArray();
            if (matches.Length == 0)
            {
                return "Create a local world in Valheim with exactly these values:\n\nName: " + source.Name +
                    "\nSeed: " + source.SeedText + "\n\nThen return to the main menu and press F9 again. No world was changed.";
            }
            if (matches.Length != 1)
            {
                throw new InvalidOperationException("Multiple local worlds have this exact name and seed. Keep one, then press F9.");
            }
            var target = matches[0];
            if (worlds.Any(world => !ReferenceEquals(world, target) && world.m_uid == source.Uid))
            {
                throw new InvalidOperationException("Another LOCAL world already has UID " + source.Uid +
                    ". Remove that local duplicate in Valheim, then press F9. Nothing was changed.");
            }
            return PrepareTarget(target, source, archive.DirectoryPath);
        }

        // Requires the exact displayed name, seed text and matching numeric seed.
        private static bool SameNameAndSeed(World world, WorldIdentity source)
        {
            return string.Equals(world.m_name, source.Name, StringComparison.Ordinal) &&
                string.Equals(world.m_seedName, source.SeedText, StringComparison.Ordinal) && world.m_seed == source.Seed;
        }

        // Preserves native metadata, terrain generation and every existing database chunk.
        private static string PrepareTarget(World target, WorldIdentity source, string exportDirectory)
        {
            var expected = source.Copy();
            expected.Uid = target.m_uid;
            expected.GenerationVersion = target.m_worldGenVersion;
            var path = LatestWorldApi.MetadataPath(target);
            NativeWorldMetadata.Verify(path, expected);
            if (target.m_uid != source.Uid)
            {
                RestoreJournal.Reset(exportDirectory, source.Uid);
                NativeWorldMetadata.ReplaceUid(path, expected, source.Uid);
                target.m_uid = source.Uid;
            }
            LatestWorldApi.Worlds();
            LatestWorldApi.RefreshMenu();
            return "Local world '" + source.Name + "' is ready.\n\nSeed: " + source.SeedText +
                "\nUID: " + source.Uid + "\n\nEnter this local world, then press F10 to restore the recorded areas.";
        }
    }
}
