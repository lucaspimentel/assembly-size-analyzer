# Changelog

## [v1.0.0-beta.2] - 2026-06-16

### Added
- Add SHA256 checksum verification to remote install script

### Changed
- Update NuGet dependencies to latest versions
- Update GitHub Actions to latest versions
- Update README description and add platform support note
- Update installation and usage instructions and add Scoop install method
- Clarify command line option descriptions
- Add CI and Release workflow badges to README
- Rename repo to analyze-assembly-size

## [v1.0.0-beta.1] - 2026-03-08

### Added
- Analyze .NET assembly sizes with per-type breakdown and tree output
- Namespace filtering with `--namespace` for focused analysis
- Size unit selection with `--unit` (auto, bytes, KB, MB)
- JSON output with `--json` flag for machine-readable results
- CI and Release GitHub workflows
- Remote installer and `install-local.ps1` script
- Dependabot configuration
