# Plano de Implementação — IfcEnvelopeMapper

> Documento vivo. Atualizar a cada sessão de desenvolvimento.
> Última atualização: 2026-04-19

---

## Conceitos Fundamentais e Terminologia

Este documento pressupõe familiaridade com os termos abaixo. Leitores sem formação em AEC devem consultá-los antes de prosseguir.

| Termo | Definição |
|-------|-----------|
| **IFC** | Industry Foundation Classes — padrão aberto ISO 16739 para intercâmbio de dados BIM. Modelos IFC descrevem geometria, atributos e relações de elementos de uma edificação em formato neutro de software. |
| **BIM** | Building Information Modelling — metodologia de representação digital integrada de uma edificação, associando geometria 3D a metadados de projeto, construção e operação. |
| **xBIM** | Xbim Toolkit — biblioteca open-source .NET para leitura, escrita e consulta de modelos IFC. |
| **BCF** | BIM Collaboration Format — formato de marcação de modelos BIM utilizado para comunicar observações, revisões e resultados sem modificar o arquivo IFC original. |
| **Envoltório** *(building envelope)* | Conjunto de todas as superfícies exteriores de uma edificação que separam o ambiente interior do externo, em todas as orientações (paredes, coberturas, pisos, aberturas). Definido funcionalmente pela separação interior/exterior (ASHRAE 90.1; Sadineni et al., 2011). Não existe como entidade no schema IFC. |
| **Casca geométrica** *(building shell)* | Superfície envolvente computada por operações geométricas (voxelização, alpha wrapping). Artefato geométrico puro, sem rastreabilidade aos elementos IFC de origem. |
| **Fachada** *(facade)* | Região contínua da superfície exterior do envoltório, caracterizada por uma orientação dominante (vetor normal médio das faces convergentes). Fachada é artefato da superfície, não um agrupamento de elementos. Elementos IFC são *participantes* (relação muitos-para-muitos): um elemento de canto participa de duas fachadas. Inclui qualquer orientação — sem limiar angular arbitrário. Não existe no schema IFC; é inferida computacionalmente (ver `conceitos-fundamentais.md`). |
| **Face** | Unidade atômica de superfície exterior: conjunto de triângulos de um elemento IFC que pertencem a um mesmo plano ajustado. Preserva rastreabilidade ao `BuildingElement` de origem. |
| **Plano dominante** | Direção média de um grupo de normais detectado por DBSCAN sobre a esfera de Gauss. Base para o agrupamento de elementos em fachadas. |
| **Ground truth** | Conjunto de rótulos de referência (elementos marcados como fachada / não-fachada) produzido por rotulação manual de especialistas AEC. Base para contagens TP/FP/FN/TN e cálculo de Precisão e Recall. |
| **TP / FP / FN / TN** | Contagens da matriz de confusão de classificação binária. TP (True Positive): classificados como exterior e realmente exteriores. FP (False Positive): classificados como exterior mas são interiores. FN (False Negative): exteriores não detectados. TN (True Negative): interiores corretamente identificados. Estilo de reporte seguindo van der Vaart (2022). |
| **Precisão / Recall** | Métricas derivadas das contagens. Precisão = TP / (TP + FP): dos classificados como exterior, quantos realmente são. Recall = TP / (TP + FN): dos que são exterior, quantos foram encontrados. Definições conforme Ying et al. (2022, Eq. 12–13). F1 e Kappa foram descartados como métricas principais por não aparecerem nas referências canônicas — ver ADR-12 nota. |
| **DBSCAN** | Density-Based Spatial Clustering of Applications with Noise — algoritmo de clustering sem número fixo de grupos. Usado para agrupar normais de faces na esfera de Gauss e detectar planos dominantes. |
| **BVH** | Bounding Volume Hierarchy — estrutura de aceleração espacial para ray casting. |
| **WWR** | Window-to-Wall Ratio — razão entre área de janelas e área total de parede por fachada. Métrica usada como prova de aplicabilidade do método. |

---

## Objetivo

Construir uma ferramenta C#/.NET que identifica automaticamente elementos de fachada em modelos IFC usando **apenas geometria 3D** — sem depender de propriedades ou metadados do modelo.

O trabalho propõe **um método computacional**, avaliado rigorosamente em modelos IFC de diferentes tipologias. O método implementa uma estratégia de produção única (`VoxelFloodFillStrategy` — van der Vaart 2022, com cascata 4-testes + 3 fases flood-fill + `FillGaps`) e mantém `RayCastingStrategy` (Ying 2022) implementada exclusivamente como baseline de comparação no capítulo de Resultados. Decisão fundamentada em ADR-14 (que superseda ADR-12 parcialmente).

---

## Stack de Tecnologias

### Linguagem e Runtime

- **C# / .NET 8** — stack profissional do Jeff; xBIM é .NET nativo
- **xUnit + FluentAssertions** — framework de testes

### Bibliotecas Externas

| Biblioteca | NuGet Package | Uso | Projeto |
|---|---|---|---|
| **xBIM Essentials** | `Xbim.Essentials` | Leitura de modelos IFC, schema IFC4 | Ifc |
| **xBIM Geometry** | `Xbim.Geometry` | Triangulação de geometria IFC via `Xbim3DModelContext` | Ifc |
| **geometry4Sharp** | `geometry4Sharp` | Mesh 3D (`DMesh3`), BVH (`DMeshAABBTree3`), normais (`MeshNormals`), plane-fit PCA (`OrthogonalPlaneFit3`), eigen (`SymmetricEigenSolver`), esfera de Gauss (`NormalHistogram`) — namespace `g4`; fork ativo de `geometry3Sharp`. Tri-AABB ausente — implementado via SAT próprio (Akenine-Möller 1997). Mapeamento completo em ADR-13. | Core + Geometry + Algorithms |
| **NetTopologySuite** | `NetTopologySuite` | Geometria **2D apenas** (containment, projeção em plano, `STRtree` 2D para união de polígonos no LoD 0, ADR-15). Não é usado para indexação 3D — ver ADR-13 para queries 3D. | Geometry + Lod |
| **DBSCAN** | `DBSCAN` (NuGet) | Clustering de normais sobre a esfera de Gauss | Algorithms |
| **QuikGraph** | `QuikGraph` | Grafo de adjacência espacial, componentes conectados | Algorithms |
| **SharpGLTF** | `SharpGLTF.Toolkit` | Escrita de glTF (scenes, nodes, per-vertex color, extras) para debug visual. Padrão standard: qualquer browser/CloudCompare/Blender lê. (ADR-16) | Debug |
| **System.CommandLine** | `System.CommandLine` | Parser de argumentos CLI | Cli |

**Política de bibliotecas:** usar bibliotecas externas agora e substituir por implementação própria somente se uma biblioteca não for extensivamente utilizada no projeto. Não prematuramente otimizar.

---

## Modelo de Domínio

### Hierarquia conceitual

```
ModelLoadResult
    ├── Elements[]     ← átomos classificáveis (sempre com geometria)
    └── Groups[]       ← agregadores organizacionais (IfcCurtainWall, IfcStair…)

Envelope (totalidade das faces exteriores com rastreabilidade)
    └── input para →
        Facade[] (região de superfície por plano dominante)
            └── Face[] (superfície atômica exterior — unidade primária)
                └── BuildingElement (rastreável ao IFC via GlobalId)

Relação Facade ↔ BuildingElement: MUITOS-PARA-MUITOS
  - Uma Face pertence a exatamente 1 BuildingElement e 1 Facade
  - Um BuildingElement pode ter Faces em 0, 1 ou N Facades
  - Uma Facade agrega Faces de M BuildingElements diferentes

Relação BuildingElement ↔ BuildingElementGroup: MUITOS-PARA-UM (opcional)
  - Um Element que veio de um agregador IFC tem GroupGlobalId preenchido
  - Um Group referencia seus Elements via lista
  - Algoritmos consomem apenas Elements; Groups servem a relatório e Viewer
```

> **ModelLoadResult** é o que o loader retorna — separa átomos (o que se classifica) de agregadores (o que se usa para rastreabilidade de IfcCurtainWall/IfcStair).
> **Envelope** não contém `Facade[]` — é input para o `IFacadeGrouper`, que produz `Facade[]`.
> **Facade** referencia Envelope (parent) e contém um subconjunto de `Face[]`.
> **Facade.Elements** retorna os elementos que possuem ≥1 Face nesta região.

### BuildingElementContext — record struct (ADR-08)

> Implementação em `src/IfcEnvelopeMapper.Core/Element/BuildingElementContext.cs`

### BuildingElement — átomo classificável (ADR-08, ADR-11)

> Implementação em `src/IfcEnvelopeMapper.Core/Element/BuildingElement.cs`

**Por que anêmico (sem `BoundingBox`/`Centroid` cacheados)?** Simplicidade e imutabilidade por construção. Quem precisa, chama `element.Mesh.GetBounds().Center` no ponto de uso — `geometry4Sharp` já caminha o mesh uma vez e o custo é negligível frente ao DBSCAN/ray casting.

**Por que `IEquatable<BuildingElement>` por `GlobalId`?** Usar `HashSet<BuildingElement>`, `Distinct()` e `Dictionary<BuildingElement, T>` sem lambdas de key selector. `GlobalId` é identidade natural do IFC.

**Por que `required init` e não construtor?** Construção por *object initializer* deixa os testes legíveis (`new BuildingElement { GlobalId = "…", IfcType = "IfcWall", Mesh = mesh }`) e obriga cada campo a ser fornecido. `readonly record struct` em `Context` permite defaults nulos sem boilerplate.

**Por que `sealed class` e não `record`?** `DMesh3` não implementa value equality — `record` geraria equality sintética que compara `Mesh` por referência. Equality aqui é por identidade IFC (`GlobalId`), então implementamos explicitamente.

### BuildingElementGroup — agregador organizacional (ADR-11)

> Implementação em `src/IfcEnvelopeMapper.Core/Element/BuildingElementGroup.cs`

**Por que separar `Element` e `Group`?** Um modelo único com `Mesh` opcional + `Children[]` opcional cria estados inválidos (átomo com children, agregador sem children). O split elimina isso por construção: `Element` sempre tem mesh, `Group` sempre tem `Elements` não-vazia.

### ModelLoadResult

> Implementação em `src/IfcEnvelopeMapper.Core/Loading/ModelLoadResult.cs`

### Face — superfície atômica exterior (ADR-04)

> Implementação em `src/IfcEnvelopeMapper.Core/Surface/Face.cs`

**Rastreabilidade sem duplicação:** `Face` não armazena `DMesh3`. Os triângulos são lidos por `Element.Mesh.GetTriangle(id)` para cada `id in TriangleIds`. `face.Element.GlobalId` já dá o link ao IFC sem lookup externo.

### Envelope — casca + faces exteriores

> Implementação em `src/IfcEnvelopeMapper.Core/Surface/Envelope.cs`

### Facade — região de superfície por plano dominante

> Implementação em `src/IfcEnvelopeMapper.Core/Surface/Facade.cs`

### Interfaces do pipeline

> Implementações em `src/IfcEnvelopeMapper.Core/Loading/` (`IModelLoader`, `IElementFilter`, `DefaultElementFilter`), `Detection/` (`IDetectionStrategy`), `Grouping/` (`IFacadeGrouper`)

### Acesso cru ao IIfcProduct (ADR-10)

Mora na camada Ifc. Viewer, Cli e testes importam quando precisam de metadados IFC não previstos em `BuildingElementContext` (properties de `Pset_*`, material, tag, relações como `IfcRelConnectsPathElements`):

> Implementações em `src/IfcEnvelopeMapper.Ifc/Resolver/IIfcProductResolver.cs` e `XbimIfcProductResolver.cs`

Index em `Dictionary` evita busca linear em modelos com milhares de elementos. Lifetime: o resolver precisa do `IfcStore` aberto; gerenciar via `using` ou escopo de DI.

### Reporting

> Implementações em `src/IfcEnvelopeMapper.Core/Detection/DetectionResult.cs` e `ElementClassification.cs`

---

## Estrutura do Projeto (8 projetos + testes)

```
IfcEnvelopeMapper/
├── docs/
│   └── plano.md                          ← este arquivo
├── scripts/
│   └── run-from-temp.ps1                 ← workaround Google Drive Streaming (xBIM native DLLs)
├── tools/
│   └── debug-viewer/                     ← HTML + three.js local (ADR-16, Fase 3)
│       └── index.html
│
├── src/
│   ├── IfcEnvelopeMapper.Core/           ← domínio puro + interfaces (ports)
│   │   ├── Element/                      ← átomos e agregadores
│   │   │   ├── BuildingElement.cs        ← átomo classificável (ADR-11)
│   │   │   ├── BuildingElementGroup.cs   ← agregador organizacional (ADR-11)
│   │   │   └── BuildingElementContext.cs ← record struct: Site/Building/Storey IDs (ADR-08)
│   │   ├── Surface/                      ← superfícies inferidas (Envelope, Facade, Face)
│   │   │   ├── Envelope.cs
│   │   │   ├── Facade.cs
│   │   │   └── Face.cs                   ← Element + TriangleIds + FittedPlane (ADR-04)
│   │   ├── Loading/                      ← carregamento e filtragem
│   │   │   ├── IModelLoader.cs
│   │   │   ├── ModelLoadResult.cs        ← record (Elements, Groups) (ADR-11)
│   │   │   ├── IElementFilter.cs         ← filtro de tipos IFC (ADR-05)
│   │   │   └── DefaultElementFilter.cs
│   │   ├── Detection/                    ← Stage 1 — detecção + extração de faces
│   │   │   ├── IDetectionStrategy.cs
│   │   │   ├── IFaceExtractor.cs         ← BuildingElement → Face[] (PCA coplanar)
│   │   │   ├── DetectionResult.cs
│   │   │   └── ElementClassification.cs
│   │   ├── Grouping/                     ← Stage 2 — agrupamento em fachadas
│   │   │   └── IFacadeGrouper.cs
│   │   [deps: geometry4Sharp]
│   │
│   ├── IfcEnvelopeMapper.Geometry/       ← operações geométricas stateless
│   │   ├── GeometricOps.cs
│   │   └── Debug/
│   │       └── GeometryDebug.cs         ← debugger geométrico (#if DEBUG) (ADR-17)
│   │   [deps: Core, geometry4Sharp, NetTopologySuite]
│   │
│   ├── IfcEnvelopeMapper.Ifc/            ← integração xBIM
│   │   ├── Loading/
│   │   │   ├── XbimModelLoader.cs        ← implementa IModelLoader
│   │   │   ├── IfcLoadException.cs
│   │   │   └── IfcGeometryException.cs
│   │   └── Resolver/
│   │       ├── IIfcProductResolver.cs    ← acesso cru ao IIfcProduct (ADR-10)
│   │       └── XbimIfcProductResolver.cs
│   │   [deps: Core, Xbim.Essentials, Xbim.Geometry]
│   │
│   ├── IfcEnvelopeMapper.Algorithms/     ← estratégias de detecção + agrupamento
│   │   ├── Strategies/
│   │   │   ├── VoxelFloodFillStrategy.cs ← primária (ADR-14)
│   │   │   └── RayCastingStrategy.cs     ← baseline de comparação P4 (ADR-14)
│   │   └── Grouping/
│   │       └── DbscanFacadeGrouper.cs    ← implementa IFacadeGrouper
│   │   [deps: Core, Geometry, DBSCAN, QuikGraph]
│   │
│   ├── IfcEnvelopeMapper.Lod/            ← geradores de LoD (ADR-15, Biljecki/van der Vaart)
│   │   ├── ILodGenerator.cs              ← contrato: DetectionResult + Facade[] → LodOutput
│   │   ├── LodOutput.cs                  ← record (LodId, Semantic, Geometry, Provenance)
│   │   ├── LodRegistry.cs                ← resolve "3.2" → Lod32SemanticShellGenerator
│   │   ├── Footprint/
│   │   │   ├── Lod00FootprintXY.cs       ← projeção XY (não convex hull)
│   │   │   └── Lod02StoreyFootprints.cs
│   │   ├── Block/
│   │   │   ├── Lod10ExtrudedBbox.cs
│   │   │   └── Lod12StoreyBlocks.cs
│   │   ├── Roof/
│   │   │   └── Lod22DetailedRoofWallsStoreys.cs
│   │   ├── Facade/
│   │   │   └── Lod32SemanticShell.cs     ← core do TCC: Facade[] + Face semantic
│   │   └── Full/
│   │       ├── Lod40ElementWise.cs
│   │       ├── Lod41ExteriorElements.cs
│   │       └── Lod42MergedSurfaces.cs
│   │   [deps: Core, Geometry, NetTopologySuite]
│   │
│   ├── IfcEnvelopeMapper.Debug/          ← reservado para utilitários opcionais (e.g. glTF writer)
│   │   └── Placeholder.cs               ← GeometryDebug lives in IfcEnvelopeMapper.Geometry
│   │   [deps: Core]
│   │
│   ├── IfcEnvelopeMapper.Cli/            ← entry point, output writers
│   │   ├── Commands/
│   │   │   ├── DetectCommand.cs          ← orquestra o pipeline
│   │   │   └── DebugVoxelCommand.cs      ← dump voxel como PLY/OBJ (Fase 2)
│   │   ├── Output/
│   │   │   ├── JsonReportWriter.cs       ← usa ILodGenerator por --lod
│   │   │   └── BcfWriter.cs              ← mantido em paralelo ao Viewer (ADR-06)
│   │   └── Program.cs
│   │   [deps: Core, Ifc, Algorithms, Lod, Debug, System.CommandLine, Microsoft.Extensions.Logging]
│   │
│   └── IfcEnvelopeMapper.Viewer/         ← visualizador web Blazor + three.js (ADR-07 — stretch)
│       ├── Components/                   ← render 3D, inspeção por elemento
│       ├── Editing/                      ← edição manual de rotulação (isolada do Core)
│       └── Export/                       ← BCF export (via iabi.BCF ou equivalente)
│       [deps: Core, Ifc, Algorithms, iabi.BCF]
│       [nota: ADR-07 ainda stretch goal; decisão em Fase 7 sobre possível absorção pelo debug-viewer — ver ADR-07]
│
├── tests/
│   └── IfcEnvelopeMapper.Tests/          ← xUnit + FluentAssertions
│       ├── Core/
│       │   ├── Detection/               ← DetectionResult, ElementClassification
│       │   ├── Element/                 ← BuildingElement, Group, Context
│       │   ├── Loading/                 ← DefaultElementFilter
│       │   └── Surface/                 ← Face, Envelope, Facade
│       ├── Ifc/
│       │   └── Loading/                 ← XbimModelLoader (integração)
│       ├── Algorithms/                  ← strategies + grouper implementations
│       ├── Lod/                         ← geradores por LoD level
│       └── Regression/                  ← snapshot tests (expected-report.json)
│
├── data/
│   ├── models/                           ← arquivos IFC para testes
│   ├── results/                          ← outputs JSON gerados pela CLI
│   ├── debug/                            ← glTF/PLY gerados pela flag --debug
│   └── ground-truth/                     ← rotulação manual por especialistas (CSV)
│
├── IfcEnvelopeMapper.slnx
└── README.md
```

### Por que 8 projetos?

`Core` concentra o domínio e as interfaces de pipeline — tudo sem depender de infraestrutura (exceto `geometry4Sharp` para tipos geométricos). `Geometry` isola operações geométricas puras, reutilizáveis entre strategies. `Ifc` encapsula toda a complexidade do xBIM — tanto o carregamento quanto o acesso ad-hoc a metadados IFC via `IIfcProductResolver` (ADR-10) —, e pode ser substituído por outra biblioteca de leitura IFC sem tocar o domínio. `Algorithms` contém as strategies e o agrupamento — a parte mais experimental do projeto. `Lod` (ADR-15) implementa os 10 geradores do framework Biljecki/van der Vaart — cada LoD é um `ILodGenerator` que consome `DetectionResult + Facade[]` e produz saída no formato natural daquele nível (Polygon 2D, DMesh3, voxel grid). `Debug` (ADR-17) é um placeholder reservado para utilitários opcionais de serialização futura — `GeometryDebug` (`#if DEBUG`) vive em `Geometry`. `Cli` é um dos dois pontos de entrada e o lugar dos writers de relatório (JSON + BCF) e do comando `debug-voxel` para LoD 5 via debug. `Viewer` é o segundo ponto de entrada: visualizador web que consome o mesmo JSON produzido pela CLI e permite render, inspeção, edição manual de rotulação e export BCF complementar (ADR-07 — stretch; decisão de absorção em Fase 5, ver ADR-16).

### Dependency Inversion

**`IModelLoader` fica em Core, não em Ifc.** A interface pertence ao consumidor, não ao provedor. `XbimModelLoader` implementa `IModelLoader` e fica em Ifc; Core não sabe que xBIM existe.

**`IFacadeGrouper` e `IDetectionStrategy` ficam em Core, não em Algorithms.** `DbscanFacadeGrouper` e as strategies implementam as interfaces e ficam em Algorithms.

**`IElementFilter` fica em Core** (ADR-05). `DefaultElementFilter` com lista padrão fica em Core; `XbimModelLoader` recebe a instância por construtor.

**`IIfcProductResolver` fica em Ifc, não em Core** (ADR-10). A interface existe para permitir que Viewer, Cli ou testes acessem o `IIfcProduct` cru sem acoplar Core ao xBIM — quem importa o resolver já depende de xBIM por definição.

**Sem `IReportWriter` em Core.** A CLI produz um `DetectionResult` e chama writers concretos. Nenhuma abstração é necessária neste ponto.

### Diagrama de dependências (sem circular)

```
Core ← Geometry ← Algorithms ──┐
Core ← Ifc ────────────────────┤
Core ← Lod ────────────────────┼─→ Cli, Viewer
Core ← Debug ──────────────────┘
Core ────────────────────────────
```

`Viewer` depende de `Core + Ifc + Algorithms + Lod + Debug` mas não é dependência de ninguém. `Cli` depende de todos (exceto Viewer). `Tests` depende de todos os projetos de `src/`. Debug geométrico é acessado diretamente via `GeometryDebug.Mesh(...)` etc. — sem configuração; `#if DEBUG` garante zero overhead em Release (ADR-17).

---

## Pipeline de Detecção em Dois Estágios

```
IFC Model
    │
    ▼
[XbimModelLoader — implements IModelLoader]
    │  IReadOnlyList<BuildingElement>
    ▼
[Stage 1 — IDetectionStrategy.Detect()]
    │  DetectionResult (Envelope + ElementClassification[])
    │
    │  Implementadas (ADR-14 — superseda ADR-12 parcialmente):
    │
    │  Primária: VoxelFloodFillStrategy (van der Vaart 2022 / Liu 2021)
    │    → discretiza modelo em voxel grid 3D (SAT triângulo-AABB — Akenine-Möller 1997)
    │    → cascata 4-testes de interseção voxel↔triângulo
    │    → 3 fases flood-fill: growExterior → growInterior → growVoid
    │    → FillGaps pós-processamento (robustez em meshes imperfeitas)
    │    → configurável (--voxel-size)
    │
    │  Baseline de comparação: RayCastingStrategy (Ying 2022) — exclusivo P4
    │    → BVH global (g4.DMeshAABBTree3)
    │    → raio por face na direção da normal (com jitter)
    │    → face exposta = raio escapa sem interceptar outro elemento
    │    → configurável (--ray-count, --hit-ratio)
    │    → propósito: comparação algorítmica no capítulo de Resultados
    │
    ▼
[Stage 2 — IFacadeGrouper.Group(envelope)]
    │
    ├─ DbscanFacadeGrouper
    │    → DBSCAN sobre normais das faces exteriores
    │    → sobre a esfera de Gauss (ε e minPoints configuráveis)
    │    → cada cluster = um plano dominante candidato
    │    │
    │    └─ QuikGraph (adjacência espacial)
    │         → para cada cluster DBSCAN, constrói grafo de adjacência
    │         → arestas = elementos com bounding boxes próximas
    │         → componentes conectados = Facades independentes
    │         → resolve: duas paredes norte em lados opostos = 2 Facades
    │
    ▼
Facade[]
    │
    ▼
[ReportBuilder.Build(result, facades, runMeta)]
    │
    ├─ JsonReportWriter → report.json
    └─ BcfWriter        → report.bcf  (opcional)
```

### Orquestração na CLI (sem FacadeDetector)

```csharp
// DetectCommand.cs — composition root
// GeometryDebug (#if DEBUG) está sempre disponível sem configuração (ADR-17)

var model    = loader.Load(modelPath);                        // IModelLoader → ModelLoadResult
var result   = strategy.Detect(model.Elements);               // IDetectionStrategy → DetectionResult
var facades  = grouper.Group(result.Envelope);                // IFacadeGrouper → Facade[]
var lodOutputs = options.Lods                                 // ILodGenerator[] (ADR-15)
                    .Select(id => registry.Resolve(id).Generate(result, facades))
                    .ToList();
var report   = ReportBuilder.Build(result, facades, lodOutputs, model.Groups, runMeta);
writer.WriteReports(report, outputPath);                      // 1 JSON por LoD + reports/
```

**Instrumentação de debug (ADR-17).** Strategies e grouper chamam `GeometryDebug.Mesh(...)`, `GeometryDebug.Voxels(...)` etc. diretamente — sem configuração, sem interfaces. Em Release builds, `#if DEBUG` elimina todas as chamadas pelo pré-processador. Em Debug builds, `Flush()` serializa para `%TEMP%\ifc-debug-output.gltf` (implementação SharpGLTF na Fase 2). Developer inspeciona com breakpoints no IDE.

**Por que sem `FacadeDetector`?** A CLI é a composition root e orquestra diretamente os dois estágios. Isto permite:
- Trocar strategy e grouper de forma independente
- Adicionar observabilidade (logging, timing) entre estágios
- Evitar classe coordenadora que apenas delega

**Por que DBSCAN + QuikGraph?** DBSCAN agrupa por orientação de normal mas não distingue duas superfícies desconexas com mesma orientação (ex: fachada norte frontal e fachada norte do poço de luz). QuikGraph resolve isso: dentro de cada cluster DBSCAN, o grafo de adjacência espacial separa superfícies fisicamente desconexas. Cada componente conectado é uma Facade distinta.

---

## Pseudocódigo Detalhado do Método

> Referências algorítmicas são indicadas onde técnicas publicadas fundamentam cada etapa.
> Para etapas sem referência direta — o clustering de normais sobre Gauss sphere para fachadas
> em IFC e a associação por participação muitos-para-muitos — estas constituem contribuição
> original deste trabalho.

### Estágio 0 — Carregamento e Triangulação

```
FUNÇÃO Load(ifcPath) → ModelLoadResult
    // Ref: xBIM Toolkit — Xbim3DModelContext (Lockley et al.)
    // ADR-05: filtro injetado por construtor (IElementFilter)
    // ADR-09: agregação IFC de building elements tem 2 níveis fixos
    // ADR-11: resultado separa átomos (Elements) de agregadores (Groups)

    model ← IfcStore.Open(ifcPath)
    context ← Xbim3DModelContext(model); context.MaxThreads = 1; context.CreateContext()
    // MaxThreads=1 evita AccessViolationException em OCCT (thread-unsafe teardown)

    elementos ← []    // átomos classificáveis
    grupos   ← []     // agregadores organizacionais

    PARA CADA ifcElem EM model.Instances.OfType<IIfcBuildingElement>():
        SE NOT filter.Include(ifcElem.GetType().Name): CONTINUE

        ctx ← ExtrairContext(ifcElem)    // (SiteId, BuildingId, StoreyId)
        children ← ifcElem.IsDecomposedBy
                         .SelectMany(r → r.RelatedObjects.OfType<IIfcBuildingElement>())
                         .Where(c → filter.Include(c.GetType().Name))
                         .ToList()

        SE children.Count == 0:
            // Átomo standalone — entra em Elements apenas se tem geometria.
            mesh ← ExtrairMesh(ifcElem, context)
            SE mesh.TriangleCount > 0:
                elementos.Add(new BuildingElement {
                    GlobalId = ifcElem.GlobalId,
                    IfcType  = ifcElem.GetType().Name,
                    Mesh     = mesh,
                    Context  = ctx
                })

        SENÃO:
            // Agregador (IfcCurtainWall, IfcStair, …).
            Debug.Assert(children.All(c → !c.IsDecomposedBy.Any()),
                "ADR-09: agregação de 3+ níveis não esperada")

            groupId ← ifcElem.GlobalId
            groupElements ← []

            PARA CADA child EM children:
                meshChild ← ExtrairMesh(child, context)
                SE meshChild.TriangleCount > 0:
                    elem ← new BuildingElement {
                        GlobalId       = child.GlobalId,
                        IfcType        = child.GetType().Name,
                        Mesh           = meshChild,
                        Context        = ExtrairContext(child),
                        GroupGlobalId  = groupId
                    }
                    elementos.Add(elem)
                    groupElements.Add(elem)
                SENÃO:
                    // Child sem geometria (ex: IfcCurtainWallPanel vazio) é descartado.
                    logger.Warning("Element {GlobalId} ({Type}) skipped: empty mesh",
                                   child.GlobalId, child.GetType().Name)

            // Agregador pode ou não ter geometria própria.
            ownMesh ← ExtrairMesh(ifcElem, context)
            grupos.Add(new BuildingElementGroup {
                GlobalId = groupId,
                IfcType  = ifcElem.GetType().Name,
                Context  = ctx,
                OwnMesh  = ownMesh.TriangleCount > 0 ? ownMesh : null,
                Elements = groupElements
            })

    RETORNAR new ModelLoadResult(elementos, grupos)
```

**Exemplo concreto.** Uma cortina de vidro em canto de prédio com 4 painéis voltados para norte e 3 para leste produz:
- `Elements`: 7 `BuildingElement`s (um por painel) + N mullions, todos com `GroupGlobalId = "curtainWall-1"`
- `Groups`: 1 `BuildingElementGroup` `"curtainWall-1"` (IfcCurtainWall, `OwnMesh = null`, `Elements` referenciando os 7+N)

O `DbscanFacadeGrouper` consome só `model.Elements` e classifica 4 painéis em Facade-Norte, 3 em Facade-Leste. O relatório JSON itera `model.Groups` para produzir `"aggregates": [{"globalId": "curtainWall-1", "participatingFacades": ["facade-N", "facade-E"]}]`.

### Estágio 1 — Detecção de Exterior (IDetectionStrategy)

O método implementa Voxel + Flood-Fill como estratégia primária (robustez em IFC real, referência canônica van der Vaart 2022) e Ray Casting como baseline de comparação (Ying 2022, caracteriza tradeoff precisão-vs-robustez no capítulo de Resultados). Normais foi descartada — ver ADR-14 que superseda ADR-12 parcialmente.

#### Estratégia 1A: Voxel + Flood-Fill (primária — ADR-14)

Arquitetura em 5 passos, alinhada ao IFC_BuildingEnvExtractor (`inc/voxelGrid.h`): cascata 4-testes para rasterização, 3 fases de flood-fill (`growExterior`/`growInterior`/`growVoid`), `FillGaps` pós-processamento para robustez em meshes com gaps/auto-interseções.

```
FUNÇÃO VoxelFloodFillDetect(elementos, tamanhoVoxel) → DetectionResult
    // Ref: van der Vaart (2022) — IFC_BuildingEnvExtractor
    // Ref: Liu et al. (2021) — ExteriorTag (anotação voxel em IFC)
    // Ref: Voxelization Toolkit (fill_gaps.h) — pós-processamento
    // SAT triângulo-AABB próprio (Akenine-Möller 1997) — g4.IntrTriangle3Box3 ausente em geometry4sharp

    // PASSO 1: Discretizar modelo em grade 3D
    bbox ← BoundingBoxGlobal(elementos) expandida por 2 * tamanhoVoxel
    grid ← VoxelGrid3D(bbox, tamanhoVoxel)

    // PASSO 2: Rasterizar — cascata 4-testes de interseção (van der Vaart 2022)
    //   Ordem barato→caro; bails out no primeiro hit
    PARA CADA elem EM elementos:
        PARA CADA tri EM elem.Mesh.Triangulos:
            voxelsCandidatos ← grid.VoxelsInBbox(tri.Bbox)
            PARA CADA v EM voxelsCandidatos:
                // (1) centro do voxel cai dentro do shape do produto?
                // (2) vértice do triângulo cai no voxel?
                // (3) aresta do triângulo cruza face do voxel?
                // (4) aresta do voxel cruza face do triângulo?
                SE TriangleIntersectsAabb(tri.v0, tri.v1, tri.v2, v.Box):
                    grid[v].Ocupado ← VERDADEIRO
                    grid[v].Elementos.Add(elem.GlobalId)   // provenance (ADR-04)

    // PASSO 3: Flood-fill em 3 fases (van der Vaart 2022)
    //   Fase A — growExterior: semente em canto do grid (garantido exterior)
    grid.GrowExterior(semente = canto, conectividade = 26)

    //   Fase B — growInterior: vazios não alcançados por Exterior,
    //   adjacentes a Ocupados → marcados como interior do edifício
    grid.GrowInterior()

    //   Fase C — growVoid: agrupa voxels interiores em cômodos (roomNum)
    //   permite distinguir paredes-meia de fachadas no reporting
    grid.GrowVoid()

    // PASSO 4: fill_gaps — fecha buracos de 1 voxel
    //   Ref: Voxelization Toolkit fill_gaps.h
    //   Robustez contra meshes com gaps/auto-interseções
    grid.FillGaps()

    // PASSO 5: Classificação — elemento com ≥1 face adjacente a voxel Exterior = exterior
    PARA CADA elem EM elementos:
        voxelsDoElemento ← grid.VoxelsOcupadosPor(elem.GlobalId)
        temFaceExterior ← FALSO
        PARA CADA v EM voxelsDoElemento:
            SE algum Vizinho26(v) tem Exterior == VERDADEIRO:
                temFaceExterior ← VERDADEIRO
                BREAK

        // Faces atômicas: triângulos cuja normal aponta para voxel Exterior,
        //   agrupados coplanarmente via g4.OrthogonalPlaneFit3 (ADR-13)
        facesAgrupadas ← ExtrairFacesVoltadasParaExterior(elem, grid)
        // Cada Face: {Element, TriangleIds, FittedPlane, Normal, Area, Centroid}
        GeometryDebug.Triangles(elem.Mesh, facesAgrupadas.SelectMany(f => f.TriangleIds))  // ADR-17

    RETORNAR DetectionResult(envelope, classificacoes)
```

#### Estratégia 1B: Ray Casting (baseline de comparação — ADR-14)

Propósito: comparação algorítmica no capítulo de Resultados — caracteriza tradeoff precisão face-por-face (raycast) vs robustez volumétrica (voxel). Implementada exclusivamente em P4; não faz parte do pipeline de produção.

```
FUNÇÃO RayCastDetect(elementos, numRaios, razaoHit) → DetectionResult
    // Ref: Ying et al. (2022) — two-stage recursive ray tracing
    // Ref: geometry4Sharp — DMeshAABBTree3 (BVH para ray-triangle intersection)

    // Construir BVH global com todos os triângulos do modelo
    meshGlobal ← MergeMeshes(elementos.Select(e → e.Mesh))
    bvh ← DMeshAABBTree3(meshGlobal)

    classificacoes ← []
    facesExteriores ← []

    PARA CADA elem EM elementos:
        PARA CADA tri EM elem.Mesh.Triangulos:
            centro ← tri.Centroide
            normal ← tri.Normal
            hitsExterior ← 0

            PARA i DE 1 ATÉ numRaios:
                // Emitir raio do centro na direção da normal (com jitter)
                direcao ← PerturbaDirecao(normal, jitter=5°)
                raio ← Ray3d(centro + normal * EPSILON, direcao)

                // Se o raio NÃO intercepta nenhum outro elemento → face exterior
                hit ← bvh.FindNearestHitTriangle(raio)
                SE hit NÃO EXISTE OU hit.Distancia > DISTANCIA_MAXIMA:
                    hitsExterior += 1

            razao ← hitsExterior / numRaios
            SE razao >= razaoHit:
                MARCAR tri como exterior

        facesAgrupadas ← AgruparPorPlanoAjustado(triangulosExteriores, elem)
        // ... agrupamento coplanar via g4.OrthogonalPlaneFit3 (ADR-13) ...

    RETORNAR DetectionResult(envelope, classificacoes)
```

### Estágio 2 — Agrupamento em Fachadas (IFacadeGrouper)

```
FUNÇÃO DbscanGroup(envelope) → Facade[]
    // Contribuição original: clustering de normais sobre a esfera de Gauss
    // para detectar planos dominantes de fachada em modelos IFC, com preservação
    // de rastreabilidade por relação de participação muitos-para-muitos.
    //
    // Ref (algoritmo DBSCAN): Ester & Kriegel (1996) — "A Density-Based Algorithm
    //   for Discovering Clusters in Large Spatial Databases with Noise"
    // Ref (DBSCAN sobre esfera): adaptação para espaço angular — distância geodésica
    //   entre vetores normais unitários

    faces ← envelope.Faces

    // PASSO 2.1: Projetar normais na esfera de Gauss
    //   Cada face gera um ponto na esfera unitária: sua normal normalizada
    //   Opção de pré-filtro (ADR-13): g4.NormalHistogram com SphericalFibonacciPointSet
    //   discretiza a esfera em N bins; clustering subsequente opera só em bins
    //   com contagem significativa. Avaliar em P5 se o ruído justificar.
    pontos ← faces.Select(f → f.Normal.Normalizado)

    // PASSO 2.2: DBSCAN com distância angular
    //   ε = tolerância angular (ex: 15°, convertido para radianos)
    //   minPts = mínimo de faces por cluster (ex: 3)
    //   Distância: arccos(dot(n1, n2)) — ângulo entre normais
    clusters ← DBSCAN(pontos, ε=anguloTolerancia, minPts=minFaces,
                       distancia=DistanciaAngular)

    // Faces ruidosas (sem cluster) são descartadas como "superfícies indefinidas"
    // e documentadas no relatório JSON para inspeção

    fachadas ← []
    PARA CADA cluster EM clusters:
        facesDoCluster ← faces filtradas pelo cluster

        // PASSO 2.3: Normal dominante
        normalDominante ← Média(facesDoCluster.Select(f → f.Normal)).Normalizado

        // PASSO 2.4: Grafo de adjacência espacial
        //   Ref: QuikGraph — componentes conectados em grafo não-direcionado
        //   Motivo: mesmo cluster de orientação pode conter superfícies fisicamente
        //   desconexas (ex: fachada norte frontal + fachada norte do poço de luz)
        grafo ← GrafoNaoDirecionado<Face>()
        PARA CADA par (f1, f2) EM facesDoCluster:
            SE ProximidadeEspacial(f1, f2):
                // Critério: bounding boxes se sobrepõem OU distância entre
                // centroides < limiar OU faces compartilham vértices
                grafo.AddAresta(f1, f2)

        // PASSO 2.5: Componentes conectados
        componentes ← ComponentesConectados(grafo)

        // PASSO 2.6: Cada componente = uma Facade
        PARA CADA comp EM componentes:
            azimute ← CalcularAzimute(normalDominante)
            fachada ← Facade(
                id: GerarId(azimute, indice),
                envelope: envelope,
                faces: comp.Faces,          // superfície — unidade primária
                dominantNormal: normalDominante,
                azimuth: azimute
            )
            // NOTA: fachada.Elements retorna os BuildingElements que
            // possuem ≥1 Face nesta região. Um elemento de canto aparecerá
            // em 2+ fachadas — comportamento correto (muitos-para-muitos).
            fachadas.Add(fachada)

    RETORNAR fachadas
```

### Estágio 3 — Relatório e Métricas

```
FUNÇÃO BuildReport(result, facades, groundTruth?) → JSON
    report ← {
        run: { model, strategy, grouper, timestamp, parameters },
        summary: { totalElements, exteriorElements, facadeCount },
        classifications: [],
        facades: []
    }

    // Classificações por elemento
    PARA CADA c EM result.Classifications:
        entry ← {
            globalId: c.Element.GlobalId,
            ifcType: c.Element.IfcType,
            computed: { isExterior: c.IsExterior },
            facadeIds: facades.Where(f → f.Elements.Contains(c.Element))
                             .Select(f → f.Id)   // pode ser múltiplos!
        }
        // Se IsExternal declarado disponível:
        entry.declared ← { isExternal: LerIsExternal(c.Element) }
        entry.agreement ← entry.computed.isExterior == entry.declared.isExternal
        report.classifications.Add(entry)

    // Fachadas com métricas
    PARA CADA f EM facades:
        wallArea ← f.Faces.Where(tipo ∈ {"IfcWall", "IfcCurtainWall"}).Sum(Area)
        windowArea ← f.Faces.Where(tipo == "IfcWindow").Sum(Area)
        entry ← {
            id: f.Id,
            dominantNormal: f.DominantNormal,
            azimuthDegrees: f.AzimuthDegrees,
            faceCount: f.Faces.Count,
            participantCount: f.Elements.Count(),
            metrics: { totalArea, wallArea, windowArea, wwr: windowArea/wallArea }
        }
        report.facades.Add(entry)

    // Se ground truth fornecido, calcular métricas
    SE groundTruth EXISTE:
        TP ← elementos classificados exterior E rotulados exterior
        FP ← elementos classificados exterior MAS rotulados interior
        FN ← elementos classificados interior MAS rotulados exterior
        precisao ← TP / (TP + FP)
        recall ← TP / (TP + FN)
        f1 ← 2 * precisao * recall / (precisao + recall)
        report.summary.precision ← precisao
        report.summary.recall ← recall
        report.summary.f1 ← f1

    RETORNAR report
```

---

## Tabela Comparativa das Estratégias de Detecção

> A decisão (ADR-14) é Voxel primária + RayCasting baseline. Esta tabela respalda
> a escolha e alimenta o capítulo de Resultados — comparação algorítmica entre as duas.

| Critério | Voxel + Flood-Fill (primária) | Ray Casting (baseline) |
|---|---|---|
| **Papel no método (ADR-14)** | Estratégia de produção | Comparação algorítmica no capítulo de Resultados |
| **Referência principal** | van der Vaart (2022); Liu et al. (2021) | Ying et al. (2022) |
| **Princípio** | Discretização em voxels + 3 fases flood-fill + classificação por adjacência exterior | Visibilidade: raio da face na direção da normal escapa sem interceptar outro elemento |
| **Complexidade temporal** | O(V) + O(n) — V voxels no grid, n triângulos para rasterização | O(n · k · log m) — k raios por face, log m para BVH |
| **Dependência de geometria global** | Alta — grid discreto do modelo inteiro | Alta — BVH com todos os triângulos |
| **Robustez a meshes malformados** | Alta — voxel contorna gaps, auto-interseções, topologia ruim (motivo da escolha) | Baixa — raio sensível a gaps; falsos positivos em auto-interseções |
| **Sensibilidade a concavidades** | Baixa — flood-fill contorna geometrias em L/U | Baixa — raio testa visibilidade direta |
| **Precisão em protuberâncias** | Média — limitada pelo voxel size | Alta — cada face testada individualmente |
| **Precisão em detalhes finos (ex: janelas <300mm)** | Limitada — voxel 0.5m perde detalhe | Alta — precisão da malha |
| **Parametrização** | `voxel-size` (metros) | `ray-count`, `hit-ratio` |
| **Consumo de memória** | Alto (grade 3D, O(V)) | Médio (BVH) |
| **Rastreabilidade** | Preservada via `grid[v].Elementos` (padrão do EnvExtractor) | Preservada nativamente — raio por face do elemento |
| **Validação na literatura** | Forte (van der Vaart: casca multi-LoD; projeto CHEK €5M) | Forte (Ying: 99%+ em ray tracing recursivo) |

**Nota sobre a decisão (ADR-14).** Voxel é primária pela robustez em IFC real — modelos com gaps, auto-interseções e topologia imperfeita são a norma, não a exceção (documentado em `Ferramentas/BuildingEnvExtractor/IFC_BuildingEnvExtractor_Evaluation.md` §5). Ray Casting fica como baseline de comparação, caracterizando tradeoff precisão-vs-robustez. A `NormalsStrategy` (presente em ADR-12) foi descartada: baseline trivial não contribui comparação científica relevante — RayCasting é baseline mais forte, contrastando com método state-of-the-art validado.

---

## IsExternal e LoD — Decisões de Design

### IsExternal não pertence ao BuildingElement

A propriedade `IsExternal` do IFC (`Pset_WallCommon`, etc.) é **não confiável** em modelos reais. O IFC_BuildingEnvExtractor da TU Delft ignora-a por padrão (`ignoreIsExternal_ = true`).

O algoritmo *computa* exterioridade — incluir `IsExternal` no modelo de domínio criaria dualidade confusa. A propriedade é extraída opcionalmente pelo `XbimModelLoader` e inserida como campo de **comparação** no relatório JSON:

```json
{
  "globalId": "2O2Fr$t4X7Zf8NOew3FL9r",
  "computed": { "isExterior": true },
  "declared": { "isExternal": true },
  "agreement": true
}
```

Isto permite uma métrica de validação: *"Em N% dos casos, a classificação geométrica concordou com a propriedade IsExternal declarada."*

### Sistema de LoD adotado (ADR-15)

A ferramenta adota o framework LoD de **Biljecki et al. (2016)** — refinado por **van der Vaart (2022)** no IFC_BuildingEnvExtractor — como sistema de **saídas** do pipeline. Cada LoD é um `ILodGenerator` que consome o mesmo `DetectionResult + Facade[]` e produz representação no formato natural daquele nível. A contribuição original do TCC (facade como agregado composto com provenance IFC) vive no **LoD 3.2** do framework.

```
LoD <classe>.<detalhe>      ← Biljecki/van der Vaart
    │        │
    │        └── 0–4: nível de detalhe geométrico
    └──────────── 0: footprint / 1: block / 2: roof / 3: facade / 4: full
```

**LoDs implementados** (10 standard; experimentais b/c/d/e descartados):

| Grupo | LoDs | Observação |
|---|---|---|
| Footprint (0.x) | 0.0, 0.2 | **LoD 0 via projeção XY**, não convex hull (preserva forma de L/U); 0.3/0.4 descartados (roof inclinado nos primeiros LoDs) |
| Block (1.x) | 1.0, 1.2 | Extrusões simples; 1.3 descartado (mesma razão) |
| Roof (2.x) | 2.2 | Telhado detalhado + paredes + storeys |
| Facade (3.x) | **3.2** | **Core do TCC** — shell semântico com `Facade[]` e `Face` classificadas |
| Full (4.x) | 4.0, 4.1, 4.2 | BIM-level 1:1, filtrado por exterior, faces coplanares fundidas |
| Voxel (5.0) | 5.0 (v.0) | **Não é LoD separado** — é saída do sistema de debug (ADR-16) via `DebugVoxelSet` |

**Rastreabilidade preservada em todos os LoDs.** Cada `LodOutput` carrega `ElementProvenance: IReadOnlyCollection<string>` (GlobalIds dos elementos que contribuíram), satisfazendo a exigência da questão de pesquisa *"preservando rastreabilidade semântica"*.

**CLI:** `--lod 0.0,1.0,2.2,3.2,4.1` seleciona quais gerar. Default: `3.2` (core). Saídas: arquivos separados por LoD (`report_lod32.json`, `footprint_lod00.geojson`, `shell_lod22.gltf`, …) — formato natural de cada nível; não forçamos schema unificado.

---

## Decisões Arquiteturais (ADRs)

Formato curto: decisão, motivo, consequência. Decisões históricas revogadas ficam registradas para rastreabilidade na dissertação.

### ADR-01 — [REVOGADA por ADR-09]

Previa `LeavesDeep()` recursivo em `BuildingElement` para navegar árvore profunda arbitrária. Análise pós-decisão mostrou que IFC real mantém agregações em 2 níveis — recursão é *overengineering*. Substituída por ADR-09 + ADR-11.

### ADR-02 — `IfcRelFillsElement` é ignorado no loader

**Decisão.** Janela, porta e parede são carregadas como `BuildingElement`s independentes. A relação "janela preenche void na parede" não é preservada via metadado IFC; é descoberta pelos algoritmos via geometria (bounding-box overlap, proximidade).

**Motivo.** Fiel ao princípio "geometria primeiro, IFC properties são hints". Mantém loader simples; não cria dependência em metadado que pode faltar em modelos de baixa qualidade.

**Consequência.** Algoritmos de classificação não recebem dica de "esta janela está em parede externa" — precisam inferir. Aceitável: é justamente o que o TCC se propõe a demonstrar.

### ADR-03 — Semântica de agregadores é fixa, sem flag CLI

**Decisão.** Uma só semântica de tratamento de agregadores (ADR-11) para todo o projeto. Não existe `--aggregate-mode flatten|tree|hybrid`.

**Motivo.** Menos superfície de bugs; testes mais previsíveis; documentação da dissertação mais simples; usuário final da ferramenta não precisa conhecer este detalhe interno.

**Consequência.** Se surgir um caso de modelo real que exige outro tratamento, a decisão precisa voltar ao plano antes de virar código.

### ADR-04 — `Face` = `Element` + `TriangleIds` + `Plane3d`

**Decisão.** `Face` referencia `BuildingElement` diretamente, carrega índices de triângulos no mesh do elemento (não duplica geometria) e um `Plane3d` ajustado por PCA (substitui `Normal + PointOnPlane` separados).

**Motivo.** Rastreabilidade forte (`face.Element.GlobalId` funciona direto) sem lookup externo; sem duplicação de geometria; `Plane3d` centraliza `Normal`, `PointOnPlane`, `Distance(p)`, `Project(p)`.

**Consequência.** Acoplamento `Face → BuildingElement` é aceitável — unidirecional, ambos em Core. Em serialização JSON, usar `[JsonIgnore]` em `Face.Element` e expor só `Element.GlobalId` evita ciclos.

### ADR-05 — `IElementFilter` em Core + default inclusivo + override CLI

**Decisão.** Filtro de tipos IFC é interface em Core. `DefaultElementFilter` traz uma lista hardcoded razoável. `XbimModelLoader` recebe `IElementFilter` por construtor. CLI aceita `--include-types X,Y,Z` e `--exclude-types A,B` para montar filtro programaticamente. Config opcional em `data/elementFilter.json` para persistência por modelo.

**Motivo.** Feedback explícito: *"o filtro deve ser facilmente alterado no futuro, até pelo usuário se necessário"*. Interface permite DI em testes, CLI permite override sem recompilar.

**Consequência.** `DefaultElementFilter` fica *opinativo* — inclui `IfcRailing`, exclui `IfcFooting`, etc. Decisões do default são documentadas e questionáveis em PR.

### ADR-06 — `BcfWriter` + Viewer em paralelo

**Decisão.** `BcfWriter` continua em `Cli/Output/` produzindo BCF a partir do JSON. O Viewer também produz BCF (após edição manual de rotulação). Ambos consomem o mesmo JSON.

**Motivo.** Pipeline + JSON é o caminho automatizado (reproduzível em CI). Viewer é o caminho assistido (curadoria humana). São usos distintos; um não substitui o outro.

**Consequência.** Há duas implementações de BCF no projeto. A do Viewer pode divergir (anotações manuais, viewpoints editados) da do CLI (viewpoints gerados). Compartilhar código via biblioteca BCF comum (`iabi.BCF` ou equivalente) quando possível.

### ADR-07 — Viewer MVP default; Completo como stretch goal (revisado por ADR-12; possível absorção por ADR-16)

**Decisão.** O entregável obrigatório do Viewer é o **MVP**: render 3D dos meshes coloridos por fachada, inspeção por elemento e filtro exterior/interior. **Edição manual de rotulação** e **export BCF** são *stretch goals* condicionais a stage gates (Precision/Recall do Stage 1 aceitáveis + tempo de cronograma). A versão anterior desta ADR tratava o Viewer Completo como obrigatório; ADR-12 reclassificou.

**Motivo.** Viewer Completo é o item de maior risco de cronograma e não é a questão de pesquisa. O MVP já satisfaz o critério #4 do TCC (≥4 ferramentas BIM) quando somado a Revit/ArchiCAD/FME/Solibri na validação. Edição + BCF entram apenas se houver folga após P1–P5.

**Consequência.** Contingência documentada: se Precision/Recall do Stage 1 forem insuficientes até set/2026 ou se cronograma estiver apertado, o Viewer permanece em escopo MVP e BCF é gerado pela CLI (ADR-06). Stage gates detalhados continuam na seção Viewer (§ Viewer).

> **Possível absorção (decisão em Fase 7, ver ADR-17).** O sistema de debug adotado (ADR-17) produz um viewer HTML local em `tools/debug-viewer/` a partir da Fase 3. Se esse viewer evoluir para UX amigável a especialistas AEC, o Viewer Blazor MVP pode ser absorvido — elimina-se o Viewer como projeto separado, energia concentra no debug-viewer que serve duplo propósito (dev + end-user). A decisão é adiada para Fase 7; até lá, Viewer segue como stretch goal de ADR-07 revisado.

### ADR-08 — `BuildingElement` anêmico + `IEquatable` + `BuildingElementContext`

**Decisão.** `BuildingElement` tem apenas `GlobalId`, `IfcType`, `Mesh`, `Context` (record struct com `SiteId`/`BuildingId`/`StoreyId`) e `GroupGlobalId` opcional. Implementa `IEquatable<BuildingElement>` por `GlobalId`. Sem `BoundingBox` cacheada, sem `Centroid` derivado, sem propriedades IFC avançadas.

**Motivo.** Core desacoplado de xBIM. Domínio enxuto e testável. `IEquatable` habilita `HashSet`/`Distinct`/`Dictionary` sem lambdas. `required init` torna testes legíveis.

**Consequência.** Callers que precisam de bounding box chamam `element.Mesh.GetBounds()` no ponto de uso. Qualquer metadado IFC além dos 3 IDs espaciais é buscado via `IIfcProductResolver` (ADR-10) na camada Ifc.

### ADR-09 — Agregação IFC de building elements tem 2 níveis fixos

**Decisão.** IFC real mantém `IfcRelAggregates` para building elements em exatamente 2 níveis (agregador → átomos). `Debug.Assert` no loader captura violação (child com `IsDecomposedBy` não-vazio); log warning em Release.

**Motivo.** Agregadores comuns (`IfcCurtainWall`, `IfcStair`, `IfcRamp`, `IfcRoof`) têm filhos construtivos diretos; ninguém aninha `IfcStair` dentro de `IfcStair`. Premissa informa o split do ADR-11 e evita recursão desnecessária.

**Consequência.** Loader simples, sem `LeavesDeep`. Se um modelo real violar a premissa, o assert falha em Debug e produz log em Release — trata-se excepcionalmente caso aconteça.

### ADR-10 — `IIfcProductResolver` na camada Ifc

**Decisão.** Interface em `IfcEnvelopeMapper.Ifc` (não em Core). `XbimIfcProductResolver` indexa `IfcStore.Instances.OfType<IIfcProduct>()` por `GlobalId` em `Dictionary`. Viewer, Cli, testes importam quando precisam de metadados IFC não previstos em `BuildingElementContext`.

**Motivo.** Core permanece sem referência a xBIM. Resolver explicita que o consumidor está acoplando ao schema IFC. Index evita O(n) por lookup.

**Consequência.** Propriedades IFC são *hints* — algoritmos Core não dependem do resolver. Uso típico: Viewer mostra `Pset_WallCommon` ao clicar em elemento; BCF export lê material/tag; testes de integração acessam metadados específicos.

### ADR-11 — Split do modelo: `BuildingElement` (átomo) + `BuildingElementGroup` (agregador)

**Decisão.** Loader retorna `ModelLoadResult(Elements, Groups)`. `BuildingElement` sempre tem geometria. `BuildingElementGroup` agrupa Elements de um agregador IFC (`IfcCurtainWall`, `IfcStair` etc.); tem `OwnMesh` opcional.

**Motivo.** Modelo único com `Mesh` opcional e `Children` opcional criava estados inválidos (átomo com children, agregador sem children). O split elimina isso por construção. Algoritmos consomem só `model.Elements` — comportamento trivial, sem `LeavesDeep`. `Groups` servem à rastreabilidade no relatório JSON e ao Viewer.

**Consequência.** `BuildingElement.GroupGlobalId` é back-ref opcional por `string` (evita ciclos em serialização). Filho sem geometria (ex: `IfcCurtainWallPanel` vazio) é descartado pelo loader — não vira Element, não entra em `Group.Elements`.

### ADR-12 — Escopo reduzido: 1 primária + 1 fallback + baseline, Stage 1 antes de Stage 2, Viewer MVP default

**Decisão.** O método implementa **uma** estratégia primária (`RayCastingDetectionStrategy`, Ying 2022) e **uma** estratégia de fallback (`VoxelFloodFillStrategy`, van der Vaart 2022 / Liu 2021). `NormalsStrategy` é reduzida a baseline trivial de ~20 linhas, usada apenas para comparação no capítulo de Discussão; não é mais estratégia completa. O pipeline é serializado: Stage 1 (detecção + avaliação por contagem TP/FP/FN/TN + Precision/Recall sobre ground truth) precede Stage 2 (agrupamento DBSCAN); Stage 2 não inicia até Precision/Recall do Stage 1 serem aceitáveis (threshold calibrado após primeira medição em P2). O Viewer entrega um MVP (render 3D + cores por fachada) como default; edição manual e export BCF (escopo do ADR-07 original) ficam como stretch goals sob stage gate.

**Motivo.** (a) Prazo até abr/2027 não comporta três estratégias implementadas em paralelo; literatura (Ying 2022; van der Vaart 2022) sustenta RayCasting + Voxel como combinação suficiente e complementar. (b) DBSCAN depende criticamente da qualidade do Envelope; calibrar agrupamento antes de ter detecção confiável é desperdício de esforço. (c) Viewer Completo é o item de maior risco de cronograma e não é a questão de pesquisa — MVP satisfaz o critério "≥4 ferramentas BIM" quando somado a Revit/ArchiCAD/FME/Solibri para validação.

**Consequência.** ADR-07 é redefinido: Viewer MVP é o entregável obrigatório; Viewer Completo é condicional. A ordem das Fases muda: testes/CI (P1) → RayCasting ponta-a-ponta (P2) → JsonReportWriter (P3) → Voxel fallback (P4) → DBSCAN grouper (P5) → Viewer MVP (P6). A tabela comparativa das três estratégias permanece no plano como registro de alternativas investigadas — valor para Discussão e Ameaças à Validade.

> **Nota:** ADR-12 é **superseda parcialmente por ADR-14** quanto à escolha de estratégias. Permanecem válidos: Stage 1 antes de Stage 2, gate baseado em Precision/Recall aceitáveis (threshold calibrado em P2 — ver critério de P2), Viewer MVP como default. A ordem de fases e o papel das estratégias foram redefinidos — ver ADR-14. F1 e Kappa foram removidos do plano de avaliação após leitura das referências canônicas (van der Vaart 2022 usa contagens manuais; Ying 2022 usa apenas Precision/Recall).

### ADR-13 — Aproveitamento máximo da stack para matemática e indexação espacial

**Decisão.** Matemática de detecção e agrupamento (plane-fit PCA, eigen solver, interseção triângulo-AABB, histograma de normais na esfera de Gauss) usa classes já presentes em `geometry4Sharp`. **`NetTopologySuite.STRtree` é 2D apenas** — usado exclusivamente no LoD 0 (projeção XY, ADR-15). Para queries 3D sobre `BuildingElement` o plano é: linear scan com AABB test (n típico ≤ 10⁴ — O(n) é aceitável); para queries triangulo-a-triangulo, `g4.DMeshAABBTree3` (BVH 3D nativo do geometry4Sharp). Nenhum `MathNet.Numerics` é adicionado; nenhum algoritmo clássico (Akenine-Möller tri-AABB) é re-implementado localmente.

**Motivo.** Investigação das ferramentas de referência (Voxelization Toolkit, IFC_BuildingEnvExtractor) mostrou que ambas escreveram voxel storage e flood-fill do zero, mas delegaram math fundamental a Eigen/OCCT/Boost. A stack .NET **não tem equivalente direto ao `Boost.Geometry rstar<Point3D>`** — tentar usar `STRtree` em 3D foi um erro da versão anterior desta ADR. Análise do hot path do algoritmo mostra que: (i) voxelização itera `elemento → triângulos → voxels` (não precisa indexar elementos); (ii) provenance é guardada em `grid[v].Elements` (não precisa query reversa indexada); (iii) DBSCAN opera em R³ unitário (Gauss sphere, não espaço físico); (iv) adjacência de faces é O(f²) com f pequeno. Linear scan basta. Se profiling futuro apontar gargalo, um octree custom (~150 linhas) resolve sem depender de lib.

**Consequência.** Mapeamento direto de decisões algorítmicas a classes .NET:

| Componente do plano | Classe / lib |
|---|---|
| `Face.FittedPlane` via PCA (ADR-04) | `g4.OrthogonalPlaneFit3` |
| Normais de mesh (ponderadas por área) | `g4.MeshNormals` |
| Eigen genérico (se portar `dimensionality_estimate`) | `g4.SymmetricEigenSolver` |
| Voxelização — interseção triângulo-AABB (P2+P3) | SAT próprio (Akenine-Möller 1997) — `g4.IntrTriangle3Box3` ausente |
| Esfera de Gauss pré-discretizada (P5, opcional) | `g4.NormalHistogram` |
| BVH 3D de triângulos por mesh (ray casting P4) | `g4.DMeshAABBTree3` |
| Queries AABB 3D sobre `BuildingElement` | Linear scan com AABB pre-filter |
| Índice R-tree **2D** (união de polígonos no LoD 0) | `NetTopologySuite.STRtree` |
| Clustering DBSCAN | `DBSCAN` (NuGet) |
| Grafo + componentes conectados | `QuikGraph` |

Se surgir necessidade de indexação 3D performante (profiling futuro), avaliar octree custom antes de adicionar dependência externa.

### ADR-14 — Consolidação: 1 primária (Voxel) + 1 baseline (RayCasting), Normais descartada

**Superseda ADR-12** nos itens: (a) escolha da primária, (b) papel do RayCasting, (c) presença de `NormalsStrategy`. Mantém de ADR-12: Stage 1 antes de Stage 2, Viewer MVP como default, stage gate baseado em Precision/Recall (thresholds calibrados após primeira medição em P2; F1/Kappa removidos — ver ADR-12 nota).

**Decisão.** Estratégia de produção única: `VoxelFloodFillStrategy` (van der Vaart 2022 + extensões: cascata 4-testes, 3 fases flood-fill, `FillGaps`). `RayCastingStrategy` (Ying 2022) permanece implementada exclusivamente como baseline de comparação no capítulo de Resultados — não é usada em produção. `NormalsStrategy` é descartada completamente.

**Motivo.** (a) Voxel é robusto por design em IFC real — malformed meshes são norma, não exceção; sua própria avaliação do `IFC_BuildingEnvExtractor` documenta isso (`Ferramentas/BuildingEnvExtractor/IFC_BuildingEnvExtractor_Evaluation.md` §5). (b) A contribuição original do TCC é Stage 2 (fachada como composto + DBSCAN sobre Gauss sphere) — Stage 1 deve ser confiável, não comparativo superficial entre 3 alternativas. (c) Baseline trivial (Normais ~20 linhas) prova contribuição científica zero; RayCasting como baseline caracteriza tradeoff substantivo precisão-vs-robustez contra state-of-the-art validado (Ying 99%+). (d) Prazo até abr/2027 favorece profundidade sobre largura: 1 implementação robusta + 1 baseline comparativo é mais defensável que 2 primárias superficiais + 1 trivial.

**Consequência.**
- CLI default: `--strategy voxel` (removidas `raycast` como default e `normals` como opção).
- Ordem das Fases atualizada: P1 (infra) → P2 (Voxel ponta-a-ponta + debug visual) → P3 (RayCasting baseline + JSON/BCF) → P4 (LoDs 0.x–2.x) → P5 (DbscanFacadeGrouper) → P6 (LoDs 3.x–4.x) → P7 (Viewer MVP).
- Pseudocódigo 1A (Normais) removido do plano; 1B (RayCasting) reclassificado como baseline; 1C (Voxel) renomeado para 1A e expandido como primária com cascata 4-testes + 3 fases + `FillGaps`.
- Provenance em Voxel: cada voxel mantém `Elementos` (set de `GlobalId`) ao ser marcado ocupado; classificação final lê essa lista. Padrão replicado do `internalProducts_` do EnvExtractor.
- Contingência: se voxel em P2 falhar em fixtures com detalhes finos (ex: janelas <300mm) e não houver calibração satisfatória via `voxel-size`, reconsiderar voxel adaptativo ou (última opção) RayCasting como primária. Decisão documentada em novo ADR caso necessário.

**Ameaças à validade (registrar na dissertação).** Dropar Normais significa perder o baseline "trivial" clássico. Mitigação narrativa: RayCasting é baseline mais forte — argumento na banca será *"comparamos com método state-of-the-art validado, não com heurística ingênua"*. Perda da análise "voxel como fallback": reformulada como *"voxel como primária por robustez, raycast como comparação de precisão"* — narrativa mais clara.

### ADR-15 — Adoção do framework LoD (Biljecki/van der Vaart)

**Decisão.** Adotar o sistema LoD de Biljecki et al. (2016), refinado por van der Vaart (2022) no IFC_BuildingEnvExtractor, como **sistema de saídas** do IfcEnvelopeMapper. 10 LoDs standard implementados via `ILodGenerator` em projeto novo `IfcEnvelopeMapper.Lod/`. Experimentais (b.0, c.1, c.2, d.1, d.2, e.1) descartados. LoD 0 via **projeção XY** (não convex hull — preserva formas L/U). LoD 5.0 (voxel) **subsumido pelo sistema de debug** (ADR-16), não é LoD separado.

**LoDs adotados:** `0.0, 0.2, 1.0, 1.2, 2.2, 3.2, 4.0, 4.1, 4.2`. A contribuição original do TCC (facade como agregado composto com provenance IFC) vive no **LoD 3.2**. LoDs 0.3/0.4/1.3/2.2-roof-inclinado e variantes experimentais descartados para conter escopo — detecção de superfícies inclinadas de telhado em níveis de footprint/block é overkill; em 3.2 já há semantic face classification que cobre o caso.

**Motivo.** (a) Posicionamento acadêmico forte: *"este trabalho estende o LoD 3.2 do framework Biljecki/van der Vaart introduzindo facade como entidade composta com provenance IFC"* é narrativa sólida para a banca. (b) Stage 1 + Stage 2 produzem o mesmo `DetectionResult + Facade[]` independente de LoD — os geradores são transformações de saída, não alteram o algoritmo core. (c) Múltiplos LoDs atendem múltiplos casos de uso (GIS LoD 0-1, modelagem urbana LoD 2, BIM LoD 3-4) — reforça o critério #4 do TCC (≥4 ferramentas BIM). (d) LoD 0 com projeção XY (em vez de convex hull) preserva forma exata; convex hull perderia informação em edifícios em L ou com poço de luz.

**Consequência.**
- Novo projeto `IfcEnvelopeMapper.Lod/` com 10 `ILodGenerator` implementations + `LodRegistry`.
- Remoção da seção "Sem sistema de LoD" (substituída por "Sistema de LoD adotado").
- CLI ganha flag `--lod <lista>` (default: `3.2`). Saídas em arquivos separados por LoD.
- Schema JSON v3 substitui v2 para o LoD 3.2; outros LoDs usam formatos naturais (GeoJSON para 0.x, glTF/OBJ para 2.x+, etc.).
- Rastreabilidade (`ElementProvenance: IReadOnlyCollection<string>` com `GlobalId`s) preservada em todos os LoDs — satisfaz a questão de pesquisa em qualquer nível de saída.

### ADR-16 — Sistema de debug multi-estágio via glTF

> ⚠️ **Substituída por ADR-17 (2026-04-19).** Ver ADR-17 abaixo.

**Decisão (Opção A — facade estático).** Projeto novo `IfcEnvelopeMapper.Debug/` define `IDebugSink` em Core e implementa sinks em Debug/. Sink primário: `GltfDebugSink` (via `SharpGLTF.Toolkit`). `NullDebugSink` default em produção.

---

### ADR-17 — Debug geométrico via compilação condicional (#if DEBUG)

**Decisão.** Substituir a arquitetura de runtime `IDebugSink`/`NullDebugSink`/`GltfDebugSink` por uma classe estática `GeometryDebug` em `IfcEnvelopeMapper.Geometry/Debug/`, inteiramente envolvida em `#if DEBUG`. Em builds Release, todas as chamadas são eliminadas pelo pré-processador do C# — zero overhead, zero configuração, sem null-object pattern. Em builds Debug, cada método acumula formas na lista interna e chama `Flush()` imediatamente; na Fase 2, `Flush()` serializa para `%TEMP%\ifc-debug-output.gltf` via `SharpGLTF`. O developer define breakpoints no IDE para controlar o ritmo da inspeção.

**Localização em Geometry (não Core).** `Voxels()` depende de `VoxelGrid3D`, que é um tipo Geometry. `Core` não pode referenciar `Geometry` — dependência invertida. Todos os projetos que precisam do debugger (Algorithms, Cli) já referenciam Geometry.

**Motivo.** `IDebugSink` adicionava complexidade desnecessária: DI em construtores, null-sink em produção, fan-out. `#if DEBUG` é o padrão idiomático do C# para código de desenvolvimento — eliminação em compile-time, sem configuração.

**Consequência.**
- Deletados de `Core`: `IDebugSink`, `DebugShape`, `DebugColor`, `NullDebugSink`, `Debug`.
- `IfcEnvelopeMapper.Debug/` mantido como placeholder para utilitários opcionais de serialização futura.
- Strategies e grouper chamam `GeometryDebug.Mesh(...)`, `GeometryDebug.Voxels(...)` etc. diretamente.
- `SharpGLTF.Toolkit` permanece na stack — será usado em `Flush()` na Fase 2.
- **ADR-07 pode ser absorvida.** Se o debug-viewer (Camada B) evoluir para UX amigável a end-user, Viewer MVP Blazor pode ser absorvido. Decisão adiada para Fase 5.

---

## Determinismo do Método

Requisito para responder à banca *"o método é determinístico?"* e para viabilizar testes de regressão por snapshot (§ Testes).

**Aleatoriedade controlada.** DBSCAN e ordenações default podem produzir saídas não-determinísticas. Garantias:

1. **Semente fixa** para qualquer uso de `Random`: `new Random(seed: 42)`. Seed é constante do projeto, documentada, nunca derivada de tempo/hostname.
2. **Ordenação estável antes de iterar** em coleções cuja ordem afeta o resultado: `.OrderBy(e => e.GlobalId, StringComparer.Ordinal)`. Vale especialmente para o input do DBSCAN (primeira face vira primeiro cluster).
3. **Sem paralelismo não controlado.** `Xbim3DModelContext.MaxThreads = 1` já está fixado (workaround OCCT); demais pipelines rodam sequencialmente. Se for introduzir PLINQ/`Parallel.For`, só com ordenação final explícita.

**Teste de determinismo.** Em `tests/IfcEnvelopeMapper.Tests/Regression/`: rodar o pipeline no mesmo fixture 3× e comparar outputs byte-a-byte (após serialização JSON com chaves ordenadas). Falha se algum par diverge.

**Regras para re-geração de snapshot.** Arquivos `expected-report.json` só são regerados em PR com (a) justificativa no commit message, (b) diff revisado por humano, (c) bump de versão do schema se a mudança for estrutural.

---

## Filtragem de Relatório e Prova de Aplicabilidade

O modelo `Envelope` + `Facade[]` suporta filtragem para diferentes cenários de uso sem nenhuma arquitetura adicional:

```csharp
// WWR por fachada — puro LINQ sobre o modelo existente
foreach (var facade in facades)
{
    var wallArea   = facade.Faces.Where(f => f.Element.IfcType is "IfcWall" or "IfcCurtainWall").Sum(f => f.Area);
    var windowArea = facade.Faces.Where(f => f.Element.IfcType == "IfcWindow").Sum(f => f.Area);
    var wwr        = windowArea / wallArea;
}
```

**Decisão:** O relatório JSON incluirá **WWR por fachada** como prova de aplicabilidade. Outros cenários (OTTV, GIS/CityJSON, compliance checking) são mencionados no capítulo de Trabalhos Futuros sem implementação. Sem sistema de perfis/configurações de relatório.

---

## Schema JSON v2

```json
{
  "run": {
    "model": "duplex.ifc",
    "strategy": "voxel",
    "grouper": "dbscan",
    "timestamp": "2026-04-10T14:30:00Z",
    "parameters": {
      "voxelSize": 0.5
    }
  },
  "summary": {
    "totalElements": 142,
    "exteriorElements": 38,
    "facadeCount": 4,
    "evaluation": {
      "truePositives": null,
      "falsePositives": null,
      "falseNegatives": null,
      "trueNegatives": null,
      "precision": null,
      "recall": null
    }
  },
  "classifications": [
    {
      "globalId": "2O2Fr$t4X7Zf8NOew3FLne",
      "ifcType": "IfcWall",
      "computed": {
        "isExterior": true,
        "facadeIds": ["facade-01"]
      },
      "declared": {
        "isExternal": true
      },
      "agreement": true
    }
  ],
  "facades": [
    {
      "id": "facade-01",
      "dominantNormal": [0.0, -1.0, 0.0],
      "azimuthDegrees": 180.0,
      "faceCount": 48,
      "elementCount": 22,
      "metrics": {
        "totalArea": 245.6,
        "wallArea": 198.2,
        "windowArea": 47.4,
        "wwr": 0.239
      }
    }
  ],
  "aggregates": [
    {
      "globalId": "3DqR$tPmX7Zf8NOew3FLaa",
      "ifcType": "IfcCurtainWall",
      "elementCount": 11,
      "participatingFacades": ["facade-01", "facade-02"]
    }
  ],
  "diagnostics": {
    "elementsSkipped": 12,
    "reasons": [
      { "globalId": "1A2B…", "ifcType": "IfcCurtainWallPanel", "reason": "empty mesh" },
      { "globalId": "3C4D…", "ifcType": "IfcWall", "reason": "n-gon face, triangulated via fan" }
    ]
  }
}
```

Quando `--ground-truth` é fornecido, o bloco `evaluation` é preenchido automaticamente com contagens TP/FP/FN/TN + Precision e Recall derivados (sem F1, sem Kappa — ver ADR-12).

**Bloco `aggregates`.** Produzido a partir de `ModelLoadResult.Groups` (ADR-11). Lista cada `BuildingElementGroup` com o conjunto de fachadas em que seus Elements participaram — útil para relatórios agrupados por cortina de vidro, escada, etc.

**Bloco `diagnostics`.** Coleta warnings do `XbimModelLoader` e dos Stages 1/2: elementos descartados por mesh vazio, triangulações convertidas por fan-fallback, faces *noise* do DBSCAN. Alimentado por `ILogger<T>` com sink em memória. Ver seção Determinismo e estratégia de testes.

---

## Viewer — Curadoria Assistida (ADR-07)

Segundo ponto de entrada: **ASP.NET Core Blazor Server + three.js**. Consome o mesmo `report.json` da CLI + o IFC original. A decisão sobre escopo (MVP Blazor vs absorção pelo debug-viewer) ocorre na **Fase 7** (dez/2026) — ver ADR-07 e ADR-17.

**MVP obrigatório:** render 3D colorido por fachada, filtro exterior/interior, inspeção por elemento (GlobalId, IfcType, `IIfcProductResolver`). Viewer **nunca re-executa o pipeline** — CLI = algoritmo automatizado, Viewer = revisão humana.

**Stretch goal** (condicional a Precision/Recall do Stage 1 aceitáveis e folga de cronograma): edição manual de rotulação + export BCF (ADR-06). Stage gate bloqueante: Viewer não inicia até pipeline produzir JSON válido (P3 concluído).

**Trabalhos Futuros (fora do escopo):** ingestão de BCF externo para re-calibrar algoritmo; re-execução sobre regiões editadas; histórico de rotulações; multi-usuário.

---

## Interface CLI v2

```
ifcenvmapper detect <model.ifc> [opções]

Opções globais:
  --strategy      <voxel|raycast>                Estratégia de detecção     [padrão: voxel — ADR-14]
  --grouper       <dbscan|directional>           Agrupamento em fachadas    [padrão: dbscan]
  --lod           <lista>                        LoDs a gerar (ADR-15)       [padrão: 3.2]
                                                 Válidos: 0.0,0.2,1.0,1.2,2.2,3.2,4.0,4.1,4.2
  --output        <path>                         Diretório de saída         [padrão: ./output]
  --format        <json|bcf|both>                Formato primário (LoD 3.2) [padrão: json]
  --ground-truth  <labels.csv>                   Calcula contagens TP/FP/FN/TN + Precision/Recall (opcional)
  --verbose                                      Logging detalhado

Opções específicas por estratégia:
  --voxel-size      <metros>    [voxel]        Aresta do voxel            [padrão: 0.5]
  --ray-count       <int>       [raycast]      Raios por centroide        [padrão: 64]
  --hit-ratio       <float>     [raycast]      Razão mínima exterior      [padrão: 0.5]

Exemplos:
  ifcenvmapper detect duplex.ifc
  ifcenvmapper detect duplex.ifc --lod 1.0,3.2 --voxel-size 0.25 --output results/
  ifcenvmapper detect duplex.ifc --strategy raycast --ray-count 128   # baseline P4
  ifcenvmapper detect duplex.ifc --ground-truth data/ground-truth/duplex.csv
```

**Formato do ground truth CSV:**
```
GlobalId,IsFacade
2O2Fr$t4X7Zf8NOew3FLne,true
3xYmK9pQr2Wv7NLqZ1ABcd,false
```

---

## Modelos IFC Disponíveis

Arquivos prontos para uso local (já copiados para `data/models/`):

| Arquivo | Origem | Complexidade |
|---|---|---|
| `duplex.ifc` | voxelization_toolkit/tests/fixtures/ | Média — edifício residencial duplex |
| `duplex_wall.ifc` | voxelization_toolkit/tests/fixtures/ | Baixa — somente paredes |
| `schependom_foundation.ifc` | voxelization_toolkit/tests/fixtures/ | Baixa — fundações |
| `demo2.ifc` | voxelization_toolkit/tests/fixtures/ | A verificar |
| `covering.ifc` | voxelization_toolkit/tests/fixtures/ | A verificar |

**Próximo passo em datasets:** identificar modelos IFC públicos mais completos (buildingSMART Sample Models, BIMData R&D, OpenIFC Auckland, IFCNet RWTH Aachen).

---

## Fases de Desenvolvimento

### Fase 0 — ✅ Spike: carregamento e triangulação (concluída)
**Meta:** parsear um arquivo IFC real com xBIM e extrair geometria.
**Critério de sucesso:** ✅ carregar `duplex.ifc` (157 elementos) e produzir `BuildingElement` com `DMesh3` não-vazia.

- [x] Solução `.slnx` com os 6 projetos e estrutura de pastas
- [x] Pacotes NuGet básicos (Xbim.Essentials, Xbim.Geometry, geometry4Sharp)
- [x] `Program.cs` mínimo: abre IFC, itera elementos, loga tipos + GlobalId
- [x] `XbimModelLoader.Load()` v0: `IReadOnlyList<BuildingElement>` via `Xbim3DModelContext`
- [x] `Xbim3DModelContext.MaxThreads = 1` (workaround OCCT thread-unsafe)

**Saída:** `src/IfcEnvelopeMapper.Cli/Program.cs` imprime `{IfcType} {GlobalId} tris={N}` para 157 elementos.

---

### Fase 1 — P1: Modelo refinado + testes-base + CI + Debug scaffold ✅ (concluída — abr/2026)
**Meta:** absorver ADRs 02-17 no código e estabelecer infraestrutura de testes + debug geométrico antes de qualquer algoritmo novo.
**Critério de sucesso:** ✅ 34 testes unitários no CI + 2 integração local; loader retorna `ModelLoadResult(Elements, Groups)`; `GeometryDebug` compila em Debug e é eliminado em Release.

**Domínio (Core):**
- [x] `BuildingElementContext` (record struct, ADR-08)
- [x] `BuildingElement` anêmico (required init, IEquatable, ADR-08 + ADR-11)
- [x] `BuildingElementGroup` (ADR-11)
- [x] `ModelLoadResult` (record)
- [x] `Face` com `Element + TriangleIds + FittedPlane` (ADR-04)
- [x] `Envelope`, `Facade` (Surface/)
- [x] `DetectionResult`, `ElementClassification`

**Interfaces (Core — Loading/Detection/Grouping):**
- [x] `Loading/IModelLoader.cs` retornando `ModelLoadResult`
- [x] `Loading/IElementFilter.cs` + `Loading/DefaultElementFilter.cs` (ADR-05)
- [x] `Detection/IDetectionStrategy.cs` — assinatura limpa: `Detect(IReadOnlyList<BuildingElement>)`
- [x] `Detection/IFaceExtractor.cs` — `BuildingElement → Face[]` via PCA coplanar
- [x] `Grouping/IFacadeGrouper.cs` — assinatura limpa: `Group(Envelope)`

**Debug (Geometry) — ADR-17:**
- [x] `GeometryDebug` (#if DEBUG) em `IfcEnvelopeMapper.Geometry/Debug/` — 10 métodos de primitivas geométricas (`Mesh`, `Triangles`, `Voxels`, `Points`, `Line`, `Lines`, `Box`, `Plane`, `Sphere`, `Normal`)
- [x] Projeto `IfcEnvelopeMapper.Debug/` — placeholder para utilitários de serialização futura

**Loader (Ifc):**
- [x] `XbimModelLoader` v1: split Elements/Groups, filtro injetado, error handling tipado
- [x] `IIfcProductResolver` + `XbimIfcProductResolver` (ADR-10)
- [x] Descarte de Elements sem geometria (TriangleCount == 0)

**Testes:**
- [x] `tests/IfcEnvelopeMapper.Tests/` scaffold (xUnit + FluentAssertions)
- [x] `BuildingElementTests`, `BuildingElementGroupTests`, `FaceTests`
- [x] `XbimModelLoaderTests` (integração com `duplex.ifc`, Category=Integration)

**Infra:**
- [x] `.github/workflows/build.yml` — PRs para main, ubuntu-latest, exclui Category=Integration
- [x] Error handling tipado: `IfcLoadException`, `IfcGeometryException`
- [x] `.gitignore` ajustado

---

### Fase 2 — P2: Validação da detecção + debug visual (01/mai → 12/jun/2026) · 6 semanas

**Meta:** Pipeline de detecção validado quantitativamente e inspecionável visualmente no debug-viewer.
**Referência canônica:** van der Vaart (2022) — IFC_BuildingEnvExtractor. Código-fonte em `Ferramentas/BuildingEnvExtractor/`.
**Critério de sucesso:** contagens TP/FP/FN/TN + Precision/Recall reportadas para ≥ 1 fixture IFC, com thresholds de aceitação calibrados após a primeira medição — **stage gate para P4.3 e P5**; P4.1 e P4.2 podem iniciar independentemente do gate; voxels inspecionáveis no debug-viewer por fase (rasterize → exterior → interior → void). Escolha metodológica: avaliação por contagem (estilo van der Vaart 2022) + Precision/Recall (Ying 2022); F1 e Kappa foram descartados por não aparecerem nas referências canônicas (ver ADR-12 nota).

**Detecção (Stage 1) — P2:**
- [ ] `GeometricOps`: plane fitting via `g4.OrthogonalPlaneFit3` (ADR-13), face normals via `g4.MeshNormals`, building bbox
- [x] `VoxelGrid3D` — grade 3D com estado por voxel + provenance de ocupantes; SAT triângulo-AABB próprio (Akenine-Möller 1997 — `g4.IntrTriangle3Box3` ausente em geometry4sharp)
- [x] `PcaFaceExtractor : IFaceExtractor` — agrupamento por normal + distância + fit PCA via `OrthogonalPlaneFit3`
- [x] `VoxelFloodFillStrategy : IDetectionStrategy` — 3 fases (`GrowExterior` → `GrowInterior` → `GrowVoid`) + `FillGaps` (ADR-14); `PcaFaceExtractor` integrado para faces de elementos exteriores
- [ ] **Instrumentação de debug** (ADR-17): `GeometryDebug.Voxels()` por fase; `GeometryDebug.Mesh()` por elemento classificado
- [x] `DetectionResult` (Envelope + ElementClassification[]) — concluído em P1
- [ ] Determinismo: seed fixa, ordenação estável (§ Determinismo)

**Debug (Geometry) — ADR-17:**
- [ ] Implementar `GeometryDebug.Flush()` via `SharpGLTF.Toolkit` — serializa formas acumuladas para `%TEMP%\ifc-debug-output.gltf`
- [x] `Voxels()` + `VoxelsShape` adicionados após integração do `feat/phase2-voxel`
- [ ] `tools/debug-viewer/index.html` — HTML+three.js com polling de `%TEMP%\ifc-debug-output.gltf` a cada 1 segundo

**Validação quantitativa (Cli) — P2:**
- [ ] Smoke test: `detect duplex.ifc` → console com contagem de elementos exterior/interior
- [ ] CSV ground-truth loader + contagens TP/FP/FN/TN + Precision/Recall
- [ ] `ILogger<T>` (Microsoft.Extensions.Logging) para diagnostics

**Marco paralelo — Spike Viewer (1 semana, mai/2026):**
- [ ] Blazor Server scaffold + three.js interop
- [ ] Carregar 1 mesh + render + click → GlobalId no servidor
- [ ] Confirma viabilidade do Viewer MVP para P6 (nota: decisão de absorção pelo debug-viewer fica para Fase 5, ver ADR-07 revisado + ADR-16)

---

### Fase 3 — P3: RayCasting baseline + JSON + BCF (01/jul → 05/ago/2026) · 5 semanas

**Meta:** comparação Voxel vs RayCasting tabelada (contagens + Precision/Recall); output JSON e BCF mínimo operacionais.
**Critério de sucesso:** tabela comparativa Voxel vs RayCasting (TP/FP/FN/TN + Precision/Recall) em ≥ 2 modelos; `detect duplex.ifc` → `report.json` + `report.bcf` mínimo.
**Nota:** julho mais lento (Etapa 2 — 16/jun–07/jul); priorizar P3.0 + início de P3.1 nesse período.

**P3.0 — Infra (1 semana)**
- [ ] `ILodGenerator` interface + `LodOutput` record + `LodRegistry`
- [ ] `System.CommandLine`: flags `--lod`, `--strategy`, `--output`, `--format` (§ CLI v2)
- [ ] `ILogger<T>` (Microsoft.Extensions.Logging) para diagnostics

**P3.1 — RayCasting baseline (2 semanas)**
Ref: Ying et al. (2022); ADR-14.
- [ ] `RayCastingStrategy : IDetectionStrategy` — BVH via `g4.DMeshAABBTree3` (ADR-13); chama `GeometryDebug.Line(...)` para raios hit/escape (ADR-17)
- [ ] Testes unitários
- [ ] Comparação em fixtures (inclui fixture degradada com gaps para validar a escolha de Voxel como primária)
- [ ] Tabela comparativa Voxel vs RayCasting (TP/FP/FN/TN + Precision/Recall) para a dissertação

**P3.2 — JSON + BCF mínimo (2 semanas)**
Ref: ADR-06 (BCF); ADR-15 (LoD output).
- [ ] `ReportBuilder` + `JsonReportWriter` — usa `ILodGenerator` por `--lod`; default `3.2`
- [ ] `DebugVoxelCommand` (subcomando `debug-voxel`) — exporta voxels coloridos por estado
- [ ] `BcfWriter` — escopo mínimo: tópicos + viewpoints + GlobalId (ADR-06)
- [ ] Testes unitários

> RayCasting é baseline de comparação, não fallback de produção (ADR-14). Se Voxel falhar em fixtures críticos, a resposta é calibrar Voxel, não trocar estratégia.

---

### Fase 4 — P4: LoDs 0.x + 1.x + 2.x (06/ago → 03/set/2026) · 4 semanas

**Meta:** espectro de representações geométricas do modelo disponível via `--lod`.
**Critério de sucesso:** LoDs 0.0, 0.2, 1.0, 1.2, 2.2 selecionáveis via `--lod`; testes unitários por gerador.

**P4.1 — LoD 0.x: Footprints 2D (2 semanas)**
Ref: Biljecki et al. (2016) — CityGML LoD framework; ADR-15.
- [ ] `Lod00FootprintXYGenerator` — projeção XY via `NetTopologySuite` (`STRtree` 2D + `UnaryUnionOp`); ADR-13
- [ ] `Lod02StoreyFootprintsGenerator` — footprints por `IfcBuildingStorey` via `IfcRelContainedInSpatialStructure`
- [ ] Testes unitários

**P4.2 — LoD 1.x: Blocos extrudados (1 semana)**
Ref: Biljecki et al. (2016); ADR-15.
- [ ] `Lod10ExtrudedBboxGenerator` — bloco extrudado do AABB global do modelo
- [ ] `Lod12StoreyBlocksGenerator` — bloco extrudado por pavimento
- [ ] Testes unitários

**P4.3 — LoD 2.x: Superfícies detalhadas (1 semana)**
Ref: Biljecki et al. (2016); ADR-15.
- [ ] `Lod22DetailedRoofWallsStoreysGenerator` — cobertura + paredes exteriores + lajes por pavimento
- [ ] Testes unitários

---

### Fase 5 — P5: Agrupamento em fachadas — DbscanFacadeGrouper (04/set → 29/out/2026) · 8 semanas

**Meta:** `Facade[]` completo com DBSCAN + QuikGraph.
**Pré-requisito:** Precision/Recall do Stage 1 aceitáveis — gate de P2 (thresholds calibrados após primeira medição; ver ADR-12). Calibrar DBSCAN antes de detecção confiável é desperdício.
**Critério de sucesso:** facades coerentes por plano dominante em 3+ modelos; WWR calculado por fachada. Debug-viewer permite inspecionar Gauss sphere + clusters.

- [ ] `DbscanFacadeGrouper : IFacadeGrouper` (DBSCAN sobre esfera de Gauss + QuikGraph para conectividade); chama `GeometryDebug.Points(...)` / `GeometryDebug.Lines(...)` internamente (ADR-17)
- [ ] **Instrumentação de debug crítica** (ADR-17): normais da esfera de Gauss como `GeometryDebug.Points()`, arestas do grafo de adjacência como `GeometryDebug.Lines()`, fachadas finais como `GeometryDebug.Triangles()` coloridas por `facadeId`
- [ ] Calibração empírica de ε e minPoints em fixtures — **usando debug-viewer para visualização** (Camada B de ADR-16)
- [ ] Opção: pré-filtro via `g4.NormalHistogram` (ADR-13) se ruído justificar
- [ ] Testes unitários do grouper + regressão por snapshot

---

### Fase 6 — P6: LoD 3.x + 4.x (30/out → 04/dez/2026) · 5 semanas

**Meta:** completar LoDs 3.x e 4.x com rastreabilidade por fachada.
**Pré-requisito:** `Facade[]` entregue na Fase 5.
**Critério de sucesso:** LoDs 3.x e 4.x geráveis via `--lod`; faces rastreadas ao elemento de origem com `GlobalId`.

**P6.1 — LoD 3.x** (Ref: Biljecki et al. 2016; van der Vaart 2022)
- [ ] `Lod32SemanticShellGenerator` — consome `Facade[]` do DBSCAN; seção `facades` + WWR (ADR-15)
- [ ] Testes unitários Lod32

**P6.2 — LoD 4.x** (stretch goal)
- [ ] `Lod40ElementWiseGenerator` — todos os elementos 1:1
- [ ] `Lod41ExteriorElementsGenerator` — só os com face exterior
- [ ] `Lod42MergedSurfacesGenerator` — faces coplanares fundidas
- [ ] Testes unitários por gerador

---

### Fase 7 — P7: Viewer MVP OU absorção pelo debug-viewer (05/dez/2026 → 16/jan/2027) · 6 semanas

**Meta:** decisão sobre Viewer (ADR-07 × ADR-16) + implementação.
**Critério de sucesso:** usuário especialista consegue abrir artefatos e navegar resultados.

**Decisão sobre Viewer (ADR-07 × ADR-16):**
Nesta fase, avaliar o estado do `tools/debug-viewer/` (entregue em Fase 3):
- Se UX estiver amigável a especialistas AEC → **absorver** Viewer pelo debug-viewer; remover projeto `IfcEnvelopeMapper.Viewer/` do plano; energia concentra em polimento do debug-viewer.
- Se debug-viewer for adequado só para dev (UI técnica) → **Viewer MVP Blazor segue**:
    - [ ] `Components/`: render 3D por elemento colorido por fachada (consome LoD 3.2)
    - [ ] Filtro exterior/interior, inspeção (GlobalId, IfcType, `IIfcProductResolver`)
    - [ ] Overlay opcional de ground truth CSV
- Documentar decisão em ADR novo (ADR-18) na data.

**Stretch goal (condicional):**
- [ ] Edição manual de rotulação e export BCF editado — mantido como extensão opcional (ADR-07 original)

---

### Fase 8 — Ground Truth & Avaliação Experimental (out/2026 – jan/2027, paralela)
**Meta:** validar o método contra rótulos manuais de especialistas.
**Critério de sucesso:** tabela com contagens TP/FP/FN/TN + Precision/Recall por modelo e por tipologia; ≥75% de concordância simples (percent agreement) entre especialistas na rotulação.

- [ ] Selecionar 3–5 modelos IFC de tipologias diferentes (planta retangular, L, curva/irregular)
- [ ] Protocolo de rotulação (critérios, ferramenta — provavelmente Viewer MVP, resolução de divergências)
- [ ] Recrutar 5+ profissionais AEC
- [ ] Percent agreement entre especialistas (contagem direta de rótulos concordantes / total)
- [ ] Tabela de resultados para a dissertação

---

### Fase 9 — Entrega (jan–fev/2027)
**Meta:** finalizar documentação, testes de usabilidade e publicação.
**Critério de sucesso:** defesa da Etapa 4 em 05/02/2027; repositório público e reproduzível.

- [ ] Testes de usabilidade do Viewer com ≥3 especialistas AEC
- [ ] README final (instalação, uso, exemplos, workaround Google Drive)
- [ ] Publicação no GitHub como repositório público
- [ ] Artefatos da dissertação: tabelas de resultado, figuras, links para reprodução

> **Nota:** Não há saída de IFC enriquecido. O modelo original não é modificado. Resultados são exclusivamente JSON + BCF.

---

## Critérios de Sucesso do TCC

A ferramenta é bem-sucedida academicamente quando:

1. **O método funciona de ponta a ponta** em modelos IFC reais de diferentes tipologias
2. **Resultados são mensuráveis**: Precisão e Recall calculados contra ground truth rotulado por especialistas
3. **Rastreabilidade preservada**: cada face detectada e cada fachada agrupada são rastreáveis ao `BuildingElement` de origem
4. **Aplicabilidade demonstrada**: WWR por fachada calculado a partir dos resultados de detecção
5. **O resultado é reproduzível**: qualquer pessoa com .NET 8 pode rodar `dotnet run` e obter os mesmos números

---

