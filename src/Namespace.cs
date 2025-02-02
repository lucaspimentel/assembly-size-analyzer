using System.Collections.Immutable;

namespace AssemblySizeAnalyzer;

public readonly struct Namespace
{
    public readonly string FullName;
    public readonly string First;
    public readonly string Last;
    public readonly ImmutableArray<string> Parts;

    public Namespace(string value)
    {
        FullName = value;

        Parts = [..value.Split('.')];
        First = Parts[0];
        Last = Parts[^1];
    }

    public bool Equals(Namespace other)
    {
        return FullName == other.FullName;
    }

    public override bool Equals(object? obj)
    {
        return obj is Namespace other && Equals(other);
    }

    public override int GetHashCode()
    {
        return FullName.GetHashCode();
    }

    public static bool operator ==(Namespace left, Namespace right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Namespace left, Namespace right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return FullName;
    }
}
