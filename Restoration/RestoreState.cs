using System.Collections.Generic;
using System.Runtime.Serialization;
using Landoria.WorldCrawler.Storage;

namespace Landoria.WorldCrawler.Restoration
{
    // Keeps import progress independent of source export and minimap progress.
    [DataContract]
    internal sealed class RestoreState
    {
        [DataMember] public int FormatVersion = 1;
        [DataMember] public WorldIdentity World;
        [DataMember] public string Token;
        [DataMember] public string Fingerprint;
        [DataMember] public string Character;
        [DataMember] public string UpdatedUtc;
        [DataMember] public string Status = "pending";
        [DataMember] public string Error;
        [DataMember] public string BackupDirectory;
        [DataMember] public bool ReturnPending;
        [DataMember] public float[] ReturnPosition;
        [DataMember] public float[] ReturnRotation;
        [DataMember] public List<string> Completed = new List<string>();
        [DataMember] public List<RestoredObject> Objects = new List<RestoredObject>();
        [DataMember] public List<string> Warnings = new List<string>();
        [DataMember(EmitDefaultValue = false)] public Dictionary<string, string> AcceptedZones;
    }
}
