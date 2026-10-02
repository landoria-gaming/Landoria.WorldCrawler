using System;
using System.IO;
using System.Linq;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Restoration
{
    // Prepares only a fresh local world using the current engine's chunked-save metadata.
    internal static class WorldCreation
    {
        // Creates a fresh world only when neither its UID nor its local name already exists.
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
            if (worlds.Any(world => world.m_uid == identity.Uid))
            {
                throw new InvalidOperationException("A world with UID " + identity.Uid +
                    " already exists. Delete it in Valheim, then press F9 again. Nothing was changed.");
            }
            if (worlds.Any(world => world.m_fileSource == LatestWorldApi.LocalSource &&
                string.Equals(world.m_name, identity.Name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("A local world named '" + identity.Name +
                    "' already exists. Delete or rename it, then press F9 again. Nothing was changed.");
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

    }
}
