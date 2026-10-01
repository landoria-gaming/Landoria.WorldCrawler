using System;
using System.IO;
using System.Linq;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Restoration
{
    // Prepares only a fresh local world using the current engine's chunked-save metadata.
    internal static class WorldCreation
    {
        // Creates no replacement or automatic rename when an existing save collides.
        public static string Create(ExportArchive archive)
        {
            LatestWorldApi.RequireCurrent();
            if (Player.m_localPlayer != null || FejdStartup.instance == null)
            {
                throw new InvalidOperationException("Prepare the world from the main menu, not a running world.");
            }
            var identity = archive.Manifest.World;
            NativeWorldMetadata.ValidateName(identity.Name);
            var worlds = LatestWorldApi.Worlds();
            if (worlds.Any(w => w.m_dataError != World.SaveDataError.None))
            {
                throw new InvalidOperationException("An unreadable world prevents reliable UID collision checks.");
            }
            var existing = worlds.Where(w => w.m_uid == identity.Uid || w.m_fileSource == LatestWorldApi.LocalSource &&
                string.Equals(w.m_name, identity.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (existing.Count == 1)
            {
                return ReusePrepared(existing[0], archive);
            }
            if (existing.Count > 1)
            {
                throw new InvalidOperationException("Multiple worlds match this name or UID. Select an unambiguous local target.");
            }
            return CreateFresh(archive);
        }

        // Writes a new native world and its marker only after collision checks succeed.
        private static string CreateFresh(ExportArchive archive)
        {
            var identity = archive.Manifest.World;
            var world = new World(identity.Name, identity.SeedText)
            {
                m_uid = identity.Uid,
                m_seed = identity.Seed,
                m_worldGenVersion = identity.GenerationVersion,
                m_fileSource = LatestWorldApi.LocalSource,
                m_needsDB = false
            };
            var directory = LatestWorldApi.DirectoryFor(world);
            if (Directory.Exists(directory) || File.Exists(directory) ||
                File.Exists(Path.Combine(LatestWorldApi.SaveRoot(), identity.Name + ".fwl")) ||
                File.Exists(Path.Combine(LatestWorldApi.SaveRoot(), identity.Name + ".db")))
            {
                throw new IOException("A target path already exists. Nothing was overwritten.");
            }
            Directory.CreateDirectory(directory);
            NativeWorldMetadata.Verify(LatestWorldApi.SaveNew(world), identity);
            var marker = new PreparedWorld
            {
                World = identity.Copy(),
                ExportFingerprint = archive.Fingerprint,
                Token = Guid.NewGuid().ToString("N"),
                CreatedUtc = DateTime.UtcNow.ToString("o"),
                SourceCharacter = archive.Manifest.CharacterId,
                SourceSeries = archive.SeriesIdentity,
                InitialZones = archive.ZoneSignatures()
            };
            AtomicJson.Write(Path.Combine(directory, PreparedWorld.FileName), marker,
                value => value.Validate(identity, archive.Fingerprint));
            LatestWorldApi.RefreshMenu();
            return directory;
        }

        // Repeating F9 reuses only a verified local target prepared from the same export series.
        private static string ReusePrepared(World world, ExportArchive archive)
        {
            var expected = archive.Manifest.World;
            if (world.m_fileSource != LatestWorldApi.LocalSource || world.m_name != expected.Name ||
                world.m_uid != expected.Uid || world.m_seed != expected.Seed || world.m_seedName != expected.SeedText ||
                world.m_worldGenVersion != expected.GenerationVersion)
            {
                throw new InvalidOperationException("An unrelated or cloud world has this name or UID. Nothing was overwritten.");
            }
            var directory = LatestWorldApi.DirectoryFor(world);
            var path = Path.Combine(directory, PreparedWorld.FileName);
            if (!File.Exists(path))
            {
                throw new InvalidOperationException("An existing world has no World Crawler preparation marker. Nothing was overwritten.");
            }
            var marker = AtomicJson.Read<PreparedWorld>(path);
            marker.ValidateArchive(archive);
            LatestWorldApi.RefreshMenu();
            return directory;
        }

    }
}
