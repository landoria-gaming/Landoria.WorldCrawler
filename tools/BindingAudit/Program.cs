using Mono.Cecil;

namespace WorldCrawler.BindingAudit;

// Checks one compiled DLL against a selected game's metadata without loading Unity.
internal static class Program
{
    // Resolves direct references and private adapters against the supplied game version.
    private static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: BindingAudit <mod.dll> <game-managed-directory> <BepInEx-core-directory>");
            return 2;
        }
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetFullPath(args[1]));
        resolver.AddSearchDirectory(Path.GetFullPath(args[2]));
        var options = new ReaderParameters { AssemblyResolver = resolver };
        using var assembly = AssemblyDefinition.ReadAssembly(args[0], options);
        using var game = AssemblyDefinition.ReadAssembly(Path.Combine(args[1], "assembly_valheim.dll"), options);
        var errors = new List<string>();
        var count = DirectBindings.Check(assembly, errors);
        PrivateBindings.Check(game.MainModule, errors);
        foreach (var error in errors) { Console.Error.WriteLine(error); }
        Console.WriteLine($"Binding audit: {count} direct members checked; private adapters checked; {errors.Count} errors.");
        return errors.Count == 0 ? 0 : 1;
    }
}
