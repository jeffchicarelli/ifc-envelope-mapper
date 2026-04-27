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

At the time of writing: 5 src projects + 1 test project, 66 tests (64 unit + 2 integration), all green on CI.

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

## Project layout

Project folders use short names; assembly names and namespaces keep the `IfcEnvelopeMapper.*` prefix via `RootNamespace` / `AssemblyName` settings.

```
src/
  Core/                           Domain types, pipeline interfaces, math primitives, extensions.
    Domain/                         Element/ (BuildingElement, Group, Context),
                                    Surface/ (Envelope, Facade, Face),
                                    Voxel/   (VoxelGrid3D, VoxelCoord, VoxelState).
    Pipeline/                       Loading/   (ModelLoadResult, DefaultElementFilter),
                                    Detection/ (IDetectionStrategy, IFaceExtractor, DetectionResult, ElementClassification),
                                    Evaluation/ (DetectionCounts, GroundTruthRecord, EvaluationResult,
                                                 MetricsCalculator, GroundTruthCsvReader).
    Extensions/                     6 extension classes — BuildingElement.BoundingBox(),
                                    Vector3d.FitPlane()/.ToSphere(), DMesh3.Merge()/.ExtractTriangles(),
                                    Plane3d.ToQuadMesh(), VoxelGrid3D.CubesAt(),
                                    AxisAlignedBox3d.ToCube()/.ToWireframe().
  Engine/                         Detection algorithms + visualization.
    Strategies/                     PcaFaceExtractor, VoxelFloodFillStrategy.
    Visualization/                  GeometryDebug ([Conditional("DEBUG")]), Scene,
                                    ViewerHelper, GltfSerializer, DebugShape, Color,
                                    IfcTypePalette, VoxelOccupants, AtomicFile.
  Ifc/                            xBIM adapter.
    Loading/                        XbimModelLoader, IfcLoadException, IfcGeometryException.
    Resolver/                       XbimIfcProductResolver.
    Evaluation/                     GroundTruthGenerator, EvaluationPipeline.
  DebugServer/                    Standalone EXE — out-of-process HTTP viewer (ADR-17).
                                  Spawned by Engine.Visualization.ViewerHelper; not a managed reference.
  Cli/                            System.CommandLine entry point — thin wrapper around EvaluationPipeline.
tools/
  debug-viewer/                   three.js glTF viewer served by DebugServer (ADR-17).
tests/
  Tests/                          xUnit + FluentAssertions. 64 unit + 2 integration tests.
docs/
  plano.md                        Technical design, ADRs, and roadmap (PT-BR).
data/
  models/                         Input IFC files.
scripts/
  run-from-temp.ps1               Google Drive Streaming workaround.
```

Dependency direction is unidirectional and acyclic:

```
       Core (leaf — domain + math + extensions)
       ↑       ↑
       |       |
     Ifc     Engine (strategies + visualization)
       ↑       ↑
       └───┬───┘
           |
          Cli

DebugServer is process-spawned by Engine.Visualization, not a managed dependency.
```

Everything domain-shaped or math-shaped lives in `Core`. Anything that touches xBIM lives in `Ifc`. Detection strategies and the GLB-writing visualization stack live in `Engine`. `Cli` is the thin entry point; `DebugServer` is the standalone helper process.

## Documentation

- [docs/plano.md](docs/plano.md) — full technical plan (architecture, ADRs, algorithms, JSON report schema)
- [2027-TCC/](../2027-TCC) — dissertation, methodology, and references (separate repo)
