using System.Reflection.PortableExecutable;
using System.Text;
using Mono.Cecil;

namespace AssemblySizeAnalyzer;

public sealed class AssemblyAnalyzer : IDisposable
{
    private readonly AssemblyDefinition _assembly;

    public string FullName => _assembly.FullName;

    public long FileSize { get; }

    public int TotalMetadataSize { get; }

    private AssemblyAnalyzer(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var peReader = new PEReader(stream))
        {
            TotalMetadataSize = peReader.PEHeaders.MetadataSize;
        }

        _assembly = AssemblyDefinition.ReadAssembly(path);
        FileSize = new FileInfo(path).Length;
    }

    public static AssemblyAnalyzer Load(string path)
    {
        return new AssemblyAnalyzer(path);
    }

    public List<TypeSize> AnalyzeTypes(string? @namespace)
    {
        // Always compute per-type metadata for ALL types first,
        // so unaccounted metadata is distributed fairly across the full assembly
        var allTypes = _assembly.MainModule.Types
                                .Select(t => new TypeSize(t.FullName, ComputeIlSize(t), ComputeMetadataSize(t)))
                                .ToList();

        // Calculate unaccounted metadata (assembly-level overhead)
        var computedMetadata = allTypes.Sum(t => t.OverheadSize);
        var unaccountedMetadata = TotalMetadataSize - computedMetadata;

        // If we have unaccounted metadata, distribute it proportionally across ALL types
        if (unaccountedMetadata > 0 && allTypes.Count > 0)
        {
            var totalComputedSize = allTypes.Sum(t => t.TotalSize);

            if (totalComputedSize > 0)
            {
                allTypes = allTypes.Select(t =>
                {
                    // Distribute unaccounted metadata proportionally to each type's size
                    var proportion = (double)t.TotalSize / totalComputedSize;
                    var additionalMetadata = (long)(unaccountedMetadata * proportion);
                    return new TypeSize(t.FullName, t.IlSize, t.OverheadSize + additionalMetadata);
                }).ToList();
            }
        }

        // Filter after distribution so each type has its fair share of metadata
        if (@namespace != null)
        {
            return allTypes.Where(t => t.FullName.StartsWith(@namespace)).ToList();
        }

        return allTypes;
    }

    public List<ResourceSize> AnalyzeResources()
    {
        return _assembly.MainModule.Resources
                        .OfType<EmbeddedResource>()
                        .Select(r => new ResourceSize(r.Name, r.GetResourceData().Length))
                        .ToList();
    }

    private static long ComputeIlSize(TypeDefinition type)
    {
        // IL Code Size (method bodies)
        if (type.HasMethods)
        {
            return type.Methods
                       .Where(m => m.HasBody)
                       .Sum(m => m.Body.CodeSize);
        }

        return 0;
    }

    private long ComputeMetadataSize(TypeDefinition type)
    {
        long total = 0;

        // Metadata Size (approximate: method, field, and property counts)
        total += type.Methods.Count * 16;    // Approximate size per method entry
        total += type.Fields.Count * 12;     // Approximate size per field entry
        total += type.Properties.Count * 10; // Approximate size per property
        total += type.Events.Count * 10;     // Approximate size per event

        // String Metadata (names of types, methods, fields, etc.)
        var utf8 = Encoding.UTF8;
        total += utf8.GetByteCount(type.FullName);
        total += type.Methods.Sum(m => utf8.GetByteCount(m.Name));
        total += type.Methods.Sum(m => m.Parameters.Sum(p => utf8.GetByteCount(p.Name)));
        total += type.Fields.Sum(f => utf8.GetByteCount(f.Name));
        total += type.Properties.Sum(p => utf8.GetByteCount(p.Name));
        total += type.Events.Sum(e => utf8.GetByteCount(e.Name));

        // Custom attributes on type and members
        total += type.CustomAttributes.Count * 20;
        total += type.Methods.Sum(m => m.CustomAttributes.Count * 20);
        total += type.Fields.Sum(f => f.CustomAttributes.Count * 20);
        total += type.Properties.Sum(p => p.CustomAttributes.Count * 20);
        total += type.Events.Sum(e => e.CustomAttributes.Count * 20);

        // Method signatures and parameter metadata
        total += type.Methods.Sum(m => m.Parameters.Count * 8);

        // Generic parameters
        if (type.HasGenericParameters)
        {
            total += type.GenericParameters.Count * 20;
        }

        total += type.Methods.Where(m => m.HasGenericParameters).Sum(m => m.GenericParameters.Count * 20);

        // Interfaces
        if (type.HasInterfaces)
        {
            total += type.Interfaces.Count * 8;
        }

        // Static Fields (estimated based on type)
        total += type.Fields.Where(f => f.IsStatic).Sum(f => GetTypeSize(f.FieldType));

        // String Constants
        total += type.Fields.Where(f => f.HasConstant && f.Constant is string).Sum(f => utf8.GetByteCount((string)f.Constant));

        // Nested Types (recursive)
        total += type.NestedTypes.Sum(ComputeMetadataSize);

        return total;
    }

    // Estimate field size based on type
    private static long GetTypeSize(TypeReference type) => type.FullName switch
    {
        "System.Boolean" => sizeof(bool),
        "System.Byte" => sizeof(byte),
        "System.SByte" => sizeof(sbyte),
        "System.Char" => sizeof(char),
        "System.Int16" => sizeof(short),
        "System.UInt16" => sizeof(ushort),
        "System.Int32" => sizeof(int),
        "System.UInt32" => sizeof(uint),
        "System.Int64" => sizeof(long),
        "System.UInt64" => sizeof(ulong),
        "System.Single" => sizeof(float),
        "System.Double" => sizeof(double),
        "System.Decimal" => sizeof(decimal),
        "System.DateTime" => 8,
        "System.Guid" => 16,
        _ => 8 // Approximate reference type size
    };

    public void Dispose()
    {
        _assembly?.Dispose();
    }
}
