using System;
using System.Runtime.Serialization;

namespace Landoria.WorldCrawler.Storage
{
    // Stores the immutable identifiers of a source world.
    [DataContract]
    public sealed class WorldIdentity
    {
        [DataMember(Order = 0)]
        public string Name
        {
            get; set;
        }
        [DataMember(Order = 1)]
        public long Uid
        {
            get; set;
        }
        [DataMember(Order = 2)]
        public string SeedText
        {
            get; set;
        }
        [DataMember(Order = 3)]
        public int Seed
        {
            get; set;
        }
        [DataMember(Order = 4)]
        public int GenerationVersion
        {
            get; set;
        }

        // Checks identity without treating a world rename as another world.
        public bool Matches(WorldIdentity other)
        {
            return other != null && Uid == other.Uid && Seed == other.Seed &&
                GenerationVersion == other.GenerationVersion &&
                string.Equals(SeedText, other.SeedText, StringComparison.Ordinal);
        }

        // Copies identity so callers cannot mutate stored validation state.
        public WorldIdentity Copy()
        {
            return new WorldIdentity
            {
                Name = Name,
                Uid = Uid,
                SeedText = SeedText,
                Seed = Seed,
                GenerationVersion = GenerationVersion
            };
        }
    }
}
