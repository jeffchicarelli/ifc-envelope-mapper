# Plano de Implementação — IfcEnvelopeMapper

> Documento vivo. Atualizar a cada sessão de desenvolvimento.
> Última atualização: 2026-04-17

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
| **Ground truth** | Conjunto de rótulos de referência (elementos marcados como fachada / não-fachada) produzido por rotulação manual de especialistas AEC. Base para cálculo de Precisão, Recall e F1-score. |
| **Precisão / Recall / F1** | Métricas de avaliação de classificação binária. Precisão: dos classificados como fachada, quantos realmente são? Recall: dos que são fachada, quantos foram encontrados? F1: média harmônica das duas. |
| **DBSCAN** | Density-Based Spatial Clustering of Applications with Noise — algoritmo de clustering sem número fixo de grupos. Usado para agrupar normais de faces na esfera de Gauss e detectar planos dominantes. |
| **BVH** | Bounding Volume Hierarchy — estrutura de aceleração espacial para ray casting. |
| **WWR** | Window-to-Wall Ratio — razão entre área de janelas e área total de parede por fachada. Métrica usada como prova de aplicabilidade do método. |

---

## Objetivo

Construir uma ferramenta C#/.NET que identifica automaticamente elementos de fachada em modelos IFC usando **apenas geometria 3D** — sem depender de propriedades ou metadados do modelo.

O trabalho propõe **um método computacional**, avaliado rigorosamente em modelos IFC de diferentes tipologias. Três estratégias geométricas são exploradas durante o desenvolvimento; a mais adequada é selecionada como estratégia primária do método.

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
| **geometry4Sharp** | `geometry4Sharp` | Mesh 3D (`DMesh3`), ray casting, BVH, normais de face — namespace `g4`; fork ativo de `geometry3Sharp` (sucessor de fato no NuGet) | Core + Geometry |
| **NetTopologySuite** | `NetTopologySuite` | Geometria 2D, operações de containment e projeção em plano | Geometry |
| **DBSCAN** | `DBSCAN` (NuGet) | Clustering de normais sobre a esfera de Gauss | Algorithms |
| **QuikGraph** | `QuikGraph` | Grafo de adjacência espacial, componentes conectados | Algorithms |
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

```csharp
/// IDs da hierarquia espacial IFC (Project → Site → Building → Storey).
/// Core só conhece os 3 IDs. Qualquer outro metadado (Pset, Name, Material, Tag,
/// relações IFC) é obtido via IIfcProductResolver na camada Ifc (ADR-10).
public readonly record struct BuildingElementContext(
    string? SiteId = null,
    string? BuildingId = null,
    string? StoreyId = null);
```

### BuildingElement — átomo classificável (ADR-08, ADR-11)

```csharp
/// Unidade atômica que os algoritmos classificam em fachadas.
/// SEMPRE tem geometria (invariante do tipo — eliminado o estado "mesh vazio").
/// Sem IIfcProduct: Core não depende de xBIM.
public sealed class BuildingElement : IEquatable<BuildingElement>
{
    public required string GlobalId { get; init; }
    public required string IfcType { get; init; }       // "IfcWall", "IfcWindow"…
    public required DMesh3 Mesh { get; init; }
    public BuildingElementContext Context { get; init; }
    public string? GroupGlobalId { get; init; }         // back-ref opcional ao Group (string evita ciclo em JSON)

    public bool Equals(BuildingElement? other)
        => other is not null && GlobalId == other.GlobalId;
    public override bool Equals(object? obj) => Equals(obj as BuildingElement);
    public override int GetHashCode() => GlobalId.GetHashCode();
}
```

**Por que anêmico (sem `BoundingBox`/`Centroid` cacheados)?** Simplicidade e imutabilidade por construção. Quem precisa, chama `element.Mesh.GetBounds().Center` no ponto de uso — `geometry4Sharp` já caminha o mesh uma vez e o custo é negligível frente ao DBSCAN/ray casting.

**Por que `IEquatable<BuildingElement>` por `GlobalId`?** Usar `HashSet<BuildingElement>`, `Distinct()` e `Dictionary<BuildingElement, T>` sem lambdas de key selector. `GlobalId` é identidade natural do IFC.

**Por que `required init` e não construtor?** Construção por *object initializer* deixa os testes legíveis (`new BuildingElement { GlobalId = "…", IfcType = "IfcWall", Mesh = mesh }`) e obriga cada campo a ser fornecido. `readonly record struct` em `Context` permite defaults nulos sem boilerplate.

**Por que `sealed class` e não `record`?** `DMesh3` não implementa value equality — `record` geraria equality sintética que compara `Mesh` por referência. Equality aqui é por identidade IFC (`GlobalId`), então implementamos explicitamente.

### BuildingElementGroup — agregador organizacional (ADR-11)

```csharp
/// Agregador IFC (IfcCurtainWall, IfcStair, IfcRamp, IfcRoof composto).
/// Não é classificado — serve a rastreabilidade e relatório.
/// Pode ter geometria própria (raro: ArchiCAD inclui; Revit geralmente não).
public sealed class BuildingElementGroup : IEquatable<BuildingElementGroup>
{
    public required string GlobalId { get; init; }
    public required string IfcType { get; init; }
    public BuildingElementContext Context { get; init; }
    public DMesh3? OwnMesh { get; init; }                              // raro, opcional
    public required IReadOnlyList<BuildingElement> Elements { get; init; }

    public bool Equals(BuildingElementGroup? other)
        => other is not null && GlobalId == other.GlobalId;
    public override bool Equals(object? obj) => Equals(obj as BuildingElementGroup);
    public override int GetHashCode() => GlobalId.GetHashCode();
}
```

**Por que separar `Element` e `Group`?** Um modelo único com `Mesh` opcional + `Children[]` opcional cria estados inválidos (átomo com children, agregador sem children). O split elimina isso por construção: `Element` sempre tem mesh, `Group` sempre tem `Elements` não-vazia.

### ModelLoadResult

```csharp
public sealed record ModelLoadResult(
    IReadOnlyList<BuildingElement> Elements,
    IReadOnlyList<BuildingElementGroup> Groups);
```

### Face — superfície atômica exterior (ADR-04)

```csharp
/// Conjunto de triângulos de um BuildingElement que pertencem a um mesmo plano.
/// Inferida geometricamente — não existe no IFC.
/// Referência direta a BuildingElement para rastreabilidade forte.
public sealed class Face
{
    public BuildingElement Element { get; }
    public IReadOnlyList<int> TriangleIds { get; }   // índices na DMesh3 do elemento
    public Plane3d FittedPlane { get; }              // plano ajustado por PCA
    public Vector3d Normal => FittedPlane.Normal;    // derivada
    public double Area { get; }
    public Vector3d Centroid { get; }
}
```

**Rastreabilidade sem duplicação:** `Face` não armazena `DMesh3`. Os triângulos são lidos por `Element.Mesh.GetTriangle(id)` para cada `id in TriangleIds`. `face.Element.GlobalId` já dá o link ao IFC sem lookup externo.

### Envelope — casca + faces exteriores

```csharp
/// Resultado do Stage 1: casca geométrica + faces exteriores com rastreabilidade.
/// É input para o IFacadeGrouper — não contém Facade[].
public sealed class Envelope
{
    public DMesh3 Shell { get; }                    // casca geométrica (malha fundida)
    public IReadOnlyList<Face> Faces { get; }       // faces exteriores com rastreabilidade
    public IEnumerable<BuildingElement> Elements
        => Faces.Select(f => f.Element).Distinct(); // IEquatable<BuildingElement> por GlobalId
}
```

### Facade — região de superfície por plano dominante

```csharp
/// Região contínua da superfície exterior do envoltório, definida por um
/// plano dominante detectado por DBSCAN sobre a esfera de Gauss.
/// Fachada é artefato da SUPERFÍCIE (Faces[]), não agrupamento de elementos.
/// BuildingElements têm relação muitos-para-muitos com Facade:
///   - Um elemento de canto pode pertencer a 2+ fachadas (via Faces diferentes)
///   - Uma fachada contém múltiplos elementos heterogêneos
/// Produzido pelo IFacadeGrouper.
public sealed class Facade
{
    public string Id { get; }
    public Envelope Envelope { get; }               // referência ao parent
    public IReadOnlyList<Face> Faces { get; }       // região de superfície — unidade primária
    public DMesh3 FacadeShell { get; }              // casca desta fachada
    public Vector3d DominantNormal { get; }
    public double AzimuthDegrees { get; }

    /// Elementos IFC desta fachada (possuem ≥1 Face nesta região).
    /// Um mesmo elemento pode aparecer em outra Facade se tiver Faces com normal diferente.
    public IEnumerable<BuildingElement> Elements
        => Faces.Select(f => f.Element).Distinct();
}
```

### Interfaces do pipeline

```csharp
// Port de carregamento — implementado em Ifc, definido em Core (DIP)
public interface IModelLoader
{
    ModelLoadResult Load(string path);
}

// Filtro de tipos IFC — configurável (ADR-05)
public interface IElementFilter
{
    bool Include(string ifcType);
}

public sealed class DefaultElementFilter : IElementFilter
{
    private static readonly HashSet<string> DefaultIncludes = new(StringComparer.Ordinal)
    {
        "IfcWall", "IfcWallStandardCase", "IfcWindow", "IfcDoor",
        "IfcCurtainWall", "IfcCurtainWallPanel",
        "IfcSlab", "IfcRoof", "IfcColumn", "IfcBeam",
        "IfcRailing", "IfcStairFlight", "IfcRampFlight",
        "IfcMember", "IfcPlate", "IfcCovering"
    };

    private readonly IReadOnlySet<string> _includes;
    public DefaultElementFilter(IReadOnlySet<string>? includes = null)
        => _includes = includes ?? DefaultIncludes;

    public bool Include(string ifcType) => _includes.Contains(ifcType);
}

// Stage 1 — detecta elementos exteriores, produz Envelope
public interface IDetectionStrategy
{
    DetectionResult Detect(IReadOnlyList<BuildingElement> elements);
}

// Stage 2 — agrupa faces do Envelope em fachadas
public interface IFacadeGrouper
{
    IReadOnlyList<Facade> Group(Envelope envelope);
}
```

### Acesso cru ao IIfcProduct (ADR-10)

Mora na camada Ifc. Viewer, Cli e testes importam quando precisam de metadados IFC não previstos em `BuildingElementContext` (properties de `Pset_*`, material, tag, relações como `IfcRelConnectsPathElements`):

```csharp
// Ifc/IIfcProductResolver.cs
public interface IIfcProductResolver
{
    IIfcProduct? Resolve(string globalId);
}

// Ifc/XbimIfcProductResolver.cs
public sealed class XbimIfcProductResolver : IIfcProductResolver, IDisposable
{
    private readonly IfcStore _store;
    private readonly Dictionary<string, IIfcProduct> _index;

    public XbimIfcProductResolver(IfcStore store)
    {
        _store = store;
        _index = store.Instances.OfType<IIfcProduct>()
                                .ToDictionary(p => p.GlobalId, StringComparer.Ordinal);
    }

    public IIfcProduct? Resolve(string globalId) =>
        _index.TryGetValue(globalId, out var p) ? p : null;

    public void Dispose() => _store.Dispose();
}
```

Index em `Dictionary` evita busca linear em modelos com milhares de elementos. Lifetime: o resolver precisa do `IfcStore` aberto; gerenciar via `using` ou escopo de DI.

### Reporting

```csharp
/// Resultado do Stage 1: Envelope + classificação por elemento.
public sealed class DetectionResult
{
    public Envelope Envelope { get; }
    public IReadOnlyList<ElementClassification> Classifications { get; }
}

/// Classificação de um elemento individual.
public sealed class ElementClassification
{
    public BuildingElement Element { get; }
    public bool IsExterior { get; }
    public double Confidence { get; }
    public IReadOnlyList<string> Reasons { get; }
    public IReadOnlyList<Face> ExternalFaces { get; }
}
```

---

## Estrutura do Projeto (6 projetos + testes)

```
IfcEnvelopeMapper/
├── docs/
│   └── plano.md                          ← este arquivo
├── scripts/
│   └── run-from-temp.ps1                 ← workaround Google Drive Streaming (xBIM native DLLs)
│
├── src/
│   ├── IfcEnvelopeMapper.Core/           ← domínio puro + interfaces (ports)
│   │   ├── Building/
│   │   │   ├── BuildingElement.cs        ← átomo classificável (ADR-11)
│   │   │   ├── BuildingElementGroup.cs   ← agregador organizacional (ADR-11)
│   │   │   └── BuildingElementContext.cs ← record struct: Site/Building/Storey IDs (ADR-08)
│   │   ├── Envelope/
│   │   │   ├── Envelope.cs
│   │   │   ├── Facade.cs
│   │   │   └── Face.cs                   ← Element + TriangleIds + FittedPlane (ADR-04)
│   │   ├── Pipeline/
│   │   │   ├── IModelLoader.cs
│   │   │   ├── ModelLoadResult.cs        ← record (Elements, Groups) (ADR-11)
│   │   │   ├── IElementFilter.cs         ← filtro de tipos IFC (ADR-05)
│   │   │   ├── DefaultElementFilter.cs
│   │   │   ├── IDetectionStrategy.cs
│   │   │   └── IFacadeGrouper.cs
│   │   └── Reporting/
│   │       ├── DetectionResult.cs
│   │       └── ElementClassification.cs
│   │   [deps: geometry4Sharp]
│   │
│   ├── IfcEnvelopeMapper.Geometry/       ← operações geométricas stateless
│   │   └── GeometricOps.cs
│   │   [deps: Core, geometry4Sharp, NetTopologySuite]
│   │
│   ├── IfcEnvelopeMapper.Ifc/            ← integração xBIM
│   │   ├── XbimModelLoader.cs            ← implementa IModelLoader
│   │   ├── IIfcProductResolver.cs        ← acesso cru ao IIfcProduct (ADR-10)
│   │   └── XbimIfcProductResolver.cs
│   │   [deps: Core, Xbim.Essentials, Xbim.Geometry]
│   │
│   ├── IfcEnvelopeMapper.Algorithms/     ← estratégias de detecção + agrupamento
│   │   ├── Strategies/
│   │   │   ├── NormalsStrategy.cs
│   │   │   ├── RayCastingStrategy.cs
│   │   │   └── VoxelFloodFillStrategy.cs
│   │   └── Grouping/
│   │       └── DbscanFacadeGrouper.cs    ← implementa IFacadeGrouper
│   │   [deps: Core, Geometry, DBSCAN, QuikGraph]
│   │
│   ├── IfcEnvelopeMapper.Cli/            ← entry point, output writers
│   │   ├── Commands/
│   │   │   └── DetectCommand.cs          ← orquestra o pipeline
│   │   ├── Output/
│   │   │   ├── JsonReportWriter.cs
│   │   │   └── BcfWriter.cs              ← mantido em paralelo ao Viewer (ADR-06)
│   │   └── Program.cs
│   │   [deps: Core, Ifc, Algorithms, System.CommandLine, Microsoft.Extensions.Logging]
│   │
│   └── IfcEnvelopeMapper.Viewer/         ← visualizador web Blazor + three.js (ADR-07)
│       ├── Components/                   ← render 3D, inspeção por elemento
│       ├── Editing/                      ← edição manual de rotulação (isolada do Core)
│       └── Export/                       ← BCF export (via iabi.BCF ou equivalente)
│       [deps: Core, Ifc, Algorithms, iabi.BCF]
│
├── tests/
│   └── IfcEnvelopeMapper.Tests/          ← xUnit + FluentAssertions
│       ├── Building/                     ← BuildingElement, Group, Context
│       ├── Geometry/                     ← plane fitting, clustering
│       ├── Ifc/                          ← loader contra fixtures IFC
│       ├── Algorithms/                   ← strategies + grouper
│       └── Regression/                   ← snapshot tests (expected-report.json)
│
├── data/
│   ├── models/                           ← arquivos IFC para testes
│   ├── results/                          ← outputs JSON gerados pela CLI
│   └── ground-truth/                     ← rotulação manual por especialistas (CSV)
│
├── IfcEnvelopeMapper.slnx
└── README.md
```

### Por que 6 projetos?

`Core` concentra o domínio e as interfaces sem depender de infraestrutura (exceto `geometry4Sharp` para tipos geométricos). `Geometry` isola operações geométricas puras, reutilizáveis entre strategies. `Ifc` encapsula toda a complexidade do xBIM — tanto o carregamento quanto o acesso ad-hoc a metadados IFC via `IIfcProductResolver` (ADR-10) —, e pode ser substituído por outra biblioteca de leitura IFC sem tocar o domínio. `Algorithms` contém as strategies e o agrupamento — a parte mais experimental do projeto. `Cli` é um dos dois pontos de entrada e o lugar dos writers de relatório (JSON + BCF). `Viewer` é o segundo ponto de entrada: visualizador web que consome o mesmo JSON produzido pela CLI e permite render, inspeção, edição manual de rotulação e export BCF complementar (ADR-07).

### Dependency Inversion

**`IModelLoader` fica em Core, não em Ifc.** A interface pertence ao consumidor, não ao provedor. `XbimModelLoader` implementa `IModelLoader` e fica em Ifc; Core não sabe que xBIM existe.

**`IFacadeGrouper` e `IDetectionStrategy` ficam em Core, não em Algorithms.** `DbscanFacadeGrouper` e as strategies implementam as interfaces e ficam em Algorithms.

**`IElementFilter` fica em Core** (ADR-05). `DefaultElementFilter` com lista padrão fica em Core; `XbimModelLoader` recebe a instância por construtor.

**`IIfcProductResolver` fica em Ifc, não em Core** (ADR-10). A interface existe para permitir que Viewer, Cli ou testes acessem o `IIfcProduct` cru sem acoplar Core ao xBIM — quem importa o resolver já depende de xBIM por definição.

**Sem `IReportWriter` em Core.** A CLI produz um `DetectionResult` e chama writers concretos. Nenhuma abstração é necessária neste ponto.

### Diagrama de dependências (sem circular)

```
Core ← Geometry ← Algorithms ← Cli, Viewer
Core ← Ifc ──────────────────↗ ↗
Core ←──────────────────────────
```

`Viewer` depende de `Core + Ifc + Algorithms` mas não é dependência de ninguém. `Tests` depende de todos os projetos de `src/`.

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
    │  Estratégia selecionada durante desenvolvimento:
    │
    │  Candidata 1: NormalsStrategy
    │    → calcula proporção de faces com normal "para fora"
    │    → threshold configurável (--angle-tolerance)
    │
    │  Candidata 2: RayCastingStrategy
    │    → BVH sobre todos os triângulos do modelo
    │    → raio a partir de cada face na direção da normal
    │    → face exposta = raio não intercepta outro elemento
    │    → configurável (--ray-count, --hit-ratio)
    │
    │  Candidata 3: VoxelFloodFillStrategy
    │    → discretiza modelo em voxel grid 3D
    │    → flood-fill a partir do exterior
    │    → elemento com face adjacente a voxel "exterior" = exterior
    │    → configurável (--voxel-size)
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
var model    = loader.Load(modelPath);                     // IModelLoader → ModelLoadResult
var result   = strategy.Detect(model.Elements);            // IDetectionStrategy → DetectionResult
var facades  = grouper.Group(result.Envelope);             // IFacadeGrouper → Facade[]
var report   = ReportBuilder.Build(result, facades, model.Groups, runMeta);
writer.WriteJson(report, outputPath);
```

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

Três estratégias candidatas são exploradas. A mais adequada é selecionada durante desenvolvimento.

#### Estratégia 1A: Heurísticas de Normais

```
FUNÇÃO NormalsDetect(elementos, anguloTolerancia) → DetectionResult
    // Ref: Lu et al. (2022) — classificação de superfícies por orientação de normal
    // Ref: Sacks et al. (2017) — operadores de "face externa"
    centroideEdificio ← Média(elementos.Select(e → e.Centroid))

    classificacoes ← []
    facesExteriores ← []

    PARA CADA elem EM elementos:
        mesh ← elem.Mesh
        facesExt ← []
        areaExterior ← 0
        areaTotal ← 0

        PARA CADA tri EM mesh.Triangulos:
            normal ← tri.Normal
            centro ← tri.Centroide
            areaTotal += tri.Area

            // Critério: normal aponta para fora do edifício?
            direcaoParaFora ← (centro - centroideEdificio).Normalizado
            angulo ← AnguloEntre(normal, direcaoParaFora)
            SE angulo < anguloTolerancia:
                facesExt.Add(tri)
                areaExterior += tri.Area

        // Agrupar triângulos coplanares em Faces atômicas
        facesAgrupadas ← AgruparPorPlanoAjustado(facesExt, elem)
        // Cada Face: {Element, TriangleIds, FittedPlane, Normal, Area, Centroid}

        razaoExterior ← areaExterior / areaTotal
        confianca ← CalcularConfianca(razaoExterior)
        ehExterior ← razaoExterior > LIMIAR_MINIMO

        classificacoes.Add(ElementClassification(
            element: elem,
            isExterior: ehExterior,
            confidence: confianca,
            externalFaces: facesAgrupadas,
            reasons: [FormatarRazoes(razaoExterior, angulo)]
        ))

        SE ehExterior:
            facesExteriores.AddRange(facesAgrupadas)

    envelope ← Envelope(faces: facesExteriores)
    RETORNAR DetectionResult(envelope, classificacoes)
```

#### Estratégia 1B: Ray Casting

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
        // ... classificação análoga à Estratégia 1A ...

    RETORNAR DetectionResult(envelope, classificacoes)
```

#### Estratégia 1C: Voxel + Flood-Fill

```
FUNÇÃO VoxelFloodFillDetect(elementos, tamanhoVoxel) → DetectionResult
    // Ref: van der Vaart (2022) — IFC_BuildingEnvExtractor (voxelização + flood-fill)
    // Ref: Liu et al. (2021) — ExteriorTag (anotação voxel em IFC)

    // Discretizar modelo em grade 3D
    bbox ← BoundingBoxGlobal(elementos) expandida por 2 * tamanhoVoxel
    grid ← VoxelGrid3D(bbox, tamanhoVoxel)

    // Rasterizar: marcar voxels ocupados por geometria
    PARA CADA elem EM elementos:
        PARA CADA tri EM elem.Mesh.Triangulos:
            voxelsOcupados ← Voxelizar(tri, tamanhoVoxel)
            PARA CADA v EM voxelsOcupados:
                grid[v].Ocupado ← VERDADEIRO
                grid[v].Elementos.Add(elem.GlobalId)

    // Flood-fill BFS a partir de um voxel exterior (canto da grade)
    semente ← grid.VoxelLivre(canto)
    fila ← [semente]
    ENQUANTO fila NÃO VAZIA:
        v ← fila.RemoverPrimeiro()
        grid[v].Exterior ← VERDADEIRO
        PARA CADA vizinho EM grid.Vizinhos26(v):
            SE vizinho NÃO Ocupado E NÃO visitado:
                fila.Add(vizinho)

    // Classificar: elemento com face adjacente a voxel exterior = exterior
    PARA CADA elem EM elementos:
        voxelsDoElemento ← grid.VoxelsOcupadosPor(elem.GlobalId)
        temFaceExterior ← FALSO
        PARA CADA v EM voxelsDoElemento:
            SE algum Vizinho26(v) tem Exterior == VERDADEIRO:
                temFaceExterior ← VERDADEIRO
                BREAK

        // Para faces atômicas: triângulos cuja normal aponta para voxel exterior
        facesAgrupadas ← ExtrairFacesVoltadasParaExterior(elem, grid)
        // ... classificação análoga ...

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
            computed: { isExterior: c.IsExterior, confidence: c.Confidence },
            facadeIds: facades.Where(f → f.Elements.Contains(c.Element))
                             .Select(f → f.Id),  // pode ser múltiplos!
            reasons: c.Reasons
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

> O trabalho propõe **um método** e seleciona a estratégia primária durante o desenvolvimento.
> Esta tabela documenta as alternativas investigadas como base para a decisão fundamentada
> e para o capítulo de Discussão do TCC.

| Critério | Normais (1A) | Ray Casting (1B) | Voxel + Flood-Fill (1C) |
|---|---|---|---|
| **Referência principal** | Lu et al. (2022); Sacks et al. (2017) | Ying et al. (2022) | van der Vaart (2022); Liu et al. (2021) |
| **Princípio** | Orientação do vetor normal em relação ao centroide do edifício | Visibilidade: raio a partir da face na direção da normal escapa sem interceptar outro elemento | Adjacência a voxel exterior identificado por flood-fill |
| **Complexidade temporal** | O(n) — linear no número de triângulos | O(n · k · log m) — k raios por face, log m para BVH | O(V) + O(n) — V voxels no grid, n triângulos para rasterização |
| **Dependência de geometria global** | Baixa — apenas centroide | Alta — BVH com todos os triângulos | Alta — grid discreto do modelo inteiro |
| **Sensibilidade a concavidades** | Alta — centroide pode estar fora de edificações em L/U | Baixa — raio testa visibilidade direta | Baixa — flood-fill contorna concavidades |
| **Precisão em protuberâncias** | Baixa — faces laterais de balanços são mal classificadas | Alta — cada face é testada individualmente | Média — depende do tamanho do voxel |
| **Parametrização** | `angle-tolerance` (graus) | `ray-count`, `hit-ratio` | `voxel-size` (metros) |
| **Consumo de memória** | Baixo | Médio (BVH) | Alto (grade 3D) |
| **Rastreabilidade** | Preservada nativamente | Preservada nativamente | Requer mapeamento voxel→elemento |
| **Validação na literatura** | Parcial (Lu: binário, sem fachadas) | Forte (Ying: 99%+ em ray tracing recursivo) | Forte (van der Vaart: casca multi-LoD) |

**Critério de seleção:** A estratégia primária será escolhida com base nos resultados preliminares em 2-3 modelos IFC de diferentes tipologias (planta retangular, planta em L, geometria complexa). Métrica de decisão: F1-score macro-médio na tarefa de classificação binária exterior/interior.

---

## IsExternal e LoD — Decisões de Design

### IsExternal não pertence ao BuildingElement

A propriedade `IsExternal` do IFC (`Pset_WallCommon`, etc.) é **não confiável** em modelos reais. O IFC_BuildingEnvExtractor da TU Delft ignora-a por padrão (`ignoreIsExternal_ = true`).

O algoritmo *computa* exterioridade — incluir `IsExternal` no modelo de domínio criaria dualidade confusa. A propriedade é extraída opcionalmente pelo `XbimModelLoader` e inserida como campo de **comparação** no relatório JSON:

```json
{
  "globalId": "2O2Fr$t4X7Zf8NOew3FL9r",
  "computed": { "isExterior": true, "confidence": 0.92 },
  "declared": { "isExternal": true },
  "agreement": true
}
```

Isto permite uma métrica de validação: *"Em N% dos casos, a classificação geométrica concordou com a propriedade IsExternal declarada."*

### Sem sistema de LoD

A ferramenta sempre opera com rastreabilidade completa (equivalente ao LoD 3.2 do IFC_BuildingEnvExtractor). O conceito de LoD do BuildingEnvExtractor é sobre formato de exportação CityJSON, não sobre qualidade de detecção. A questão de pesquisa exige *"preservando rastreabilidade semântica"* — LoDs inferiores a perdem.

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

### ADR-07 — Viewer Completo: render + edição + export BCF

**Decisão.** Viewer implementa render 3D dos meshes coloridos por fachada, inspeção por elemento, filtro exterior/interior, **edição manual de rotulação** e **export BCF**. Recorte "Completo" (vs. "MVP só-visualização" que foi rejeitado).

**Motivo.** Evita dependência de BIMCollab/BIMvision. Ferramenta vira *curadoria assistida* — contribuição mais forte para a banca. BCF editado cria *loop* natural com outras ferramentas AEC. Responde ao critério #4 do TCC (≥4 ferramentas BIM): o próprio Viewer é uma delas.

**Consequência.** Risco alto de cronograma — ver seção Viewer (§ Viewer) para stage gates, spike técnico e plano de contingência se F1<0.60 em set/2026.

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
    "strategy": "normals",
    "grouper": "dbscan",
    "timestamp": "2026-04-10T14:30:00Z",
    "parameters": {
      "angleTolerance": 15,
      "confidence": 0.0
    }
  },
  "summary": {
    "totalElements": 142,
    "exteriorElements": 38,
    "facadeCount": 4,
    "precision": null,
    "recall": null,
    "f1": null
  },
  "classifications": [
    {
      "globalId": "2O2Fr$t4X7Zf8NOew3FLne",
      "ifcType": "IfcWall",
      "computed": {
        "isExterior": true,
        "confidence": 0.87,
        "facadeIds": ["facade-01"]
      },
      "declared": {
        "isExternal": true
      },
      "agreement": true,
      "reasons": [
        "0.71 das faces apontam para fora",
        "normal alinhada com plano dominante (ângulo=12.3°)"
      ]
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

Quando `--ground-truth` é fornecido, `precision`, `recall` e `f1` são preenchidos automaticamente.

**Bloco `aggregates`.** Produzido a partir de `ModelLoadResult.Groups` (ADR-11). Lista cada `BuildingElementGroup` com o conjunto de fachadas em que seus Elements participaram — útil para relatórios agrupados por cortina de vidro, escada, etc.

**Bloco `diagnostics`.** Coleta warnings do `XbimModelLoader` e dos Stages 1/2: elementos descartados por mesh vazio, triangulações convertidas por fan-fallback, faces *noise* do DBSCAN. Alimentado por `ILogger<T>` com sink em memória. Ver seção Determinismo e estratégia de testes.

---

## Viewer — Curadoria Assistida (ADR-07)

Segundo ponto de entrada do projeto, ao lado da CLI. Stack: **ASP.NET Core Blazor Server + three.js** (via JS interop). Consome o mesmo `report.json` da CLI + o IFC original (para render da geometria).

### Responsabilidades

| Componente | Responsabilidade |
|---|---|
| `Components/` | Render 3D do mesh por `BuildingElement`, colorido por `facadeId`. Camera controls, filtro exterior/interior, inspeção por elemento (GlobalId, IfcType, propriedades IFC via `IIfcProductResolver` — ADR-10). |
| `Editing/` | Camada de edição isolada. Usuário reclassifica elemento / altera `facadeId`. Estado mutável *só aqui*; Core e Algorithms permanecem imutáveis. Diff serializável em JSON patch. |
| `Export/` | Geração de arquivo `.bcfzip` a partir das rotulações curadas. Usa `iabi.BCF` (NuGet) ou, se indisponível, BCF mínimo (tópicos + viewpoints + comentários). |

### Integração com o pipeline

```
[CLI]        detect → report.json
[Viewer]     carrega IFC + report.json
             → render colorido por fachada
             → usuário edita rotulação (opcional)
             → exporta BCF a partir do estado editado
```

Viewer **nunca re-executa o pipeline**. Isso preserva a relação clara *CLI = algoritmo automatizado*, *Viewer = revisão humana*. Re-execução sobre regiões editadas é *Trabalho Futuro* (§ Trabalhos Futuros).

### Stage gates e contingência

Viewer Completo tem risco alto de cronograma. Para não comprometer o pipeline:

1. **Spike técnico — 1 semana, mai/2026.** Carregar 1 mesh, renderizar com three.js via Blazor interop, clicar num elemento e ler o GlobalId no servidor. **Decisão go/no-go** ao fim. Se *no-go*: Viewer vira MVP estático (render + cores, sem edição, sem BCF) e BCF fica só no CLI.

2. **Stage gate bloqueante — Viewer não começa até pipeline produzir JSON válido** em ≥1 fixture. Atualmente: pipeline em Fase 0 (loader só). Viewer fica congelado até Fase 1 completa (Stage 1 + Stage 2 + `JsonReportWriter`).

3. **Stage gate de qualidade — set/2026.** Se F1 em fixtures estiver <0.60, Viewer é reduzido a MVP (render + cores, sem edição/BCF). Decisão documentada em ADR e commitada ao `docs/plano.md`.

### Cronograma

| Período | Entrega |
|---|---|
| mai/2026 (1 sem) | Spike técnico Blazor + three.js + mesh render. Go/no-go. |
| jun–ago/2026 | **Foco absoluto em pipeline + JSON.** Viewer congelado. |
| set–out/2026 | Viewer Fase 1 — render colorido + inspeção + filtros. Stage gate qualidade (F1) ao fim de set. |
| nov–dez/2026 | Viewer Fase 2 — edição manual + export BCF. |
| jan/2027 | Testes de usabilidade com especialistas AEC + fixes. |
| fev/2027 | Etapa 4 do TCC (Entrega). |

### Riscos e mitigações

| Risco | Mitigação |
|---|---|
| Viewer compete com pipeline pelo tempo. | Stage gate bloqueante — só inicia após pipeline produzir JSON. |
| Edição mutável cria tensão com imutabilidade do Core. | `Editing/` em camada separada. Core/Algorithms recebem apenas `IReadOnlyList<…>`. |
| Export BCF 2.1/3.0 não trivial. | `iabi.BCF` NuGet. Fallback: BCF mínimo (tópicos + viewpoints básicos). |
| Blazor ↔ three.js interop tem curva. | Spike de 1 semana antes de commitment. Se falhar, pivota para Razor Components + canvas simples ou congela Viewer em MVP. |
| Edição sem undo/redo frustra usuário. | Command pattern básico ou limitação explícita: sessão = 1 arquivo, sem histórico. |

### Trabalhos Futuros (fora do escopo Completo)

- Ingestão de BCF externo para re-calibrar algoritmo (loop bidirecional).
- Re-execução do pipeline sobre regiões editadas manualmente.
- Histórico / versionamento de rotulações.
- Multi-usuário e colaboração simultânea.

---

## Interface CLI v2

```
ifcenvmapper detect <model.ifc> [opções]

Opções globais:
  --strategy      <normals|raycast|voxelflood>   Estratégia de detecção     [padrão: normals]
  --grouper       <dbscan|directional>           Agrupamento em fachadas    [padrão: dbscan]
  --output        <path>                         Diretório de saída         [padrão: ./output]
  --format        <json|bcf|both>                Formato do relatório       [padrão: json]
  --confidence    <0.0–1.0>                      Confiança mínima           [padrão: 0.0]
  --ground-truth  <labels.csv>                   Calcula Precisão/Recall/F1 (opcional)
  --verbose                                      Logging detalhado

Opções específicas por estratégia:
  --angle-tolerance <graus>     [normals]      Desvio máximo da vertical  [padrão: 15]
  --ray-count       <int>       [raycast]      Raios por centroide        [padrão: 64]
  --hit-ratio       <float>     [raycast]      Razão mínima exterior      [padrão: 0.5]
  --voxel-size      <metros>    [voxelflood]   Aresta do voxel            [padrão: 0.5]

Exemplos:
  ifcenvmapper detect duplex.ifc
  ifcenvmapper detect duplex.ifc --strategy raycast --ray-count 128 --output results/
  ifcenvmapper detect duplex.ifc --ground-truth data/ground-truth/duplex.csv
  ifcenvmapper detect duplex.ifc --format both
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

### Fase 1 — Modelo refinado + testes-base (ATUAL — abr–mai/2026)
**Meta:** absorver ADRs 02-11 no código e estabelecer infraestrutura de testes.
**Critério de sucesso:** `dotnet test` passa com ≥20 testes; loader retorna `ModelLoadResult(Elements, Groups)` determinístico.

**Domínio (Core):**
- [ ] `BuildingElementContext` (record struct, ADR-08)
- [ ] `BuildingElement` anêmico (required init, IEquatable, ADR-08 + ADR-11)
- [ ] `BuildingElementGroup` (ADR-11)
- [ ] `ModelLoadResult` (record)
- [ ] `Face` com `Element + TriangleIds + FittedPlane` (ADR-04)
- [ ] `Envelope`, `Facade` (mantidos do plano)
- [ ] `DetectionResult`, `ElementClassification`

**Pipeline (Core):**
- [ ] `IModelLoader` retornando `ModelLoadResult`
- [ ] `IElementFilter` + `DefaultElementFilter` (ADR-05)
- [ ] `IDetectionStrategy`, `IFacadeGrouper`

**Loader (Ifc):**
- [ ] `XbimModelLoader` v1: split Elements/Groups, filtro injetado, 2-level assertion (ADR-09)
- [ ] `IIfcProductResolver` + `XbimIfcProductResolver` (ADR-10)
- [ ] Descarte de Elements sem geometria + log warning (§ Diagnostics)

**Testes:**
- [ ] `tests/IfcEnvelopeMapper.Tests/` scaffold (xUnit + FluentAssertions)
- [ ] `BuildingElementTests`, `BuildingElementGroupTests`, `FaceTests`
- [ ] `XbimModelLoaderTests` (integração com `duplex.ifc` + fixture com IfcCurtainWall)
- [ ] 1 teste de regressão por snapshot em `data/models/cube.ifc`

**Infra:**
- [ ] `.github/workflows/build.yml` (dotnet restore/build/test)
- [ ] Error handling tipado: `IfcLoadException`, `IfcGeometryException`
- [ ] `.gitignore` ajustado (não bloquear fixtures `*.json` / `*.bcf`)

---

### Fase 2 — Pipeline end-to-end (mai–ago/2026)
**Meta:** `dotnet run detect duplex.ifc --strategy normals` produz JSON v2 completo.
**Critério de sucesso:** JSON com `summary`, `classifications`, `facades`, `aggregates`, `diagnostics`; WWR calculado; F1 opcional via `--ground-truth`.

**Detecção (Stage 1):**
- [ ] `GeometricOps`: plane fitting PCA, face normals, clustering angular, building centroid
- [ ] `NormalsStrategy : IDetectionStrategy`
- [ ] `DetectionResult` (Envelope + ElementClassification[])

**Agrupamento (Stage 2):**
- [ ] `DbscanFacadeGrouper : IFacadeGrouper` (DBSCAN + QuikGraph)
- [ ] Determinismo: seed fixa, ordenação estável (§ Determinismo)

**Saída (Cli):**
- [ ] `ReportBuilder` + `JsonReportWriter` (schema v2 com `aggregates` + `diagnostics`)
- [ ] `BcfWriter` (ADR-06) — escopo mínimo: tópicos + viewpoints + GlobalId
- [ ] CSV ground-truth loader + Precisão/Recall/F1/Kappa
- [ ] `System.CommandLine`: flags documentadas (§ CLI v2)
- [ ] `ILogger<T>` (Microsoft.Extensions.Logging) para diagnostics

**Marco paralelo — Spike Viewer (1 semana, mai/2026):**
- [ ] Blazor Server scaffold + three.js interop
- [ ] Carregar 1 mesh + render + click → GlobalId no servidor
- [ ] Decisão go/no-go para escopo Completo do Viewer (§ Viewer)

---

### Fase 3 — Estratégias alternativas + seleção (ago–set/2026)
**Meta:** comparar 3 estratégias e justificar a seleção da primária.
**Critério de sucesso:** F1 reportado por estratégia em 2–3 modelos; decisão documentada na dissertação.

- [ ] `RayCastingStrategy` (geometry4Sharp BVH)
- [ ] `VoxelFloodFillStrategy` (VoxelGrid3D + flood-fill BFS)
- [ ] Testes unitários por estratégia
- [ ] Comparação em fixtures + seleção fundamentada
- [ ] **Stage gate qualidade (set/2026):** se F1<0.60 em fixtures → Viewer vira MVP

> Estratégias alternativas podem virar *Trabalho Futuro* se `NormalsStrategy` atingir F1≥0.75 e não houver tempo — decisão consciente, documentada.

---

### Fase 4 — Ground Truth & Avaliação Experimental (out–dez/2026)
**Meta:** validar o método contra rótulos manuais de especialistas.
**Critério de sucesso:** tabela Precisão/Recall/F1/Kappa por modelo e por tipologia; ≥75% concordância entre especialistas.

- [ ] Selecionar 3–5 modelos IFC de tipologias diferentes (planta retangular, L, curva/irregular)
- [ ] Protocolo de rotulação (critérios, ferramenta — provavelmente Viewer, resolução de divergências)
- [ ] Recrutar 5+ profissionais AEC
- [ ] Kappa de Cohen para concordância
- [ ] Tabela de resultados para a dissertação

---

### Fase 5 — Viewer Completo (paralelo à Fase 4)
**Meta:** ferramenta de curadoria assistida (ADR-07).
**Critério de sucesso:** especialista abre IFC+JSON, navega, edita rotulação, exporta BCF que abre em BIMCollab.

**Fase 5A — Render + inspeção (set–out/2026):**
- [ ] `Components/`: render 3D por elemento colorido por fachada
- [ ] Filtro exterior/interior, inspeção (GlobalId, IfcType, `IIfcProductResolver`)
- [ ] Overlay opcional de ground truth CSV

**Fase 5B — Edição + BCF (nov–dez/2026):**
- [ ] `Editing/`: reclassificação manual de elementos, alteração de `facadeId`
- [ ] `Export/`: `iabi.BCF` (ou fallback mínimo) — tópicos + viewpoints + comentários
- [ ] JSON patch serializável (diff entre rotulação automática e curada)

---

### Fase 6 — Entrega (jan–fev/2027)
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

## Próxima Sessão de Trabalho

**Objetivo:** iniciar a Fase 1 — refinar o modelo de domínio segundo ADRs 04-11.

Ordem sugerida (passos pequenos, revisáveis):

1. **Higiene final do repo** — `.gitignore` ajustado (liberar `*.json`/`*.bcf` em `data/fixtures/` e `tests/`), `2027-TCC/00_Coordenacao/status.md` atualizado refletindo Fase 0 concluída.
2. **`Face.cs`** — alinhar com ADR-04 (`Element + TriangleIds + FittedPlane`). Commit pequeno isolado.
3. **`BuildingElementContext` + `BuildingElement` anêmico** — ADR-08. Ajustar `XbimModelLoader` para usar `required init`.
4. **`BuildingElementGroup` + `ModelLoadResult`** — ADR-11. `IModelLoader` passa a retornar `ModelLoadResult`. `Program.cs` adaptado.
5. **`IElementFilter` + `DefaultElementFilter`** — ADR-05. Injetar no loader por construtor.
6. **Loader v1** — agregação 2-níveis (ADR-09) com `Debug.Assert`; descarte de Element sem geometria com log warning.
7. **`IIfcProductResolver`** — ADR-10. Ainda sem consumidor; preparado para Viewer e testes.
8. **Testes-base** — scaffold `IfcEnvelopeMapper.Tests`, primeiros 5-8 testes unitários (BuildingElement, Face, Group, Context), 1 teste de integração com `duplex.ifc`.
9. **CI** — `.github/workflows/build.yml` rodando `dotnet test` no push.

**Arquivo IFC de entrada:** `duplex.ifc` (em `data/models/`). Para fixture com agregador, produzir ou localizar um IFC pequeno com IfcCurtainWall.
