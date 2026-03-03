# CLAUDE.md

Cross-platform CLI that analyzes .NET assembly files, estimates per-type size (IL + metadata), and displays results as a namespace tree. Targets net10.0.

## Commands

```bash
dotnet build src/AssemblySizeAnalyzer.csproj
dotnet run --project src/AssemblySizeAnalyzer.csproj -- <path-to-assembly>
dotnet publish src/AssemblySizeAnalyzer.csproj -c Release
```

## Key Design Decisions

- Metadata sizes are **estimates** — per-type metadata is computed heuristically, then unaccounted metadata (from PE headers) is distributed proportionally across types (`AssemblyAnalyzer.cs:42-61`)
- `--namespace` filter is **display-only** — the tree is built from all types so parent nodes show full-assembly sizes; only nodes on the filter path are rendered; ancestor nodes skip depth/min-size checks (`AnalyzeCommand.cs:253-275`)
- `samples/` contains pre-built assemblies for manual testing
