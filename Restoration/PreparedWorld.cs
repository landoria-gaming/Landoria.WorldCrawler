using System;
using System.IO;
using System.Runtime.Serialization;
using System.Collections.Generic;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Restoration
{
    // Binds restoration to a deliberately created local target rather than the source server.
    [DataContract]
    internal sealed class PreparedWorld
    {
        internal const string FileName = "worldcrawler-target.json";
        [DataMember] public int FormatVersion = 1;
        [DataMember] public WorldIdentity World;
        [DataMember] public string ExportFingerprint;
        [DataMember] public string Token;
        [DataMember] public string CreatedUtc;
        [DataMember] public string SourceCharacter;
        [DataMember(EmitDefaultValue = false)] public string SourceSeries;
        [DataMember(EmitDefaultValue = false)] public Dictionary<string, string> InitialZones;

        // Rejects missing or unrelated target markers before modifying a local world.
        public void Validate(WorldIdentity world, string fingerprint)
        {
            if (FormatVersion != 1 || World == null || !World.Matches(world) || World.Name != world.Name ||
                ExportFingerprint != fingerprint || !Guid.TryParseExact(Token, "N", out _) ||
                string.IsNullOrWhiteSpace(SourceCharacter))
            {
                throw new InvalidDataException("This local world was not prepared for this exact export.");
            }
            if (SourceSeries != null && (SourceSeries.Length != 64 || InitialZones == null || InitialZones.Count == 0))
            {
                throw new InvalidDataException("The prepared export series is incomplete.");
            }
        }

        // Accepts additions and verified recaptures for new targets while preserving exact binding for older markers.
        public void ValidateArchive(ExportArchive archive)
        {
            Validate(archive.Manifest.World, ExportFingerprint);
            if (SourceCharacter != archive.Manifest.CharacterId)
            {
                throw new InvalidDataException("The export belongs to a different source character.");
            }
            if (SourceSeries == null)
            {
                Validate(archive.Manifest.World, archive.Fingerprint);
                return;
            }
            if (SourceSeries != archive.SeriesIdentity)
            {
                throw new InvalidDataException("Select the original export series for this prepared world.");
            }
            archive.RequirePresent(InitialZones);
        }
    }
}
