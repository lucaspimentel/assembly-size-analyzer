A cross-platform command-line tool that analyzes the contents of a .NET assembly file, estimates the file size in bytes used by each type, and shows the results as a tree, grouped by namespace.

Based on **Sizer.Net**, a WinForms tool which targets .NET Framework 4.5: https://github.com/schellingb/sizer-net.

For a version of **Sizer.Net** that targets .NET 9, see fork: https://github.com/lucaspimentel/sizer-net.

## Usage

Requires .NET 10 SDK.

```bash
# Build
dotnet build src/AssemblySizeAnalyzer.csproj

# Run
dotnet run --project src/AssemblySizeAnalyzer.csproj -- <path-to-assembly>

# Publish as single-file executable
dotnet publish src/AssemblySizeAnalyzer.csproj -c Release

# Install to ~/.local/bin
./install-local.ps1
```

### Options

| Option | Description | Default |
|---|---|---|
| `--show-types` | Display individual types within namespaces | `false` |
| `--max-depth N` | Maximum tree depth | `4` |
| `--min-size N` | Minimum size in bytes to display | `1000` |
| `--namespace NS` | Filter to specific namespace and children | |
| `--size-units UNIT` | Display units: `auto`, `mb`, `kb`, `b` | `auto` |
| `--json` | Output results as JSON only | `false` |

## Screenshot

![Screenshot](screenshot.png)

## License

This project is licensed under the [MIT License](LICENSE).