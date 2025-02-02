namespace AssemblySizeAnalyzer;

public readonly struct TypeSize : IComparable<TypeSize>
{
    public readonly string FullName;
    public readonly long IlSize;
    public readonly long OverheadSize;

    // computed fields
    public readonly string Namespace;
    public readonly string TypeName;
    public readonly long TotalSize;

    public TypeSize(string fullName, long ilSize, long overheadSize)
    {
        FullName = fullName;
        IlSize = ilSize;
        OverheadSize = overheadSize;
        TotalSize = ilSize + overheadSize;

        var lastDot = FullName.LastIndexOf('.');

        if (lastDot < 0)
        {
            Namespace = "<Global>";
            TypeName = FullName;
        }
        else
        {
            Namespace = FullName[..lastDot];
            TypeName = FullName[(lastDot + 1)..];
        }
    }

    int IComparable<TypeSize>.CompareTo(TypeSize other)
    {
        return TotalSize.CompareTo(other.TotalSize);
    }

    public override string ToString()
    {
        return FullName;
    }
}
