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
        return _assembly.MainModule.Types
                        .Where(t => @namespace == null || t.FullName.StartsWith(@namespace))
                        .Select(t => new TypeSize(t.FullName, ComputeIlSize(t), ComputeMetadataSize(t)))
                        .ToList();
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

    private static long ComputeMetadataSize(TypeDefinition type)
    {
        long total = 0;

        // Metadata Size (approximate: method, field, and property counts)
        total += type.Methods.Count * 16;    // Approximate size per method entry
        total += type.Fields.Count * 12;     // Approximate size per field entry
        total += type.Properties.Count * 10; // Approximate size per property

        // String Metadata (names of types, methods, fields, etc.)
        var utf8 = Encoding.UTF8;
        total += utf8.GetByteCount(type.FullName);
        total += type.Methods.Sum(m => utf8.GetByteCount(m.Name));
        total += type.Methods.Sum(m => m.Parameters.Sum(p => utf8.GetByteCount(p.Name)));
        total += type.Fields.Sum(f => utf8.GetByteCount(f.Name));
        total += type.Properties.Sum(p => utf8.GetByteCount(p.Name));
        total += type.Events.Sum(e => utf8.GetByteCount(e.Name));

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
