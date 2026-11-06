using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Salochi.Launcher.Core.Mods;

/// <summary>A DLL's assembly name, version and [AssemblyMetadata] pairs, read from its metadata without loading it.</summary>
public sealed record AssemblyInfo(string Name, Version Version, IReadOnlyDictionary<string, string> Metadata);

public static class AssemblyInfoReader
{
    /// <summary>The key our builds carry ([assembly: AssemblyMetadata("Salochi.Family", "Salochi")]), set in
    /// oth-cars/src/Directory.Build.props. It tells our forked Core apart from the original OverTheHillPlus one, which
    /// has the same file name and plugin id.</summary>
    public const string FamilyKey = "Salochi.Family";

    /// <summary>The assembly's info, or null when the file isn't a .NET assembly (a native DLL) or can't be read.</summary>
    public static AssemblyInfo? Read(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var pe = new PEReader(fs);
            if (!pe.HasMetadata) return null;
            var md = pe.GetMetadataReader();
            if (!md.IsAssembly) return null;
            var asm = md.GetAssemblyDefinition();
            var meta = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var h in asm.GetCustomAttributes())
            {
                var attr = md.GetCustomAttribute(h);
                if (AttributeTypeName(md, attr) != "System.Reflection.AssemblyMetadataAttribute") continue;
                var blob = md.GetBlobReader(attr.Value);
                if (blob.ReadUInt16() != 1) continue; // the custom attribute prolog
                string? key = blob.ReadSerializedString();
                string? value = blob.ReadSerializedString();
                if (key != null) meta[key] = value ?? "";
            }
            return new AssemblyInfo(md.GetString(asm.Name), asm.Version, meta);
        }
        catch (BadImageFormatException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static string? AttributeTypeName(MetadataReader md, CustomAttribute attr)
    {
        EntityHandle type;
        if (attr.Constructor.Kind == HandleKind.MemberReference)
            type = md.GetMemberReference((MemberReferenceHandle)attr.Constructor).Parent;
        else if (attr.Constructor.Kind == HandleKind.MethodDefinition)
            type = md.GetMethodDefinition((MethodDefinitionHandle)attr.Constructor).GetDeclaringType();
        else return null;

        return type.Kind switch
        {
            HandleKind.TypeReference => Full(md.GetTypeReference((TypeReferenceHandle)type).Namespace, md.GetTypeReference((TypeReferenceHandle)type).Name, md),
            HandleKind.TypeDefinition => Full(md.GetTypeDefinition((TypeDefinitionHandle)type).Namespace, md.GetTypeDefinition((TypeDefinitionHandle)type).Name, md),
            _ => null,
        };
    }

    private static string Full(StringHandle ns, StringHandle name, MetadataReader md) => md.GetString(ns) + "." + md.GetString(name);
}
