using Mono.Cecil;

namespace WorldCrawler.BindingAudit;

// Checks runtime reflection contracts that Harmony annotations cannot describe.
internal static class PrivateBindings
{
    // Validates the known legacy and current shapes rather than silently accepting drift.
    internal static void Check(ModuleDefinition game, List<string> errors)
    {
        Field(game, "Minimap", "m_explored", errors, "System.Boolean[]", "System.Collections.BitArray");
        Field(game, "Minimap", "m_exploredOthers", errors, "System.Boolean[]", "System.Collections.BitArray");
        Field(game, "Minimap", "m_hasGenerated", errors, "System.Boolean");
        Field(game, "LocationProxy", "m_instance", errors, "UnityEngine.GameObject");
        Field(game, "ZoneSystem/ZoneData", "m_root", errors, "UnityEngine.GameObject");
        Field(game, "ZoneSystem", "m_zones", errors);
        Field(game, "Terminal", "m_cheat", errors, "System.Boolean");
        Field(game, "Game", "isModded", errors, "System.Boolean");
        foreach (var name in new[] { "m_maxAirAltitude", "m_lastGroundTouch", "m_fallTimer" })
        {
            Field(game, "Character", name, errors, "System.Single");
        }
        Field(game, "Character", "m_currentVel", errors, "UnityEngine.Vector3");
        Method(game, "Version", "get_CurrentVersion", errors);
        var sprint = Method(game, "Player", "GetRunSpeedFactor", errors);
        if (sprint?.ReturnType.FullName != "System.Single") { errors.Add("Unsupported native sprint factor."); }
        CheckZones(game, errors);
        CheckLandmarks(game, errors);
        CheckNativeTransit(game, errors);
        CheckNotifications(game, errors);
        Method(game, "ZNetScene", "OnZDODestroyed", errors, "ZDO");
        if (game.GetType("Achievements") != null)
        { CheckAchievements(game, errors); CheckRestoration(game, errors); }
    }

    // Audits native cancellation fields used only for a crawler-owned emergency return.
    private static void CheckNativeTransit(ModuleDefinition game, List<string> errors)
    {
        Field(game, "Player", "m_teleportTargetPos", errors, "UnityEngine.Vector3");
        Field(game, "Player", "m_teleporting", errors, "System.Boolean");
        Field(game, "Player", "m_teleportCooldown", errors, "System.Single");
    }

    // Requires the map discovery fields and both supported native portal-cache return types.
    private static void CheckLandmarks(ModuleDefinition game, List<string> errors)
    {
        Field(game, "Minimap", "m_pins", errors, "System.Collections.Generic.List`1<Minimap/PinData>");
        Field(game, "Minimap", "m_locationPins", errors,
            "System.Collections.Generic.Dictionary`2<UnityEngine.Vector3,Minimap/PinData>");
        Method(game, "Minimap", "IsExplored", errors, "UnityEngine.Vector3");
        var portals = Method(game, "ZDOMan", "GetPortals", errors);
        var shape = portals?.ReturnType.FullName;
        if (shape != "System.Collections.Generic.List`1<ZDO>"
            && shape != "System.Collections.Generic.Dictionary`2<ZoneSystem/SectorIndex,System.Collections.Generic.List`1<ZDO>>")
        { errors.Add("Unsupported ZDOMan.GetPortals return type: " + shape); }
    }

    // Audits every current-only native save and generated-location adapter.
    private static void CheckRestoration(ModuleDefinition game, List<string> errors)
    {
        var format = game.GetType("Version")?.Fields.FirstOrDefault(field => field.Name == "c_WorldVersion");
        if (format == null || !format.HasConstant || Convert.ToInt32(format.Constant) != 41)
        { errors.Add("Unsupported current native save format; expected c_WorldVersion = 41."); }
        Method(game, "SaveSystem", "ClearWorldListCache", errors, "System.Boolean");
        Method(game, "SaveSystem", "GetWorldList", errors);
        Method(game, "SaveSystem", "GetWorldsSaveRootPath", errors, "FileHelpers/FileSource");
        Method(game, "SaveSystem", "SetSaveNumber", errors, "System.UInt32");
        Method(game, "SaveSystem", "GetSaveNumber", errors);
        Method(game, "World", "GetSaveDirectory", errors, "FileHelpers/FileSource");
        Method(game, "World", "SaveWorldFWLData", errors, "System.DateTime");
        Method(game, "World", "GetSaveFWLPath", errors);
        Method(game, "ZNet", "Save", errors, "System.Boolean", "System.Boolean", "System.Boolean");
        Method(game, "FejdStartup", "UpdateWorldList", errors, "System.Boolean");
        Method(game, "FileHelpers", "get_LocalStorageSupportedAndAllowed", errors);
        Field(game, "ZoneSystem", "m_locationInstances", errors);
        foreach (var cache in new[] { "m_locationIDCache", "m_locationGroupCache", "m_locationMaxGroupCache" })
        { Field(game, "ZoneSystem", cache, errors); }
        Method(game, "ZoneSystem", "RegisterLocation", errors, "ZoneSystem/ZoneLocation", "UnityEngine.Vector3", "System.Boolean");
    }

    // Requires the notification overload used by the selected runtime adapter.
    private static void CheckNotifications(ModuleDefinition game, List<string> errors)
    {
        var args = new List<string> { "MessageHud/MessageType", "System.String", "System.Int32", "UnityEngine.Sprite" };
        if (game.GetType("Achievements") != null) { args.Add("System.Boolean"); }
        Method(game, "Player", "Message", errors, args.ToArray());
    }

    // Confirms both accepted sector-query signatures and the current distance constructor.
    private static void CheckZones(ModuleDefinition game, List<string> errors)
    {
        var getZone = Method(game, "ZoneSystem", "GetZone", errors, "UnityEngine.Vector3");
        var zoneType = getZone?.ReturnType.FullName;
        var list = "System.Collections.Generic.List`1<ZDO>";
        if (zoneType == "Vector2i")
        {
            Method(game, "ZDOMan", "FindSectorObjects", errors, zoneType, "System.Int32", "System.Int32", list, list);
        }
        else if (zoneType == "Vector2s")
        {
            Method(game, "ZDOMan", "FindSectorObjects", errors, zoneType, "SimulationDistance", list, list);
            Method(game, "SimulationDistance", ".ctor", errors, "System.Int32", "System.Int32", "System.Boolean");
        }
        else { errors.Add("Unsupported ZoneSystem.GetZone return type: " + zoneType); }
    }

    // Checks new-only achievement observations without forcing legacy to reference those types.
    private static void CheckAchievements(ModuleDefinition game, List<string> errors)
    {
        Field(game, "PlayerProfile", "m_usedCheats", errors, "System.Boolean");
        Method(game, "PlayerProfile", "get_s_bypassCheatChecks", errors);
        Method(game, "Achievements", "CanGetAchievements", errors, "System.Boolean");
        Method(game, "Achievements", "IsWorldCheated", errors);
        Method(game, "Inventory", "AnyCheatedItem", errors);
    }

    // Requires an exact private or public field shape on the selected game version.
    private static void Field(ModuleDefinition game, string type, string name, List<string> errors, params string[] types)
    {
        var field = game.GetType(type)?.Fields.FirstOrDefault(f => f.Name == name);
        if (field == null || types.Length > 0 && !types.Contains(field.FieldType.FullName))
        {
            errors.Add("Missing or changed reflected field: " + type + "." + name);
        }
    }

    // Requires an unambiguous full argument signature for a reflected method.
    private static MethodDefinition Method(ModuleDefinition game, string type, string name,
        List<string> errors, params string[] parameters)
    {
        var resolved = game.GetType(type) ?? game.GetTypeReferences().FirstOrDefault(t => t.FullName == type)?.Resolve();
        var candidates = resolved?.Methods.Where(m => m.Name == name &&
            m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters)).ToList();
        if (candidates == null || candidates.Count != 1)
        {
            errors.Add("Missing or ambiguous reflected method: " + type + "." + name);
            return null;
        }
        return candidates[0];
    }
}
