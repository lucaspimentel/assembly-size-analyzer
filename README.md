A command-line tool that analyzes the size of .NET assemblies, grouped by namespace.

Pre-built binaries support Windows and Linux. macOS is supported when built from source.

Based on **Sizer.Net**, a WinForms tool which targets .NET Framework 4.5: https://github.com/schellingb/sizer-net.

For a version of **Sizer.Net** that targets .NET 9, see fork: https://github.com/lucaspimentel/sizer-net.

## Installation

### Scoop (Windows)

```powershell
scoop bucket add lucaspimentel https://github.com/lucaspimentel/scoop-bucket
scoop install analyze-assembly-size
```

### Download pre-built binary

Requires PowerShell 7+.

```pwsh
irm https://raw.githubusercontent.com/lucaspimentel/analyze-assembly-size/main/install-remote.ps1 | iex
```

### Build from source

Requires PowerShell 7+ and [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```pwsh
git clone https://github.com/lucaspimentel/analyze-assembly-size
cd analyze-assembly-size
./install-local.ps1
```

Both scripts install to `~/.local/bin/analyze-assembly-size`. Ensure that directory is in your `PATH`.

## Usage

```bash
analyze-assembly-size <path-to-assembly>
```

### Options

| Option | Description | Default |
|---|---|---|
| `--show-types` | Display individual types within namespaces | `false` |
| `--max-depth N` | Maximum tree depth (deeper nodes are hidden) | `4` |
| `--min-size N` | Minimum size in bytes to display (smaller nodes are hidden) | `1000` |
| `--namespace NS` | Filter to specific namespace and children (other nodes are hidden) | |
| `--size-units UNIT` | Display units: `auto`, `mb`, `kb`, `b` | `auto` |
| `--json` | Output results as JSON only | `false` |

## Screenshot

![Screenshot](screenshot.png)

## License

This project is licensed under the [MIT License](LICENSE).
