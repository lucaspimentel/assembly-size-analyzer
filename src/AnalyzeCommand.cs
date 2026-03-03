using System.Text.Json;
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

        var assembly = AssemblyAnalyzer.Load(assemblyPath);
        var resources = assembly.AnalyzeResources();
        var allTypes = assembly.AnalyzeTypes(null);

        if (settings.Json)
        {
            return ExecuteJson(assembly, allTypes, resources);
        }

        return ExecuteInteractive(assemblyPath, settings, assembly, allTypes, resources);
    }

    private static int ExecuteJson(AssemblyAnalyzer assembly, List<TypeSize> allTypes, List<ResourceSize> resources)
    {
        var jsonData = new
        {
            assemblyName = assembly.FullName,
            fileSize = assembly.FileSize,
            types = allTypes.Select(t => new
            {
                name = t.FullName,
                ilSize = t.IlSize,
                metadataSize = t.OverheadSize
            }),
            resources = resources.Select(r => new
            {
                name = r.Name,
                size = r.Size
            })
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        Console.Out.WriteLine(JsonSerializer.Serialize(jsonData, options));
        return 0;
    }

    private static int ExecuteInteractive(
        string assemblyPath,
        AnalyzeCommandSettings settings,
        AssemblyAnalyzer assembly,
        List<TypeSize> allTypes,
        List<ResourceSize> resources)
    {
        AnsiConsole.MarkupLine($"Analyzing: [blue]{assemblyPath}[/]");
        AnsiConsole.Markup($"Show types: [blue]{settings.ShowTypes}[/], ");
        AnsiConsole.Markup($"Max depth: [blue]{settings.MaxDepth:N0}[/], ");
        AnsiConsole.Markup($"Min size: [blue]{FormatSize(settings.MinSize, SizeUnit.Auto)}[/], ");
        AnsiConsole.Markup($"Units: [blue]{settings.SizeUnits}[/], ");
        AnsiConsole.MarkupLine($"Namespace filter: [blue]{settings.NamespaceFilter ?? "(none)"}[/]");
        AnsiConsole.WriteLine();

        var hasFilter = !string.IsNullOrEmpty(settings.NamespaceFilter);
        var fullResourcesSize = resources.Sum(r => r.Size);
        var fullIlSize = allTypes.Sum(t => t.IlSize);
        var fullMetadataSize = allTypes.Sum(t => t.OverheadSize);

        // When filtering, narrow down the already-computed list
        var filteredTypes = hasFilter
            ? allTypes.Where(t => t.FullName.StartsWith(settings.NamespaceFilter!)).ToList()
            : allTypes;

        var filteredIlSize = filteredTypes.Sum(t => t.IlSize);
        var filteredMetadataSize = filteredTypes.Sum(t => t.OverheadSize);

        // dummy root node to hold the tree, won't be displayed
        var rootNode = new NamespaceNode(string.Empty, string.Empty);

        foreach (var type in allTypes)
        {
            var nsSegments = new ArraySegment<string>(type.Namespace.Split('.'));
            var node = GetOrCreateNamespaceNode(type.Namespace, nsSegments, rootNode);
            node.AddChild(type);
        }

        // update metadata size and total size of all nodes in the tree
        _ = rootNode.ComputeTotalSize();

        AnsiConsole.MarkupLine($"Assembly: [blue]{assembly.FullName}[/]");
        AnsiConsole.WriteLine();

        // use a single size unit for all labels in the chart for consistency,
        // instead of different units for each label
        var breakdownChartSizeUnits = settings.SizeUnits == SizeUnit.Auto ? GetBestSizeUnits(assembly.FileSize) : settings.SizeUnits;

        PrintSizeBreakdownChart(
            assembly.FileSize,
            fullMetadataSize,
            fullResourcesSize,
            fullIlSize,
            breakdownChartSizeUnits,
            hasFilter ? filteredMetadataSize : null,
            hasFilter ? filteredIlSize : null);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("The namespace and type sizes in the tree below include IL and an [italic]estimate[/] of metadata size.");
        AnsiConsole.WriteLine("Any unaccounted bytes are shown as \"other\" bytes in the breakdown chart above.");
        AnsiConsole.WriteLine();

        PrintSizeTree(
            settings: settings,
            rootNamespaces: rootNode.ChildNamespaces,
            resources: hasFilter ? [] : resources);

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

    private static void PrintSizeTree(
        AnalyzeCommandSettings settings,
        List<NamespaceNode> rootNamespaces,
        List<ResourceSize> resources)
    {
        var totalNamespaceSize = rootNamespaces.Sum(n => n.TotalSize);
        var totalResourcesSize = resources.Sum(r => r.Size);
        var totalComputedSize = totalNamespaceSize + totalResourcesSize;

        // use a single size unit for all nodes in the tree for consistency,
        // instead of different units for each node
        var sizeUnits = settings.SizeUnits == SizeUnit.Auto ? GetBestSizeUnits(totalComputedSize) : settings.SizeUnits;

        string rootNodeText;

        if (rootNamespaces.Count == 1)
        {
            // if there's only one namespace (for example, when using a namespace filter),
            // show the namespace name as the root node
            var rootNamespace = rootNamespaces[0];
            rootNamespaces = rootNamespace.ChildNamespaces;

            rootNodeText = FormatNode(
                rootNamespace.NamespaceSegment,
                rootNamespace.TotalSize,
                rootNamespace.TotalSize,
                sizeUnits);
        }
        else
        {
            rootNodeText = FormatNode("Total", totalComputedSize, totalComputedSize, sizeUnits);
        }

        var tree = new Tree(rootNodeText);

        AddNamespaceNodes(
            treeNodes: tree,
            nsNodes: rootNamespaces,
            currentDepth: 1,
            totalSize: totalComputedSize,
            settings: settings,
            sizeUnits: sizeUnits);

        if (resources.Count > 0)
        {
            var resourceNode = tree.AddNode(FormatNode("Embedded resources", totalResourcesSize, totalComputedSize, sizeUnits));

            foreach (var resource in resources.OrderByDescending(r => r.Size))
            {
                resourceNode.AddNode(FormatNode(resource.Name, resource.Size, totalComputedSize, sizeUnits));
            }
        }

        AnsiConsole.Write(new Padder(tree).PadLeft(1));
    }

    private static void PrintSizeBreakdownChart(
        long fileSize,
        long totalMetadataSize,
        long totalResourcesSize,
        long totalIlSize,
        SizeUnit sizeUnits,
        long? filteredMetadataSize = null,
        long? filteredIlSize = null)
    {
        var otherSize = fileSize - totalIlSize - totalMetadataSize - totalResourcesSize;

        var sizes = new List<(string Text, long Size, Color Color)>();

        if (filteredMetadataSize.HasValue)
        {
            // Split metadata into filtered and other segments
            sizes.Add(("Metadata (filtered)", filteredMetadataSize.Value, Color.Blue));
            sizes.Add(("Metadata (other)", totalMetadataSize - filteredMetadataSize.Value, Color.Grey));
        }
        else
        {
            sizes.Add(("Metadata", totalMetadataSize, Color.Blue));
        }

        if (filteredIlSize.HasValue)
        {
            // Split IL into filtered and other segments
            sizes.Add(("IL (filtered)", filteredIlSize.Value, Color.Green));
            sizes.Add(("IL (other)", totalIlSize - filteredIlSize.Value, Color.DarkGreen));
        }
        else
        {
            sizes.Add(("IL", totalIlSize, Color.Green));
        }

        sizes.Add(("Resources", totalResourcesSize, Color.Yellow));
        sizes.Add(("Other", otherSize, Color.Red));

        var displayedSized = sizes.Where(s => s.Size > 0)
                                  .OrderByDescending(s => s.Size);

        var breakdownChart = new BreakdownChart()
                             .Width(AnsiConsole.Profile.Width)
                             .UseValueFormatter(d => $"{FormatSizeWithPercent((long)d, fileSize, sizeUnits)}");

        foreach (var (text, size, color) in displayedSized)
        {
            breakdownChart.AddItem(text, size, color);
        }

        var panel = new Panel(breakdownChart)
                    .Header($"[blue]Assembly file size {FormatSize(fileSize, sizeUnits)}[/]")
                    .Padding(horizontal: 3, vertical: 1);

        AnsiConsole.Write(new Padder(child: panel).Padding(horizontal: 1, vertical: 0));
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

            // when filtering, ancestor nodes (on the path to the filter) don't count toward depth
            var isAncestorOfFilter = settings.NamespaceFilter != null &&
                                     settings.NamespaceFilter.StartsWith(childNamespace.FullNs) &&
                                     childNamespace.FullNs != settings.NamespaceFilter;

            if (!isAncestorOfFilter && currentDepth > settings.MaxDepth)
            {
                // AnsiConsole.MarkupLine("Hiding [yellow]{0}[/], too deep.", childNamespace.FullNs);
                continue;
            }

            if (!isAncestorOfFilter && childNamespace.TotalSize < settings.MinSize)
            {
                // AnsiConsole.MarkupLine("Hiding [yellow]{0}[/], too small.", childNamespace.FullNs);
                continue;
            }

            var childTreeNode = treeNodes.AddNode(FormatNode($"{childNamespace.NamespaceSegment}", childNamespace.TotalSize, totalSize, sizeUnits));

            AddNamespaceNodes(
                treeNodes: childTreeNode,
                nsNodes: childNamespace.ChildNamespaces,
                currentDepth: isAncestorOfFilter ? currentDepth : currentDepth + 1,
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
