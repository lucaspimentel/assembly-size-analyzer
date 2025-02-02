using Spectre.Console;
using Spectre.Console.Cli;

namespace AssemblySizeAnalyzer;

internal sealed class AnalyzeAssemblySizeCommand : Command<AnalyzeAssemblySizeCommandSettings>
{
    public override ValidationResult Validate(CommandContext context, AnalyzeAssemblySizeCommandSettings settings)
    {
        var result = base.Validate(context, settings);

        if (result.Successful && !File.Exists(settings.AssemblyPath))
        {
            return ValidationResult.Error($"The specified assembly '{settings.AssemblyPath}' does not exist.");
        }

        return result;
    }

    public override int Execute(CommandContext context, AnalyzeAssemblySizeCommandSettings settings)
    {
        AnsiConsole.MarkupLine("Analyzing: [blue]{0}[/]", settings.AssemblyPath);
        AnsiConsole.Markup("Show types: [blue]{0}[/], ", settings.ShowTypes);
        AnsiConsole.Markup("Max depth: [blue]{0:N0}[/], ", settings.MaxDepth);
        AnsiConsole.Markup("Min size: [blue]{0:N0}[/], ", settings.MinSize);
        AnsiConsole.MarkupLine("Namespace filter: [blue]{0}[/]", settings.NamespaceFilter ?? "(none)");
        AnsiConsole.WriteLine();

        AssemblyAnalyzer assembly = null!;
        List<ResourceSize> resources = null!;
        List<TypeSize> types = null!;

        AnsiConsole.Status()
                   .Start("Analyzing assembly...", _ =>
                   {
                       assembly = AssemblyAnalyzer.Load(settings.AssemblyPath);
                       resources = assembly.ComputeResourcesSize();
                       types = assembly.AnalyzeTypes();
                   });

        AnsiConsole.MarkupLine($"Assembly: [blue]{assembly.FullName}[/]");
        AnsiConsole.WriteLine();

        // dummy root note to hold the tree, won't be displayed
        var rootNode = new NamespaceNode(string.Empty, string.Empty);

        foreach (var type in types)
        {
            var nsSegments = new ArraySegment<string>(type.Namespace.Split('.'));
            var node = GetOrCreateNamespaceNode(type.Namespace, nsSegments, rootNode);
            node.AddChild(type);
        }

        var totalResourcesSize = resources.Sum(r => r.Size);
        var totalIlSize = types.Sum(t => t.IlSize);

        // update overhead and total size of all nodes in the tree
        var totalComputedSize = rootNode.ComputeTotalSize() + totalResourcesSize;

        DisplaySizeBreakdownChart(assembly, totalResourcesSize, totalIlSize);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("The namespace and type sizes in the tree below include IL and an [italic]estimate[/] of metadata size.");
        AnsiConsole.WriteLine("Any unaccounted bytes are shown as \"other\" bytes in the breakdown chart above.");
        AnsiConsole.WriteLine();

        var rootNodeText = $"Total size: {FormatSize(totalComputedSize)}";
        var rootNamespaces = rootNode.ChildNamespaces;

        DisplaySizeTree(
            settings,
            rootNodeText,
            totalComputedSize: totalComputedSize,
            rootNamespaces,
            resources);

        return 0;
    }

    private static void DisplaySizeTree(
        AnalyzeAssemblySizeCommandSettings settings,
        string rootNodeText,
        long totalComputedSize,
        List<NamespaceNode> rootNamespaces,
        List<ResourceSize> resources)
    {
        var tree = new Tree(rootNodeText);

        if (resources.Count > 0)
        {
            var totalResourcesSize = resources.Sum(r => r.Size);
            var resourceNode = tree.AddNode(FormatNode("Embedded resources", totalResourcesSize, totalComputedSize));

            foreach (var resource in resources.OrderByDescending(r => r.Size))
            {
                resourceNode.AddNode(FormatNode(resource.Name, resource.Size, totalComputedSize));
            }
        }

        AddNamespaceNodes(
            treeNodes: tree,
            nsNodes: rootNamespaces,
            currentDepth: 1,
            totalSize: totalComputedSize,
            settings: settings);

        AnsiConsole.Write(tree);
    }

    private static void DisplaySizeBreakdownChart(AssemblyAnalyzer assembly, long totalResourcesSize, long totalIlSize)
    {
        var breakdownChart = new BreakdownChart()
                             .Width(Console.WindowWidth)
                             .UseValueFormatter(d => $"{FormatSizeAndPercent((long)d, assembly.FileSize)}");

        var otherSize = assembly.FileSize - totalIlSize - assembly.TotalMetadataSize - totalResourcesSize;

        (string Text, long Size, Color Color)[] sizes =
        [
            ("Metadata", assembly.TotalMetadataSize, Color.Blue),
            ("Resources", totalResourcesSize, Color.Yellow),
            ("IL", totalIlSize, Color.Green),
            ("Other", otherSize, Color.Red)
        ];

        var displayedSized = sizes.Where(s => s.Size > 0)
                                  .OrderByDescending(s => s.Size);

        foreach (var (text, size, color) in displayedSized)
        {
            breakdownChart.AddItem(text, size, color);
        }

        var panel = new Panel(breakdownChart)
                    .Header($"[blue]Assembly file size {FormatSize(assembly.FileSize)}[/]")
                    .Padding(horizontal: 3, vertical: 1);

        AnsiConsole.Write(panel);
    }

    private static void AddNamespaceNodes(
        IHasTreeNodes treeNodes,
        List<NamespaceNode> nsNodes,
        int currentDepth,
        long totalSize,
        AnalyzeAssemblySizeCommandSettings settings)
    {
        foreach (var childNamespace in nsNodes.OrderByDescending(n => n.TotalSize))
        {
            if (settings.NamespaceFilter != null &&
                !settings.NamespaceFilter.StartsWith(childNamespace.FullNs) &&
                !childNamespace.FullNs.StartsWith(settings.NamespaceFilter))
            {
                // AnsiConsole.MarkupLine("Hiding [yellow]{0}[/] by filter.", childNamespace.FullNs);
                continue;
            }

            if (currentDepth > settings.MaxDepth)
            {
                // AnsiConsole.MarkupLine("Hiding [yellow]{0}[/], too deep.", childNamespace.FullNs);
                continue;
            }

            if (childNamespace.TotalSize < settings.MinSize)
            {
                // AnsiConsole.MarkupLine("Hiding [yellow]{0}[/], too small.", childNamespace.FullNs);
                continue;
            }

            var childTreeNode = treeNodes.AddNode(FormatNode($"{childNamespace.NamespaceSegment}", childNamespace.TotalSize, totalSize));

            AddNamespaceNodes(
                treeNodes: childTreeNode,
                nsNodes: childNamespace.ChildNamespaces,
                currentDepth: currentDepth + 1,
                totalSize: totalSize,
                settings: settings);

            if (settings.ShowTypes)
            {
                var children = childNamespace.ChildTypes
                                             .Where(c => settings.NamespaceFilter == null || c.Namespace == settings.NamespaceFilter)
                                             .OrderByDescending(c => c.TotalSize);

                foreach (var child in children)
                {
                    if (currentDepth + 1 > settings.MaxDepth)
                    {
                        // AnsiConsole.MarkupLine("Hiding [green]{0}[/], too small.", child.FullName);
                        continue;
                    }

                    if (childNamespace.TotalSize < settings.MinSize)
                    {
                        // AnsiConsole.MarkupLine("Hiding [green]{0}[/], too small.", child.FullName);
                        continue;
                    }

                    childTreeNode.AddNode(FormatNode($"[green]{child.TypeName}[/]", child.TotalSize, totalSize));
                }
            }
        }
    }

    private static NamespaceNode GetOrCreateNamespaceNode(string fullNs, ArraySegment<string> remainingNsSegments, NamespaceNode node)
    {
        if (remainingNsSegments.Count == 0)
        {
            return node;
        }

        foreach (var child in node.ChildNamespaces)
        {
            if (child.NamespaceSegment == remainingNsSegments[0])
            {
                return GetOrCreateNamespaceNode(fullNs, remainingNsSegments[1..], child);
            }
        }

        var newChild = new NamespaceNode(fullNs, remainingNsSegments[0]);
        node.AddChild(newChild);
        return GetOrCreateNamespaceNode(fullNs, remainingNsSegments[1..], newChild);
    }

    private static string FormatNode(string text, long value, long total)
    {
        return $"{text} {FormatSizeAndPercent(value, total)}";
    }

    private static string FormatSizeAndPercent(long value, long total)
    {
        return $"[bold yellow]{FormatSize(value)}[/] {FormatPercent(value, total)}";
    }

    private static string FormatSize(long size)
    {
        return size switch
        {
            >= 1_000_000 => FormatSizeMb(size),
            >= 1_000 => $"{size / 1_000.0:F2} KB",
            _ => $"{size:N0} bytes"
        };
    }

    private static string FormatSizeMb(long size) => $"{size / 1_000_000.0:F2} MB";

    private static string FormatPercent(long value, long total)
    {
        return $"[dim]({value * 100.0 / total:F2}%)[/]";
    }
}
