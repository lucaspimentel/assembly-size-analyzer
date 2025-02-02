using System.ComponentModel;
using Spectre.Console.Cli;

namespace AssemblySizeAnalyzer;

internal sealed class AnalyzeCommandSettings : CommandSettings
{
    [CommandArgument(0, "<assemblyPath>")]
    [Description("Path to assembly to analyze.")]
    public required string AssemblyPath { get; init; }

    [CommandOption("--show-types")]
    [DefaultValue(false)]
    [Description("Show the size of each type inside a namespace.")]
    public bool ShowTypes { get; init; }

    [CommandOption("--max-depth")]
    [DefaultValue(4)]
    [Description("The maximum depth of nodes  to show.")]
    public int MaxDepth { get; init; }

    [CommandOption("--min-size")]
    [DefaultValue(1000)]
    [Description("Only include nodes that are larger than the specified size in bytes.")]
    public int MinSize { get; init; }

    [CommandOption("--namespace")]
    [Description("Only include the specified namespace and its children.")]
    public string? NamespaceFilter { get; init; }

    [CommandOption("--size-units|--size-unit")]
    [DefaultValue(SizeUnit.Auto)]
    [Description("The size unit to use: \"auto\", \"mb\", \"kb\", or \"b\". Default: \"auto\".")]
    public SizeUnit SizeUnits { get; init; }
}
