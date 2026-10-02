using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Restoration
{
    // Clears only the requested character cheat marker after preserving a native character backup.
    internal sealed class CharacterCheatMarker
    {
        private readonly PlayerProfile _profile;
        private readonly Player _player;
        private readonly FieldInfo _used;
        private readonly Action<string> _log;
        private readonly string _directory;

        // Binds backup and reset to the exact local character used for this restoration session.
        internal CharacterCheatMarker(string directory, Action<string> log)
        {
            _profile = Game.instance.GetPlayerProfile();
            _player = Player.m_localPlayer;
            _directory = directory;
            _log = log;
            _used = AccessTools.Field(typeof(PlayerProfile), "m_usedCheats");
            if (_used == null || _used.FieldType != typeof(bool))
            {
                throw new MissingFieldException("PlayerProfile.m_usedCheats");
            }
        }

        // Backs up the character before importing and resets only its persisted cheat-history flag.
        internal void Begin()
        {
            Backup();
            Clear();
            _log("Character marker checked. World modifiers, cheated inventory items and Game.isModded can still block achievements.");
        }

        // Makes a native save and a verified independent backup without changing the storage backend.
        private void Backup()
        {
            Save();
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, "character-" + _profile.GetPlayerID() + "-" +
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".fch");
            var reader = new FileReader(_profile.GetPath(), _profile.m_fileSource);
            try
            {
                using (var stream = new MemoryStream())
                {
                    reader.m_binary.BaseStream.CopyTo(stream);
                    var bytes = stream.ToArray();
                    using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        output.Write(bytes, 0, bytes.Length);
                        output.Flush(true);
                    }
                    if (bytes.Length == 0 || StoreValidation.Hash(bytes) != StoreValidation.Hash(File.ReadAllBytes(path)))
                    {
                        throw new IOException("Character backup verification failed.");
                    }
                }
            }
            finally
            {
                reader.Dispose();
            }
            _log("Verified character backup: " + path);
        }

        // Resets the exact flag without bypassing eligibility or granting achievements.
        internal void Clear()
        {
            if (Game.instance == null || Player.m_localPlayer != _player ||
                Game.instance.GetPlayerProfile() != _profile || !(bool)_used.GetValue(_profile))
            {
                return;
            }
            _used.SetValue(_profile, false);
            try
            {
                Save();
                var verify = new PlayerProfile(_profile.GetFilename(), _profile.m_fileSource);
                if (!verify.Load() || (bool)_used.GetValue(verify))
                {
                    throw new IOException("The character cheat marker was not cleared in its saved profile.");
                }
                _log("Character m_usedCheats cleared and verified. No achievement unlock was requested.");
            }
            catch
            {
                _used.SetValue(_profile, true);
                throw;
            }
        }

        // Uses native serialization for the current inventory and map before saving the profile.
        private void Save()
        {
            _profile.SavePlayerData(_player);
            Minimap.instance.SaveMapData();
            if (!_profile.Save())
            {
                throw new IOException("The native character save failed; restoration cannot safely continue.");
            }
        }
    }
}
