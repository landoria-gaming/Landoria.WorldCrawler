using Mono.Cecil;

namespace WorldCrawler.BindingAudit;

// Resolves every game and Unity member used by the exact distributable assembly.
internal static class DirectBindings
{
    // Reports missing signatures even when a different source build would compile successfully.
    internal static int Check(AssemblyDefinition assembly, List<string> errors)
    {
        var count = 0;
        foreach (var member in assembly.MainModule.GetMemberReferences())
        {
            if (!Relevant(member.DeclaringType)) { continue; }
            count++;
            try
            {
                var resolved = member is MethodReference method ? (IMemberDefinition)method.Resolve()
                    : member is FieldReference field ? field.Resolve() : null;
                if (resolved == null) { errors.Add("Unresolved direct member: " + member.FullName); }
                else if (member is FieldReference source && resolved is FieldDefinition target &&
                    source.FieldType.FullName != target.FieldType.FullName)
                {
                    errors.Add("Changed field type: " + member.FullName);
                }
            }
            catch (Exception error) { errors.Add(member.FullName + ": " + error.Message); }
        }
        return count;
    }

    // Limits validation to the game's API surface and its Unity dependencies.
    private static bool Relevant(TypeReference type)
    {
        while (type is TypeSpecification specification) { type = specification.ElementType; }
        var scope = type.Scope?.Name ?? "";
        return scope.StartsWith("assembly_", StringComparison.Ordinal) ||
            scope.StartsWith("UnityEngine", StringComparison.Ordinal);
    }
}
