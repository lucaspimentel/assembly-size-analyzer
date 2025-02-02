namespace AssemblySizeAnalyzer;

public readonly struct ResourceSize : IComparable<ResourceSize>
{
    public readonly string Name;
    public readonly long Size;

    public ResourceSize(string name, long size)
    {
        Name = name;
        Size = size;
    }

    int IComparable<ResourceSize>.CompareTo(ResourceSize other)
    {
        return Size.CompareTo(other.Size);
    }

    public override string ToString()
    {
        return Name;
    }
}
