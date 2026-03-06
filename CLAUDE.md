# CLAUDE.md

## Key Design Decisions

- Metadata sizes are **estimates** — per-type metadata is computed heuristically, then unaccounted metadata (from PE headers) is distributed proportionally across types
- `--namespace` filter is **display-only** — the tree is built from all types so parent nodes show full-assembly sizes; only nodes on the filter path are rendered; ancestor nodes skip depth/min-size checks
- `samples/` contains pre-built assemblies for manual testing
