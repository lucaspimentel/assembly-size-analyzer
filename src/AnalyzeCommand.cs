using Spectre.Console;
using Spectre.Console.Cli;

namespace AssemblySizeAnalyzer;

internal sealed class AnalyzeCommand : Command<AnalyzeCommandSettings>
{
    public override ValidationResult Validate(CommandContext context, AnalyzeCommandSettings settings)
    {
        var result = base.Validate(context, settings);

        if (!string.IsNullOrEmpty(settings.AssemblyPath))
        {
            var assemblyPath = ExpandPath(settings.AssemblyPath);

            if (!File.Exists(assemblyPath))
            {
                return ValidationResult.Error($"File '{assemblyPath}' not found.");
            }
        }


        return result;
    }

    public override int Execute(CommandContext context, AnalyzeCommandSettings settings)
    {
        var assemblyPath = ExpandPath(settings.AssemblyPath);
        var sizeUnits = settings.SizeUnits ?? SizeUnit.Auto;

        AnsiConsole.MarkupLine($"Analyzing: [blue]{assemblyPath}[/]");
        AnsiConsole.Markup($"Show types: [blue]{settings.ShowTypes}[/], ");
        AnsiConsole.Markup($"Max depth: [blue]{settings.MaxDepth:N0}[/], ");
        AnsiConsole.Markup($"Min size: [blue]{FormatSize(settings.MinSize, SizeUnit.Auto)}[/], ");
        AnsiConsole.Markup($"Units: [blue]{settings.SizeUnits}[/], ");
        AnsiConsole.MarkupLine($"Namespace filter: [blue]{settings.NamespaceFilter ?? "(none)"}[/]");
        AnsiConsole.WriteLine();

        AssemblyAnalyzer assembly = null!;
        List<ResourceSize> resources = null!;
        List<TypeSize> types = null!;

        AnsiConsole.Status()
                   .Start("Analyzing assembly...", _ =>
                   {
                       assembly = AssemblyAnalyzer.Load(assemblyPath);
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

        DisplaySizeBreakdownChart(assembly.FileSize, assembly.TotalMetadataSize, totalResourcesSize, totalIlSize, sizeUnits);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("The namespace and type sizes in the tree below include IL and an [italic]estimate[/] of metadata size.");
        AnsiConsole.WriteLine("Any unaccounted bytes are shown as \"other\" bytes in the breakdown chart above.");
        AnsiConsole.WriteLine();

        // update metadata size and total size of all nodes in the tree
        var totalComputedSize = rootNode.ComputeTotalSize() + totalResourcesSize;

        // use a single size unit for all nodes in the tree for consistency
        var sizeUnit = sizeUnits == SizeUnit.Auto ? GetBestSizeUnits(totalComputedSize) : sizeUnits;

        var rootNodeText = $"Total size: {FormatSizeWithPercent(totalComputedSize, totalComputedSize, sizeUnit)}";
        var rootNamespaces = rootNode.ChildNamespaces;

        DisplaySizeTree(
            settings: settings,
            sizeUnits: sizeUnits,
            rootNodeText: rootNodeText,
            totalComputedSize: totalComputedSize,
            rootNamespaces: rootNamespaces,
            resources: resources);

        return 0;
    }

    private static string ExpandPath(string path)
    {
        if (path.StartsWith('~'))
        {
            var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(homePath, path.TrimStart('~', '/', '\\'));
        }

        return Path.GetFullPath(path);
    }

    private static void DisplaySizeTree(
        AnalyzeCommandSettings settings,
        SizeUnit sizeUnits,
        string rootNodeText,
        long totalComputedSize,
        List<NamespaceNode> rootNamespaces,
        List<ResourceSize> resources)
    {
        var tree = new Tree(rootNodeText);

        if (resources.Count > 0)
        {
            var totalResourcesSize = resources.Sum(r => r.Size);
            var resourceNode = tree.AddNode(FormatNode("Embedded resources", totalResourcesSize, totalComputedSize, sizeUnits));

            foreach (var resource in resources.OrderByDescending(r => r.Size))
            {
                resourceNode.AddNode(FormatNode(resource.Name, resource.Size, totalComputedSize, sizeUnits));
            }
        }

        AddNamespaceNodes(
            treeNodes: tree,
            nsNodes: rootNamespaces,
            currentDepth: 1,
            totalSize: totalComputedSize,
            settings: settings,
            sizeUnits: sizeUnits);

        AnsiConsole.Write(tree);
    }

    private static void DisplaySizeBreakdownChart(long fileSize, long totalMetadataSize, long totalResourcesSize, long totalIlSize, SizeUnit sizeUnits)
    {
        // use a single size unit for all parts of the chart for consistency
        var sizeUnit = sizeUnits == SizeUnit.Auto ? GetBestSizeUnits(fileSize) : sizeUnits;
        var otherSize = fileSize - totalIlSize - totalMetadataSize - totalResourcesSize;

        (string Text, long Size, Color Color)[] sizes =
        [
            ("Metadata", totalMetadataSize, Color.Blue),
            ("Resources", totalResourcesSize, Color.Yellow),
            ("IL", totalIlSize, Color.Green),
            ("Other", otherSize, Color.Red)
        ];

        var displayedSized = sizes.Where(s => s.Size > 0)
                                  .OrderByDescending(s => s.Size);

        var breakdownChart = new BreakdownChart()
                             .Width(Console.WindowWidth)
                             .UseValueFormatter(d => $"{FormatSizeWithPercent((long)d, fileSize, sizeUnit)}");

        foreach (var (text, size, color) in displayedSized)
        {
            breakdownChart.AddItem(text, size, color);
        }

        var panel = new Panel(breakdownChart)
                    .Header($"[blue]Assembly file size {FormatSize(fileSize, sizeUnit)}[/]")
                    .Padding(horizontal: 3, vertical: 1);

        AnsiConsole.Write(panel);
    }

    private static void AddNamespaceNodes(
        IHasTreeNodes treeNodes,
        List<NamespaceNode> nsNodes,
        int currentDepth,
        long totalSize,
        AnalyzeCommandSettings settings,
        SizeUnit sizeUnits)
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

            var childTreeNode = treeNodes.AddNode(FormatNode($"{childNamespace.NamespaceSegment}", childNamespace.TotalSize, totalSize, sizeUnits));

            AddNamespaceNodes(
                treeNodes: childTreeNode,
                nsNodes: childNamespace.ChildNamespaces,
                currentDepth: currentDepth + 1,
                totalSize: totalSize,
                settings: settings,
                sizeUnits: sizeUnits);

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

                    childTreeNode.AddNode(FormatNode($"[green]{child.TypeName}[/]", child.TotalSize, totalSize, sizeUnits));
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

    private static string FormatNode(string text, long value, long total, SizeUnit unit)
    {
        return $"{text} {FormatSizeWithPercent(value, total, unit)}";
    }

    private static string FormatSizeWithPercent(long value, long total, SizeUnit unit)
    {
        return $"[bold yellow]{FormatSize(value, unit)}[/] [dim]({FormatPercent(value, total)})[/]";
    }

    private static string FormatSize(long size, SizeUnit unit)
    {
        if (unit == SizeUnit.Auto)
        {
            unit = GetBestSizeUnits(size);
        }

        return unit switch
        {
            SizeUnit.B => Format(size, 1, "bytes"),
            SizeUnit.Kb => Format(size, 1_000, "KB"),
            SizeUnit.Mb => Format(size, 1_000_000, "MB"),

            // silence compiler warning, we handle SizeUnit.Auto above
            SizeUnit.Auto => throw new ArgumentException("Invalid unit: Auto", nameof(unit)),
            _ => throw new ArgumentException($"Invalid units: {unit}", nameof(unit))
        };

        static string Format(long size, float unitSize, string unit)
        {
            const double minValue = 0.01;
            var value = size / unitSize;

            return value < minValue ?
                $"< {minValue:#,##0.##} {unit}" :
                $"{value:#,##0.##} {unit}";
        }
    }

    private static string FormatPercent(long value, long total)
    {
        const double minimumPercent = 0.001;
        var percent = value / (float)total;

        return percent < minimumPercent ?
            $"< {minimumPercent:P1}" :
            $"{percent:P1}";
    }

    private static SizeUnit GetBestSizeUnits(long fileSize)
    {
        var sizeUnit = fileSize switch
        {
            < 1_000 => SizeUnit.B,
            < 1_000_000 => SizeUnit.Kb,
            _ => SizeUnit.Mb
        };
        return sizeUnit;
    }
}
