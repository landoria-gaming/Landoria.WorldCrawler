using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Landoria.WorldCrawler.Restoration.Compatibility;
using Landoria.WorldCrawler.Runtime;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Restoration.Persistence
{
    // Isolates new-format save APIs so the same assembly still loads in the old game.
    internal static class LatestWorldApi
    {
        // Resolves the renamed enum value at runtime because its numeric value changed in Valheim 1.0.
        public static FileHelpers.FileSource LocalSource =>
            (FileHelpers.FileSource)Enum.Parse(typeof(FileHelpers.FileSource), "Local");

        // Refuses UID preparation and restoration on the legacy runtime.
        public static void RequireCurrent()
        {
            if (!SupportedGameVersions.IsCurrent(GameContext.GameVersion))
            {
                throw new NotSupportedException("World preparation and restoration require Valheim 1.0.x.");
            }
            CurrentSaveCompatibility.Validate();
            var property = typeof(FileHelpers).GetProperty("LocalStorageSupportedAndAllowed",
                BindingFlags.Public | BindingFlags.Static);
            if (property == null || !(bool)property.GetValue(null))
            {
                throw new InvalidOperationException("Local saves are unavailable; cloud restoration is not supported.");
            }
        }

        // Reads all game-visible local and cloud worlds for collision detection.
        public static List<World> Worlds()
        {
            Call(typeof(SaveSystem), null, "ClearWorldListCache", new[] { typeof(bool) }, false);
            return (List<World>)Call(typeof(SaveSystem), null, "GetWorldList", Type.EmptyTypes);
        }

        // Resolves the current game's local save root without guessing its directory layout.
        public static string SaveRoot()
        {
            var root = (string)Call(typeof(SaveSystem), null, "GetWorldsSaveRootPath",
                new[] { typeof(FileHelpers.FileSource) }, LocalSource);
            return LocalSavePath.Root(root, Utils.GetSaveDataPath(LocalSource));
        }

        // Resolves the chunked save folder of the selected world.
        public static string DirectoryFor(World world)
        {
            var directory = (string)Call(typeof(World), world, "GetSaveDirectory",
                new[] { typeof(FileHelpers.FileSource) }, LocalSource);
            return LocalSavePath.World(directory, SaveRoot(), world.m_name);
        }

        // Uses the world's loaded generation, never the previous session's global save counter.
        public static string MetadataPath(World world)
        {
            if (world.m_fileSource != LocalSource)
            {
                throw new InvalidOperationException("Only local world metadata can be prepared.");
            }
            var generation = (uint)Call(typeof(World), world, "SaveNumber", Type.EmptyTypes);
            return Path.Combine(DirectoryFor(world), "_main." + generation + ".fwl2");
        }

        // Includes local saves hidden by a cloud save with the same name in Valheim's menu.
        public static World[] LocalWorlds()
        {
            var saves = (SaveWithBackups[])Call(typeof(SaveSystem), null, "GetSavesByType",
                new[] { typeof(SaveDataType) }, SaveDataType.World);
            var worlds = new List<World>();
            foreach (var file in saves.SelectMany(save => save.AllFiles)
                .Where(file => file.m_source == LocalSource &&
                    !(bool)Call(typeof(SaveFile), file, "get_IsBackup", Type.EmptyTypes)))
            {
                var name = (string)Call(typeof(SaveFile), file, "get_Name", Type.EmptyTypes);
                var local = new SaveWithBackups(name, new SaveCollection(SaveDataType.World), null);
                local.AddSaveFile(file.AllPaths, LocalSource);
                var world = World.LoadWorld(local);
                if (world == null)
                {
                    throw new IOException("An unreadable local world prevents UID collision checks: " + name);
                }
                worlds.Add(world);
            }
            return worlds.ToArray();
        }

        // Requests a world-only asynchronous save and returns its previously committed generation.
        public static uint BeginSave()
        {
            if (ZNet.instance.IsSaving())
            {
                throw new InvalidOperationException("Wait for the current world save.");
            }
            var before = SaveNumber();
            Call(typeof(ZNet), ZNet.instance, "Save", new[] { typeof(bool), typeof(bool), typeof(bool) },
                false, false, false);
            return before;
        }

        // Confirms the engine committed a newer complete chunked generation.
        public static bool SaveFinished(uint before, string directory)
        {
            if (ZNet.instance.IsSaving())
            {
                return false;
            }
            var after = SaveNumber();
            var file = Path.Combine(directory, "_main." + after + ".ok");
            if (after <= before || !File.Exists(file))
            {
                throw new IOException("Valheim did not confirm the new world save; restore progress was not committed.");
            }
            using (var reader = new BinaryReader(File.OpenRead(file)))
            {
                if (reader.ReadInt32() != 41)
                {
                    throw new IOException("Invalid native world-save completion marker.");
                }
            }
            return true;
        }

        // Reads only the last committed engine generation when no save is active.
        public static uint SaveNumber()
        {
            return (uint)Call(typeof(SaveSystem), null, "GetSaveNumber", Type.EmptyTypes);
        }

        // Refreshes the menu without selecting or entering the newly prepared world.
        public static void RefreshMenu()
        {
            if (FejdStartup.instance != null)
            {
                Call(typeof(FejdStartup), FejdStartup.instance, "UpdateWorldList", new[] { typeof(bool) }, false);
            }
        }

        // Requires an exact reflected signature instead of choosing a changing overload by name.
        private static object Call(Type type, object instance, string name, Type[] signature, params object[] args)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic |
                (instance == null ? BindingFlags.Static : BindingFlags.Instance), null, signature, null)
                ?? throw new MissingMethodException(type.FullName, name);
            return method.Invoke(instance, args);
        }
    }
}
