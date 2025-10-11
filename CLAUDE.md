# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Assembly Size Analyzer** is a cross-platform command-line tool that analyzes .NET assembly files to estimate the size in bytes used by each type and displays results as a tree grouped by namespace. Based on Sizer.Net but built as a modern CLI tool targeting .NET 9.

## Building and Running

### Build the project
```bash
dotnet build src/AssemblySizeAnalyzer.csproj
```

### Run the tool
```bash
dotnet run --project src/AssemblySizeAnalyzer.csproj -- <path-to-assembly>
```

### Publish as single file
The project is configured to publish as a single file (see `PublishSingleFile` in .csproj):
```bash
dotnet publish src/AssemblySizeAnalyzer.csproj -c Release
```

## Architecture

### Core Components

- **Program.cs** - Entry point that sets up Spectre.Console CLI with `AnalyzeCommand`
- **AnalyzeCommand.cs** - Main command implementation that orchestrates the analysis workflow and renders output
- **AssemblyAnalyzer.cs** - Core analysis engine using Mono.Cecil to inspect assemblies
- **NamespaceNode.cs** - Tree structure for organizing types by namespace hierarchy

### Data Models

- **TypeSize.cs** - Represents a type with IL size and metadata size estimates
- **ResourceSize.cs** - Represents embedded resource size
- **Namespace.cs** - Namespace string representation and splitting logic
- **SizeUnit.cs** - Enum for size display units (B, KB, MB, Auto)
- **AnalyzeCommandSettings.cs** - CLI command options via Spectre.Console.Cli

### Analysis Workflow (AnalyzeCommand.cs:44-68)

1. Load assembly using `AssemblyAnalyzer.Load()` with Mono.Cecil
2. Analyze resources with `AnalyzeResources()` (only if no namespace filter)
3. Analyze types with `AnalyzeTypes()` (applies namespace filter if specified)
4. Build namespace tree by recursively creating `NamespaceNode` hierarchy
5. Compute total sizes bottom-up with `ComputeTotalSize()`
6. Render breakdown chart and tree with Spectre.Console

### Size Calculations (AssemblyAnalyzer.cs:50-91)

**IL Size**: Sum of method body code sizes from Mono.Cecil

**Metadata Size** (estimated):
- Method entries: count × 16 bytes
- Field entries: count × 12 bytes
- Property entries: count × 10 bytes
- String metadata: UTF-8 byte count of all names (types, methods, fields, properties, parameters, events)
- Static field storage: estimated by type
- String constants: UTF-8 byte count
- Nested types: recursive calculation

**Total Size**: IL + Metadata + Resources, compared against actual file size

## Command Options

- `<assemblyPath>` - Path to .NET assembly (required argument)
- `--show-types` - Display individual types within namespaces
- `--max-depth N` - Maximum tree depth (default: 4)
- `--min-size N` - Minimum size in bytes to display (default: 1000)
- `--namespace NS` - Filter to specific namespace and children
- `--size-units UNIT` - Display units: auto, mb, kb, b (default: auto)

## Key Dependencies

- **Mono.Cecil** (0.11.6) - Assembly inspection and IL analysis
- **Spectre.Console** (0.49.1) - Rich terminal UI (tree, charts, markup)
- **Spectre.Console.Cli** (0.49.1) - Command-line framework

## Output Format

1. **Breakdown Chart** - Visual bar chart showing file size composition (metadata, resources, IL, other)
2. **Size Tree** - Hierarchical tree of namespaces and types with sizes and percentages

The tool uses Spectre.Console for rich terminal output including colors, formatting, and visual components.
