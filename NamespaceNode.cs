namespace AssemblySizeAnalyzer;

public class NamespaceNode
{
    public string FullNs { get; }
    public string NamespaceSegment { get; }
    public List<NamespaceNode> ChildNamespaces { get; } = [];
    public List<TypeSize> ChildTypes { get; } = [];
    public long TotalSize { get; private set; }

    public NamespaceNode(string fullNs, string nsSegment)
    {
        FullNs = fullNs;
        NamespaceSegment = nsSegment;
    }

    public void AddChild(NamespaceNode ns)
    {
        ChildNamespaces.Add(ns);
    }

    public void AddChild(TypeSize type)
    {
        ChildTypes.Add(type);
    }

    public long ComputeTotalSize()
    {
        long size = 0;

        foreach (var ns in ChildNamespaces)
        {
            size += ns.ComputeTotalSize();
        }

        foreach (var type in ChildTypes)
        {
            size += type.TotalSize;
        }

        TotalSize = size;
        return size;
    }

    public override string ToString()
    {
        return NamespaceSegment;
    }
}
