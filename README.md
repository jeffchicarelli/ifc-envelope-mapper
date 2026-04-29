# IfcEnvelopeMapper

[![build](https://github.com/jeffchicarelli/ifc-envelope-mapper/actions/workflows/build.yml/badge.svg)](https://github.com/jeffchicarelli/ifc-envelope-mapper/actions/workflows/build.yml)

C#/.NET 8 CLI tool that extracts the envelope and facades of a building from an IFC file using only 3D geometry. IFC *properties* (e.g. `Pset_WallCommon.IsExternal`) may be consulted as *hints*, but are never a pre-requisite for classification.

Practical component of the MBA in Software Engineering TCC (USP/Esalq), defense scheduled for April 2027. Full technical design is documented in [docs/plano.md](docs/plano.md) (in Portuguese).

## What it does

Given an IFC model, the CLI computes:

- **Envelope** — exterior face regions of the building, traceable to each contributing IFC element.
- **Facades** — exterior faces grouped by dominant orientation (DBSCAN over the Gauss sphere + spatial connectivity), with per-facade WWR (window-to-wall ratio).
- **Multi-LoD outputs** — 2D footprint per storey, block model, roof shell, interior space classification, voxel grid (Biljecki 2016 / van der Vaart 2025 LoD framework).
- **Topological features** — airwells, light shafts, recesses, atria, setbacks, overhangs, cantilevers — derived as queries on the LoD layers.

The detection step is geometry-first: IFC properties such as `Pset_WallCommon.IsExternal` may inform but never gate classification.

### Detection strategies

Three strategies are compared on the same fixtures with TP/FP/FN/TN + Precision/Recall:

| Strategy | Role | Reference |
|---|---|---|
| Voxel uniform + 3-phase flood-fill | Ablation baseline | van der Vaart (2022) |
| Ray casting per face | External baseline | Ying et al. (2022) |
| Hierarchical voxel flood-fill | Original contribution | this work |

## Prerequisites

- [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- Windows x64 (xBIM Toolkit ships native DLLs for this platform only)
- An IFC file in `data/models/` (a `duplex.ifc` sample is bundled and used by the current CLI)

## Build and test

```bash
dotnet build --configuration Release
dotnet test  --no-build --configuration Release
```

## Run the CLI

### Standard path (any local disk)

```bash
dotnet run --project src/Cli
```

### When the repo lives on Google Drive Streaming

xBIM's native DLLs crash when loaded from a Google Drive Streaming path. The bundled script copies binaries and models to `C:\temp\ifcenvmapper\` and runs the CLI from there:

```powershell
pwsh scripts/run-from-temp.ps1
```

## Output formats

| Output | Format | Purpose |
|---|---|---|
| JSON report | Custom JSON v3 | Machine-readable analysis with per-LoD blocks and topological features |
| BCF | BCF 2.1 (zip XML) | Visual review with viewpoints in BIMcollab / Solibri / Revit Issues |
| Enriched IFC | IFC4 + custom Pset + IfcGroup | Round-trip back into Revit, ArchiCAD, BlenderBIM |
| GLB | glTF binary | Visualisation in browser-based viewers |

The original IFC is never modified in place — the enriched IFC is written as `*_enriched.ifc` next to the input.

## Project layout

Project folders use short names; assembly names and namespaces keep the `IfcEnvelopeMapper.*` prefix.

| Path | Role |
|---|---|
| `src/Domain/` | Pure business model — no xBIM. Depends on `geometry4Sharp` only. |
| `src/Application/` | Use-case orchestration, ports, report builders. Depends on `Domain`. |
| `src/Infrastructure/` | xBIM adapters, detection algorithms, persistence, debug visualisation. |
| `src/Cli/` | DI composition root + `System.CommandLine` entry point. |
| `src/DebugServer/` | Standalone EXE (ADR-17). Out-of-process HTTP viewer; spawned at runtime, not a managed reference. |
| `tests/Domain.Tests/` | Pure unit tests — no xBIM, no IFC files. |
| `tests/Infrastructure.Tests/` | Unit tests requiring xBIM and IFC fixtures. |
| `tests/Integration.Tests/` | End-to-end (AirwellDetection, StrategyComparison). |
| `tools/debug-viewer/` | three.js glTF viewer served by `DebugServer`. |
| `docs/plano.md` | Technical design, ADRs, phases (PT-BR). |
| `data/models/` | Input IFC files. |
| `scripts/run-from-temp.ps1` | Google Drive Streaming workaround. |

Dependency direction is unidirectional and acyclic:

```
Cli ──► Application ──► Domain
  └──► Infrastructure ──► Domain
                     └──► Application
```

`Domain` holds the pure business model with no external library dependencies beyond `geometry4Sharp`. `Application` orchestrates use cases without touching xBIM. `Infrastructure` owns everything that requires xBIM, algorithm implementations, file I/O, and GLB visualisation. `Cli` is the DI composition root and thin entry point. Per-class breakdown lives in [docs/plano.md §4](docs/plano.md).

## Documentation

- [docs/plano.md](docs/plano.md) — full technical plan: architecture, ADRs, algorithms, JSON v3 schema, phase plan

### Academic positioning

Closest prior work is **van der Vaart, Arroyo Ohori & Stoter (2025)**'s `IFC_BuildingEnvExtractor` (TU Delft 3D Geoinformation Group), which produces multi-LoD CityJSON output. This work preserves `IElement` references at every LoD and adds a topological taxonomy of building features (airwell, light shaft, recess) tied to the LoD framework. Full positioning in [docs/plano.md §10](docs/plano.md).
