# IfcEnvelopeMapper

[![build](https://github.com/jeffchicarelli/ifc-envelope-mapper/actions/workflows/build.yml/badge.svg)](https://github.com/jeffchicarelli/ifc-envelope-mapper/actions/workflows/build.yml)

C#/.NET 8 CLI tool that extracts the envelope and facades of a building from an IFC file using only 3D geometry. IFC *properties* (e.g. `Pset_WallCommon.IsExternal`) may be consulted as *hints*, but are never a pre-requisite for classification.

Practical component of the MBA in Software Engineering TCC (USP/Esalq), defense scheduled for April 2027. Full technical design is documented in [docs/plano.md](docs/plano.md) (in Portuguese).

## Prerequisites

- [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- Windows x64 (xBIM Toolkit ships native DLLs for this platform only)
- An IFC file in `data/models/` (a `duplex.ifc` sample is bundled and used by the current CLI)

## Build and test

```bash
dotnet build --configuration Release
dotnet test  --no-build --configuration Release
```

At the time of writing: 7 projects, 64 unit tests, all green on CI.

## Run the CLI

### Standard path (any local disk)

```bash
dotnet run --project src/IfcEnvelopeMapper.Cli
```

### When the repo lives on Google Drive Streaming

xBIM's native DLLs crash when loaded from a Google Drive Streaming path. The bundled script copies binaries and models to `C:\temp\ifcenvmapper\` and runs the CLI from there:

```powershell
pwsh scripts/run-from-temp.ps1
```

## Project layout

```
src/
  IfcEnvelopeMapper.Core/         Domain types and interfaces (Element/, Surface/, Loading/, Detection/, Grouping/)
  IfcEnvelopeMapper.Geometry/     Geometric operations on DMesh3 (plane fitting, etc.)
  IfcEnvelopeMapper.Algorithms/   Envelope detection and facade grouping strategies
  IfcEnvelopeMapper.Ifc/          xBIM adapter: implements IModelLoader
  IfcEnvelopeMapper.Lod/          LoD generators (ADR-15) — scaffolded
  IfcEnvelopeMapper.Debug/        GeometryDebug API + GLB writer + local HTTP viewer server (ADR-17)
  IfcEnvelopeMapper.Cli/          System.CommandLine executable
  IfcEnvelopeMapper.Viewer/       Web viewer (Blazor + three.js) — scaffolded
tools/
  debug-viewer/                   three.js glTF viewer served by DebugViewerServer (ADR-17, Camada B)
tests/
  IfcEnvelopeMapper.Tests/        xUnit + FluentAssertions
docs/
  plano.md                        Technical design, ADRs, and roadmap (PT-BR)
data/
  models/                         Input IFC files
scripts/
  run-from-temp.ps1               Google Drive Streaming workaround
```

Dependency direction is unidirectional: `Core` does not depend on `Ifc`; `Algorithms` does not depend on `Cli`. Anything that touches xBIM lives inside `IfcEnvelopeMapper.Ifc`.

## Documentation

- [docs/plano.md](docs/plano.md) — full technical plan (architecture, ADRs, algorithms, JSON report schema)
- [2027-TCC/](../2027-TCC) — dissertation, methodology, and references (separate repo)
