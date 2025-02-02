using System.Reflection.PortableExecutable;
using System.Text;
using Mono.Cecil;

namespace AssemblySizeAnalyzer;

public sealed class AssemblyAnalyzer : IDisposable
{
    private readonly AssemblyDefinition _assembly;

    public string Path { get; }

    public string FullName { get; }

    public string ModuleName { get; }

    public long FileSize { get; }

    public int TotalMetadataSize { get; }

    private AssemblyAnalyzer(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var peReader = new PEReader(stream))
        {
            TotalMetadataSize = peReader.GetMetadata().Length;
        }

        _assembly = AssemblyDefinition.ReadAssembly(path);

        Path = path;
        FullName = _assembly.FullName;
        ModuleName = _assembly.MainModule.Name;
        FileSize = new FileInfo(Path).Length;
    }

    public static AssemblyAnalyzer Load(string path)
    {
        return new AssemblyAnalyzer(path);
    }

    public List<TypeSize> AnalyzeTypes()
    {
        List<TypeSize> typeSizes = [];

        var module = _assembly.MainModule;
        typeSizes.Capacity = module.Types.Count;

        foreach (var type in module.Types)
        {
            typeSizes.Add(new TypeSize(type.FullName, ComputeIlSize(type), ComputeMetadataSize(type)));
        }

        return typeSizes;
    }

    public List<ResourceSize> ComputeResourcesSize()
    {
        List<ResourceSize> resourceSizes = [];

        var resources = _assembly.MainModule.Resources;
        resourceSizes.Capacity = resources.Count;

        foreach (var embeddedResource in resources.OfType<EmbeddedResource>())
        {
            resourceSizes.Add(new ResourceSize(embeddedResource.Name, embeddedResource.GetResourceData().Length));
        }

        return resourceSizes;
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
