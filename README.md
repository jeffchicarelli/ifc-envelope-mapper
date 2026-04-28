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
  Domain/                         Pure business model — no xBIM dependency.
    Interfaces/                     IElement (+ IIfcEntity, IBoxEntity, IMeshEntity).
    Surface/                        Envelope, Facade, Face.
    Voxel/                          VoxelGrid3D, VoxelCoord, VoxelState.
    Detection/                      DetectionResult, ElementClassification, StrategyConfig.
    Evaluation/                     DetectionCounts, EvaluationResult, GroundTruthRecord.
    Services/                       IEnvelopeDetector, IFaceExtractor, IFacadeGrouper.
    Extensions/                     6 math extension classes (DMesh3, Plane3d, Vector3d,
                                    AxisAlignedBox3d, VoxelGrid3D, IBoxEntity).
  Application/                    Orchestration — depends on Domain only.
    Ports/                          IModelLoader, ModelLoadResult, IBcfWriter,
                                    IJsonReportWriter, IGroundTruthReader.
    Reports/                        JsonReportBuilder, BcfBuilder, DetectionReport, BcfPackage.
    Evaluation/                     EvaluationService, MetricsCalculator.
  Infrastructure/                 xBIM adapters, detection algorithms, persistence.
    Ifc/                            Element, Storey, IfcProductContext, IProductEntity.
    Ifc/Loading/                    XbimModelLoader, ElementFilter,
                                    IfcLoadException, IfcGeometryException.
    Detection/                      VoxelFloodFillDetector, RayCastingDetector,
                                    PcaFaceExtractor, DbscanFacadeGrouper.
    Persistence/                    JsonReportWriter, BcfWriter,
                                    GroundTruthCsvReader, GroundTruthGenerator.
    Visualization/                  GeometryDebug ([Conditional("DEBUG")]), Scene,
                                    ViewerHelper, GltfSerializer, DebugShape, Color,
                                    IfcTypePalette, VoxelOccupants, AtomicFile.
    Diagnostics/                    AppLog.
  DebugServer/                    Standalone EXE — out-of-process HTTP viewer (ADR-17).
                                  Spawned by Infrastructure.Visualization.ViewerHelper; not a managed reference.
  Cli/                            DI wiring + System.CommandLine entry point.
tools/
  debug-viewer/                   three.js glTF viewer served by DebugServer (ADR-17).
tests/
  Domain.Tests/                   Pure unit tests — no xBIM, no IFC files.
  Infrastructure.Tests/           Unit tests requiring xBIM and IFC fixtures.
  Integration.Tests/              End-to-end — AirwellDetection, StrategyComparison.
docs/
  plano.md                        Technical design, ADRs, and roadmap (PT-BR).
data/
  models/                         Input IFC files.
scripts/
  run-from-temp.ps1               Google Drive Streaming workaround.
```

Dependency direction is unidirectional and acyclic:

```
Cli ──► Application ──► Domain
  └──► Infrastructure ──► Domain
                     └──► Application

DebugServer is process-spawned by Infrastructure.Visualization; not a managed dependency.
```

`Domain` holds the pure business model with no external library dependencies beyond `geometry4Sharp`. `Application` orchestrates use cases without touching xBIM. `Infrastructure` owns everything that requires xBIM, algorithm implementations, file I/O, and GLB visualization. `Cli` is the DI composition root and thin entry point.

## Documentation

- [docs/plano.md](docs/plano.md) — full technical plan (architecture, ADRs, algorithms, JSON report schema)
- [2027-TCC/](../2027-TCC) — dissertation, methodology, and references (separate repo)
