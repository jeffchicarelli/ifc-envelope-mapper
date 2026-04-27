# Plano de Implementação — IfcEnvelopeMapper

> Documento vivo. Atualizar a cada sessão de desenvolvimento.
> Última atualização: 2026-04-26

---

## Sumário do projeto

**Problema.** Identificar elementos de fachada em modelos IFC é trabalho manual em ferramentas BIM. Este TCC investiga se um método **puramente geométrico** (sem usar metadados como `IsExternal`) consegue automatizar essa identificação preservando rastreabilidade ao `GlobalId` IFC de cada elemento.

**Método.** Pipeline em dois estágios:
1. **Detecção** (`IDetectionStrategy`) — três estratégias comparadas:
   *Voxel + flood-fill uniforme* (van der Vaart 2022, ablation baseline),
   *ray casting por face* (Ying 2022, baseline externo),
   *Hierarchical Voxel Flood-Fill* (contribuição original).
2. **Agrupamento** (`IFacadeGrouper`) — DBSCAN sobre esfera de Gauss + grafo de adjacência espacial = `Facade[]`.

**Saídas.** JSON (primário, schema v1 atual; v2 após P4.3 com fachadas; v3 após P6.4 com LoDs), BCF 2.1 (revisão assistida), GLB para debug visual (ADR-17), múltiplos LoDs 0.x–4.x do framework Biljecki/van der Vaart (ADR-15).

**Validação.** Ground truth de especialistas AEC (Fase 8); contagens TP/FP/FN/TN + Precisão/Recall por estratégia × modelo. F1 e Kappa intencionalmente fora — ADR-12.

**Ferramentas auxiliares.** `IfcInspector` (P4.2) — triagem de modelos para selecionar 5–8 candidatos com cobertura de casos adversariais (átrios, pátios abertos, pilotis). Não faz parte do algoritmo de detecção.

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
| **Face** | Unidade atômica de superfície exterior: conjunto de triângulos de um elemento IFC que pertencem a um mesmo plano ajustado. Preserva rastreabilidade ao `Element` de origem. |
| **Plano dominante** | Direção média de um grupo de normais detectado por DBSCAN sobre a esfera de Gauss. Base para o agrupamento de elementos em fachadas. |
| **Ground truth** | Conjunto de rótulos de referência (elementos marcados como fachada / não-fachada) produzido por rotulação manual de especialistas AEC. Base para contagens TP/FP/FN/TN e cálculo de Precisão e Recall. |
| **TP / FP / FN / TN** | Contagens da matriz de confusão de classificação binária. TP (True Positive): classificados como exterior e realmente exteriores. FP (False Positive): classificados como exterior mas são interiores. FN (False Negative): exteriores não detectados. TN (True Negative): interiores corretamente identificados. Estilo de reporte seguindo van der Vaart (2022). |
| **Precisão / Recall** | Métricas derivadas das contagens. Precisão = TP / (TP + FP): dos classificados como exterior, quantos realmente são. Recall = TP / (TP + FN): dos que são exterior, quantos foram encontrados. Definições conforme Ying et al. (2022, Eq. 12–13). F1 e Kappa foram descartados como métricas principais por não aparecerem nas referências canônicas — ver ADR-12 nota. |
| **DBSCAN** | Density-Based Spatial Clustering of Applications with Noise — algoritmo de clustering sem número fixo de grupos. Usado para agrupar normais de faces na esfera de Gauss e detectar planos dominantes. |
| **BVH** | Bounding Volume Hierarchy — estrutura de aceleração espacial para ray casting. |
| **WWR** | Window-to-Wall Ratio — razão entre área de janelas e área total de parede por fachada. Métrica usada como prova de aplicabilidade do método. |
| **Átrio** *(atrium)* | Volume vertical aberto no interior de um edifício, atravessando múltiplos pavimentos. Pode ser coberto (skylight de vidro — topologicamente fechado) ou aberto (poço de luz — topologicamente conectado ao exterior pelo topo). Caso adversarial canônico para detectores de envoltório: ray casting tipicamente classifica paredes do átrio como interior; voxel flood-fill com átrio aberto as classifica corretamente como exterior. Motiva a contribuição HVFF (P5). |
| **Pátio aberto** *(courtyard)* | Cavidade interior de planta baixa em U/L/O, sem cobertura, que cria um anel interno na projeção 2D do footprint. Detectado em P4.2 (Inspector) via análise topológica de `NetTopologySuite`. |

---

## Objetivo

Construir uma ferramenta C#/.NET que identifica automaticamente elementos de fachada em modelos IFC usando **apenas geometria 3D** — sem depender de propriedades ou metadados do modelo.

### Pergunta de pesquisa

> Um esquema de voxelização hierárquica com *flood-fill* supera a voxelização uniforme (van der Vaart 2022) e o *ray casting* (Ying 2022) em precisão e *recall* sobre modelos IFC reais, preservando rastreabilidade ao `GlobalId` do elemento de origem e agregação em fachadas por plano dominante?

### Método

O trabalho propõe e avalia **três estratégias de detecção** em modelos IFC de tipologias distintas, com contagens TP/FP/FN/TN + Precisão/Recall reportadas por estratégia:

1. **Voxelização uniforme com *flood-fill* 3-fases** (van der Vaart 2022) — **ablation baseline**. Grade de voxels cúbicos de lado fixo; `GrowExterior → GrowInterior → GrowVoid` + `FillGaps`. Concluída em P2.
2. ***Ray casting* por face** (Ying 2022) — **baseline externo**. BVH sobre todos os triângulos; raios partindo de cada face na direção da normal; face é exterior se o raio escapa sem interceptar outro elemento. Concluída em P3.
3. ***Hierarchical Voxel Flood-Fill*** — **contribuição original**. Voxelização multi-resolução (octree ou adaptativa): células grandes no espaço livre, refinamento progressivo na vizinhança da casca. Mesmo contrato de classificação do baseline uniforme (TP/FP/FN/TN + Precisão/Recall + rastreabilidade por `GlobalId`), com ganho esperado em precisão em detalhes finos (ex: janelas <300mm) sem inflar o custo total do *flood-fill*. P5 (alvo).

A decisão é defendida em ADR-14 (revisada em 2026-04-24). A comparação algorítmica das três estratégias aparece no capítulo de Resultados.

---

## Stack de Tecnologias

### Linguagem e Runtime

- **C# / .NET 8** — stack profissional do Jeff; xBIM é .NET nativo
- **xUnit + FluentAssertions** — framework de testes

### Bibliotecas Externas

| Biblioteca | NuGet Package | Uso | Projeto | Status |
|---|---|---|---|---|
| **xBIM Essentials** | `Xbim.Essentials` | Leitura de modelos IFC, schema IFC4 | Ifc | ✅ em uso |
| **xBIM Geometry** | `Xbim.Geometry` | Triangulação de geometria IFC via `Xbim3DModelContext` | Ifc | ✅ em uso |
| **geometry4Sharp** | `geometry4Sharp` | Mesh 3D (`DMesh3`), BVH (`DMeshAABBTree3`), normais (`MeshNormals`), plane-fit PCA (`OrthogonalPlaneFit3`), eigen (`SymmetricEigenSolver`), esfera de Gauss (`NormalHistogram`) — namespace `g4`; fork ativo de `geometry3Sharp`. Tri-AABB ausente — implementado via SAT próprio (Akenine-Möller 1997). Mapeamento completo em ADR-13. | Core, Engine | ✅ em uso |
| **SharpGLTF** | `SharpGLTF.Toolkit` | Escrita de GLB (scenes, nodes, per-vertex color, extras) para debug visual. Padrão standard: qualquer browser/CloudCompare/Blender lê (ADR-17). | Engine | ✅ em uso |
| **System.CommandLine** | `System.CommandLine` | Parser de argumentos CLI | Cli | ✅ em uso |
| **Microsoft.Extensions.Logging** | `Microsoft.Extensions.Logging` | Logging ambient via `AppLog` (Console sink em produção; injetado em `XbimServices`) | Core, Cli | ✅ em uso |
| **NetTopologySuite** | `NetTopologySuite` | Geometria **2D apenas** (containment, projeção em plano, `STRtree` 2D para união de polígonos no LoD 0). Não é usado para indexação 3D — ver ADR-13 para queries 3D. | Engine | ⏳ alvo P4.2 (Inspector footprint) e P6.1 (LoD 0.x) |
| **DBSCAN** | `DBSCAN` (NuGet) | Clustering de normais sobre a esfera de Gauss | Engine | ⏳ alvo P4.3 |
| **QuikGraph** | `QuikGraph` | Grafo de adjacência espacial, componentes conectados | Engine | ⏳ alvo P4.3 |

**Política de bibliotecas:** usar bibliotecas externas agora e substituir por implementação própria somente se uma biblioteca não for extensivamente utilizada no projeto. Não prematuramente otimizar.

---

## Modelo de Domínio

### Hierarquia conceitual

```
IfcEntity (abstract — Core/Domain/IfcEntity.cs)
    ├── Element       ← átomo classificável (Wall, Door, Window, Slab…)
    ├── ElementGroup  ← composite (CurtainWall, Roof); Mesh = own + filhos mesclado
    ├── Space                 ← volume habitável (Spatial/)
    └── Storey                ← marcador de elevação (Spatial/)

ModelLoadResult (output do XbimModelLoader.Load)
    ├── Elements[]   ← Element
    ├── Groups[]     ← ElementGroup
    ├── Spaces[]     ← Space
    ├── Storeys[]    ← Storey
    └── Metadata     ← ModelMetadata (schema IFC, ferramenta de autoria, project name)

Envelope (totalidade das faces exteriores com rastreabilidade)
    └── input para → IFacadeGrouper → Facade[]
        └── Face[] (superfície atômica exterior — unidade primária)
            └── Element (rastreável ao IFC via GlobalId)

Relações:
  Facade ↔ Element: muitos-para-muitos (canto participa de 2+ fachadas)
  Element ↔ ElementGroup: muitos-para-um (opcional, via GroupGlobalId)
```

### Interfaces de capacidade

A base `IfcEntity` carrega só identidade (`GlobalId`, `IfcType`, `Name`, igualdade por `GlobalId`). Capacidades geométricas vivem em interfaces ortogonais:

| Interface | Contrato | Implementadores |
|---|---|---|
| `IIfcEntity` | identidade IFC (root) | `Element`, `ElementGroup`, `Space`, `Storey` |
| `IBoxEntity` | `AxisAlignedBox3d BoundingBox` | `Element`, `ElementGroup`, `Space` |
| `IMeshEntity` | `DMesh3 Mesh` | `Element`, `ElementGroup`, `Space` |

`IBoxEntity` e `IMeshEntity` são siblings — não há herança entre elas. `Storey` é puro `IIfcEntity` (sem extent, sem mesh — apenas `Elevation`).

### Decisões de design

**Por que classe abstrata `IfcEntity` + interfaces?** A classe abstrata implementa os campos de identidade uma única vez, sem duplicação de equality boilerplate em cada concrete. As interfaces permitem dispatch polimórfico sobre capacidades (`void Render(IEnumerable<IBoxEntity>)` aceita qualquer mix de Element/Group/Space).

**Por que `BoundingBox` armazenado e não `=> Mesh.GetBounds()`?** O loader popula bbox a partir de `XbimShapeInstance.BoundingBox` (xBIM já em coords mundo) — campo armazenado desacopla o contrato `IBoxEntity` de `IMeshEntity`. Consumidor que precisa só do bbox não força inflação de mesh; future-proofs lazy mesh.

**Por que `ElementGroup.Mesh` é mesclado e não tem `OwnMesh` separado?** O loader funde mesh próprio do composite + meshes dos filhos numa única `DMesh3` no momento do load. Um único contrato (`IMeshEntity.Mesh`); consumidores que queiram filhos individualmente acessam `Group.Elements`.

**Por que `IEquatable<IfcEntity>` por `GlobalId`?** Identidade natural do IFC. Permite usar `HashSet<IfcEntity>`, `Distinct()` e `Dictionary<IfcEntity, T>` sem lambdas de key selector — funciona para qualquer concrete derivada.

**Por que `required init` e não construtor?** Object initializer mantém testes legíveis (`new Space { GlobalId = "...", BoundingBox = bbox, ... }`). `readonly record struct Containment` permite defaults nulos sem boilerplate.

**Por que `sealed class` e não `record`?** `DMesh3` não implementa value equality. Records gerariam equality sintética comparando `Mesh` por referência — errado para identidade IFC. Equality é por `GlobalId`; implementamos explicitamente na base `IfcEntity`.

### Containment

`readonly record struct Containment(string? SiteId, string? BuildingId, string? StoreyId)` — caminho do entity através de `IfcSite → IfcBuilding → IfcBuildingStorey`. Aplica-se a `Element`, `ElementGroup` e `Space`. Storey não tem (é ele mesmo). Renomeado de `BuildingElementContext` em P4.1.

### Para que servem Space e Storey

`Space` e `Storey` **não são inputs do algoritmo de detecção** — este consome apenas `Element[]` / `ElementGroup[]`. São usados por:

1. **`IfcInspector` (P4.2)** — triagem de modelos para os experimentos: volume de spaces como heurística de átrio, contagem de storeys, busca textual em `Space.LongName`. Output alimenta `00_Manuais_e_Referencias/datasets-ifc.md`.
2. **Geradores de LoD (P6)** — `Lod02StoreyFootprintsGenerator` (footprints por andar), `Lod12StoreyBlocksGenerator` (blocos extrudados por pavimento), `Lod22DetailedRoofWallsStoreysGenerator` (shells detalhadas com slabs por andar). Storey vira referência espacial; Space pode entrar em LoDs futuros que descrevam volumes habitáveis.

### Surface types

- `Envelope` — totalidade das faces exteriores (input do agrupamento)
- `Facade` — região de superfície por plano dominante (output do `IFacadeGrouper`); referencia `Envelope` parent + subconjunto de `Face[]`
- `Face` — superfície atômica exterior, unidade primária. **Não armazena `DMesh3`** — triângulos lidos via `Element.Mesh.GetTriangle(id)` para cada `id in TriangleIds`; `face.Element.GlobalId` dá o link ao IFC

### Acesso cru ao IIfcProduct (ADR-10)

`XbimIfcProductResolver` (em `src/Ifc/Resolver/`) expõe metadados IFC não-previstos em `Containment` (Pset_*, material, tags, `IfcRelConnectsPathElements`). Lifetime: `IfcStore` aberto; gerenciar via `using` ou escopo de DI.

### Interfaces de pipeline

`IDetectionStrategy`, `IFaceExtractor`, `DetectionResult`, `ElementClassification` em `src/Core/Pipeline/Detection/`. Interfaces com implementação única (`IModelLoader`, `IElementFilter`, `IIfcProductResolver`) e a `IFacadeGrouper` sem implementação foram removidas em 2026-04-23 — voltam naturalmente quando segunda implementação for necessária.

---

## Estrutura do Projeto

5 projetos `src/` + 1 testes. Pastas curtas (`src/Core`, `src/Engine`…); namespaces e DLLs mantêm prefixo `IfcEnvelopeMapper.*` via `RootNamespace` + `AssemblyName`.

| Projeto | Responsabilidade | Dependências externas |
|---|---|---|
| `Core` | Domínio + pipeline + math primitives. Zero infraestrutura. Domain (entidades), Pipeline (contratos por estágio), Extensions (math idiomático sobre g4). | `geometry4Sharp` apenas |
| `Engine` | Estratégias de detecção + visualização debug (`GeometryDebug`, `GltfSerializer`, `DebugSession`, ADR-17). Critério: tudo com dependência pesada. | `Core`, `SharpGLTF.Toolkit` |
| `Ifc` | Integração xBIM. `XbimModelLoader` produz `ModelLoadResult` sem expor xBIM aos consumidores — trocar de lib IFC toca só este projeto. | `Core`, `Xbim.Essentials`, `Xbim.Geometry`, `Xbim.ModelGeometry.Scene` |
| `DebugServer` | EXE standalone (não referência gerenciada). Roda viewer HTTP em processo OS separado para sobreviver ao freeze do debugger `.NET` em breakpoints com `Suspend: All` (ADR-17). | nenhuma |
| `Cli` | Entry point fino (~80 LOC): parse args → `EvaluationPipeline.EvaluateDetection()` → print. | `Core`, `Ifc`, `Engine`, `System.CommandLine`, `Microsoft.Extensions.Logging` |
| `tests/Tests` | xUnit + FluentAssertions. | `Core`, `Ifc`, `Cli`, `Engine` |

### Diagrama de dependências (sem ciclo)

```
       Core (leaf — domínio + pipeline + extensions)
       ↑       ↑
       |       |
     Ifc     Engine (estratégias + visualização)
       ↑       ↑
       └───┬───┘
           |
          Cli

DebugServer é spawned via Process.Start por Engine.Visualization.DebugSession.
Não é referência gerenciada — fica fora do grafo de deps.
```

`Tests` depende de `Core + Ifc + Cli + Engine`. Debug geométrico é acessado via `GeometryDebug.Mesh(...)` etc. — `[Conditional("DEBUG")]` em cada método público garante eliminação total das chamadas em Release (zero IL nos call sites).

`tools/debug-viewer/` (HTML + three.js local, ADR-16/17) e `data/{models,results,debug,ground-truth}/` ficam fora de `src/`.

> **Pendente neste projeto** (não implementado ainda): refactor de domínio para `IfcEntity` + interfaces de capacidade (Fase P4.1, abr/2026), `IfcInspector` + `XbimMetadataLoader` (Fase P4.2, mai/2026), `DbscanFacadeGrouper` (Stage 2 — Fase P4.3, jun–jul/2026), `HierarchicalVoxelFloodFillStrategy` (Fase P5, jul–set/2026, contribuição original), e os 10 geradores de LoD do framework Biljecki/van der Vaart (ADR-15, Fase P6, set–nov/2026). Quando criados, vivem em `Ifc/Inspection/`, `Engine/Strategies/`, `Engine/Grouping/`, `Engine/Lod/` (sem novo projeto).

---

## Pipeline de Detecção em Dois Estágios

```
IFC Model
    │
    ▼
[XbimModelLoader (sealed) — Load(path) → ModelLoadResult]
    │  IReadOnlyList<Element>
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
    │  Baseline de comparação: RayCastingStrategy (Ying 2022) — entregue P3
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

**Estado atual (P3 entregue):**

```csharp
// Program.cs — composition root
var model  = loader.Load(modelPath);          // XbimModelLoader → ModelLoadResult
var result = strategy.Detect(model.Elements); // IDetectionStrategy → DetectionResult
if (output is not null)
    writer.Write(report, output);             // .json → JsonReportWriter; .bcf → BcfWriter
```

**Estado alvo (após P4.3 + P6):**

```csharp
var model      = loader.Load(modelPath);                            // ModelLoadResult
var result     = strategy.Detect(model.Elements);                   // DetectionResult
var facades    = grouper.Group(result.Envelope);                    // Facade[] — após P4.3
var lodOutputs = options.Lods                                       // ILodGenerator[] — após P6
                    .Select(id => registry.Resolve(id).Generate(result, facades))
                    .ToList();
var report = ReportBuilder.Build(result, facades, lodOutputs, model.Groups, runMeta);
writer.WriteReports(report, outputPath);                            // 1 arquivo por LoD
```

**Instrumentação de debug (ADR-17).** Strategies e grouper chamam `GeometryDebug.Mesh(...)`, `GeometryDebug.Voxels(...)` etc. diretamente — sem configuração, sem interfaces. Em Release builds, `[Conditional("DEBUG")]` elimina as chamadas no call site. Em Debug builds, cada chamada serializa via atomic write para `C:\temp\ifc-debug-output.glb`; `DebugServer` (processo OS separado, ADR-17) serve o GLB ao browser em `:5173`. Developer inspeciona com breakpoints no IDE.

**Por que sem `FacadeDetector`?** A CLI é a composition root e orquestra diretamente os dois estágios. Isto permite:
- Trocar strategy e grouper de forma independente
- Adicionar observabilidade (logging, timing) entre estágios
- Evitar classe coordenadora que apenas delega

**Por que DBSCAN + QuikGraph?** DBSCAN agrupa por orientação de normal mas não distingue duas superfícies desconexas com mesma orientação (ex: fachada norte frontal e fachada norte do poço de luz). QuikGraph resolve isso: dentro de cada cluster DBSCAN, o grafo de adjacência espacial separa superfícies fisicamente desconexas. Cada componente conectado é uma Facade distinta.

### Inspetor de modelos (auxiliar — P4.2)

Fluxo paralelo, **fora do pipeline de detecção**. Suporta a fase de seleção de modelos para os experimentos: dado um diretório de IFCs candidatos, sumarizar contagens por tipo, schema, ferramenta de autoria, e flags de candidatos a casos adversariais (átrios, pátios abertos).

```
IFC file(s)
    │
    ▼
[XbimMetadataLoader] (rápido, sem Xbim3DModelContext)  ─┐
    │  ModelMetadata + element counts + storey count    │  Fase A
    │                                                   │
[XbimModelLoader.Load]  (full, com geometria)          ─┘──┬──┘
    │  ModelLoadResult (Elements, Groups, Spaces, Storeys, Metadata)
    ▼
[IfcInspector]
    │  Fase B: SpaceAnalyzer (top-N spaces por volume, candidatos a átrio)
    │  Fase D: RoofAnalyzer (count + materiais → cobertura vítrea)
    │  Fase E: FootprintAnalyzer (cavidades 2D → courtyards abertos)
    ▼
IfcInspection (record)
    │
    ├─ console (resumo legível)
    ├─ JSON por modelo (`inspect`)
    └─ CSV agregado (`inspect-all`, uma linha por arquivo)
```

O Inspector **não roda detecção**, **não produz `DetectionResult`**, **não compete com `IDetectionStrategy`**. Output alimenta a tabela "Modelos Selecionados" em `00_Manuais_e_Referencias/datasets-ifc.md`.

---

## Pseudocódigo Detalhado do Método

> Referências algorítmicas são indicadas onde técnicas publicadas fundamentam cada etapa.
> Para etapas sem referência direta — o clustering de normais sobre Gauss sphere para fachadas
> em IFC e a associação por participação muitos-para-muitos — estas constituem contribuição
> original deste trabalho.

### Estágio 0 — Carregamento e Triangulação

**Implementação:** `src/Ifc/Loading/XbimModelLoader.cs`

1. `IfcStore.Open(path)` → STEP parsing
2. `Xbim3DModelContext.CreateContext()` (`MaxThreads=1`, workaround OCCT)
3. Para cada `IIfcElement` filtrado por `DefaultElementFilter` (ADR-05):
   - **Standalone** (sem filhos): vira `Element` se `Mesh.TriangleCount > 0`
   - **Composite** (IfcCurtainWall/IfcRoof, ADR-09): vira `ElementGroup`; filhos viram `Element` apontando ao grupo via `GroupGlobalId`
4. Retorna `ModelLoadResult(Elements, Groups, Spaces, Storeys, Metadata)` (ADR-11)

**Exemplo concreto.** Uma cortina de vidro em canto de prédio com 4 painéis voltados para norte e 3 para leste produz 7 `Element`s (painéis) + N mullions com `GroupGlobalId = "curtainWall-1"`, e 1 `ElementGroup` IfcCurtainWall com mesh mesclado de own + filhos. O `DbscanFacadeGrouper` (P4.3) consome só `model.Elements` e classifica 4 painéis em Facade-Norte, 3 em Facade-Leste; um elemento de canto pode aparecer em 2+ fachadas (muitos-para-muitos).

### Estágio 1 — Detecção de Exterior (IDetectionStrategy)

O método implementa Voxel + Flood-Fill como estratégia primária (robustez em IFC real, referência canônica van der Vaart 2022) e Ray Casting como baseline de comparação (Ying 2022, caracteriza tradeoff precisão-vs-robustez no capítulo de Resultados). Normais foi descartada — ver ADR-14 que superseda ADR-12 parcialmente.

#### Estratégia 1A: Voxel + Flood-Fill (primária — ADR-14)

**Implementação:** `src/Engine/Strategies/VoxelFloodFillStrategy.cs`
**Referências canônicas:** van der Vaart (2022) — IFC_BuildingEnvExtractor; Liu et al. (2021) — ExteriorTag; Voxelization Toolkit (`fill_gaps.h`); Akenine-Möller (1997) — SAT triângulo-AABB

1. Bbox global expandida por `2 × voxelSize` + `VoxelGrid3D`
2. Rasterização: cascata 4-testes SAT triângulo-AABB (implementação própria — `g4.IntrTriangle3Box3` ausente). Cada voxel ocupado guarda lista de `GlobalId`s (provenance — ADR-04)
3. Flood-fill 3 fases: `GrowExterior` (semente em canto, conectividade 26) → `GrowInterior` (vazios adjacentes a ocupados) → `GrowVoid` (room labels)
4. `FillGaps` — fecha buracos de 1 voxel; robustez contra meshes com gaps/auto-interseções
5. Classificação: elemento exterior se ≥1 voxel ocupado por ele tem vizinho-26 marcado Exterior
6. Faces extraídas via `PcaFaceExtractor` (`g4.OrthogonalPlaneFit3` — ADR-13); cada `Face: {Element, TriangleIds, FittedPlane, Normal, Area, Centroid}`. `GeometryDebug.Triangles` instrumenta a saída (ADR-17)

#### Estratégia 1B: Ray Casting (baseline de comparação — ADR-14)

**Implementação:** `src/Engine/Strategies/RayCastingStrategy.cs`
**Referência canônica:** Ying et al. (2022) — two-stage recursive ray tracing

1. Mesh global mesclada + BVH (`g4.DMeshAABBTree3`)
2. Mapa triângulo→elemento (ownership) para distinguir auto-hits de hits externos
3. Por triângulo: `numRaios` raios partindo de `centroid + ε·normal` na direção da normal, com jitter de ±5°
4. Raio "escapa" se não intercepta ou hit pertence ao próprio elemento
5. Triângulo é exterior se `escapes / numRaios ≥ hitRatio`
6. Elemento é exterior se ≥1 triângulo exterior

### Estágio 2 — Agrupamento em Fachadas via DBSCAN (P4.3, não implementado)

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
    //   com contagem significativa. Avaliar em P4.3 se o ruído justificar.
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
            // NOTA: fachada.Elements retorna os Elements que
            // possuem ≥1 Face nesta região. Um elemento de canto aparecerá
            // em 2+ fachadas — comportamento correto (muitos-para-muitos).
            fachadas.Add(fachada)

    RETORNAR fachadas
```

### Estágio 3 — Relatório e Métricas

**Implementação:** `src/Core/Pipeline/Reporting/ReportBuilder.cs`, `JsonReportWriter.cs`; `src/Core/Pipeline/Bcf/BcfWriter.cs`
**Schema atual:** v1 (sem `facades`/`aggregates`); v2 alvo após P4.3, v3 alvo após P6.4 (LoD 3.2). Ver `## Schema JSON`.

1. Classificação por elemento: `globalId`, `ifcType`, `isExterior`
2. Determinismo: `OrderBy(c => c.Element.GlobalId, StringComparer.Ordinal)` antes de serializar (saída byte-estável)
3. Quando `--ground-truth` fornecido (P9): TP/FP/FN/TN + Precision/Recall por contagem (ADR-12: F1/Kappa intencionalmente fora — não aparecem nas referências canônicas: van der Vaart 2022 usa contagens manuais, Ying 2022 usa apenas Precision/Recall)
4. BCF 2.1: um tópico por elemento exterior, viewpoint apontando para o centroide via `Components/Selection/Component@IfcGuid`

---

## Tabela Comparativa das Estratégias de Detecção

> Escopo desta tabela: **Voxelização Uniforme (ablation baseline)** × ***Ray Casting* (baseline externo)**. Cobre as Fases 2–4 (período em que estas duas estratégias coexistem sem HVFF). A terceira estratégia — *Hierarchical Voxel Flood-Fill* (contribuição original) — é introduzida na Fase 5; a comparação final com 3 estratégias aparece no capítulo de Resultados da dissertação e é resumida em §Comportamento em casos geométricos chave.

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

**Nota sobre a decisão (ADR-14, revisada 2026-04-24).** Esta tabela cobre apenas as Fases 3–4. A escolha de *voxel flood-fill* uniforme como baseline de produção se apoia na robustez em IFC real — modelos com *gaps*, auto-interseções e topologia imperfeita são a norma, não a exceção (documentado em `Ferramentas/BuildingEnvExtractor/IFC_BuildingEnvExtractor_Evaluation.md` §5). *Ray Casting* entra como baseline de comparação, caracterizando o *tradeoff* precisão × robustez. A `NormalsStrategy` (presente em ADR-12) permanece descartada: baseline trivial não contribui comparação científica relevante. Na Fase 5 (HVFF), a voxelização uniforme passa a ser comparada com a variante hierárquica como contribuição original do trabalho.

---

## Comportamento em casos geométricos chave

Esta seção documenta os padrões geométricos em que as estratégias divergem. É a base do argumento de defesa: *por que flood-fill volumétrico é preferível ao ray casting em IFC real*, e *por que uma variante hierárquica é defensável como contribuição*.

### Caso 1 — Poço de luz (*air well*) central

Cavidade vertical aberta no topo, cercada por paredes em todos os lados horizontais. Frequente em edifícios residenciais e comerciais antigos.

```
        ceu (exterior)
            │
            ▼
    ┌───────────────────────┐       ── telhado ──
    │         │     │       │
    │  sala   │ poço│ sala  │       ↓ flood-fill desce
    │         │ luz │       │         pelo topo aberto
    │         │     │       │         (exterior)
    │─────────┤░░░░░├───────│
    │         │░░░░░│       │       ── piso ──
    │  sala   │░░░░░│ sala  │         raio horizontal
    │         │░░░░░│       │         de parede interna
    │─────────┤░░░░░├───────│         bate na parede
    │         │     │       │         oposta → classifica
    │ garagem │ poço│ depos.│         como EXTERIOR (falso
    │         │     │       │         positivo)
    └───────────────────────┘
```

**Voxel *flood-fill***: o *flood-fill* parte do canto (0,0,0) do *grid* expandido, atinge o céu acima do telhado e desce pelo poço. Paredes que dão para o poço são corretamente marcadas como exterior.

**Ray casting**: um raio lançado a partir da normal de uma parede interna do poço, na horizontal, intercepta a parede oposta do mesmo poço. Classifica como **interior** — falso negativo.

**Hierarchical Voxel Flood-Fill**: mesmo resultado correto que o *flood-fill* uniforme, porém com gasto proporcional ao volume do poço (célula grande) em vez do volume da casa inteira em resolução fina.

### Caso 2 — Átrio coberto com *skylight*

Cavidade vertical fechada no topo por vidro (exterior declarado, mas topologicamente selada).

```
    ─── vidro do skylight ───       ↓ flood-fill NÃO desce
     ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓         (skylight = casca sólida
    ┌───────────────────────┐         após rasterização)
    │         │     │       │
    │ quarto  │ \a/ │ quarto│       paredes do átrio: topo-
    │         │ |   │       │       logicamente isoladas do
    │─────────┤ |   ├───────│       exterior → classificadas
    │         │ |   │       │       INTERIOR por flood-fill
    │ sala    │ |   │ cozinh│
    │         │     │       │       ray casting: raio hori-
    └───────────────────────┘       zontal bate na parede
                                    oposta → INTERIOR (ok)
```

**Voxel *flood-fill***: correto — *skylight* vedado impede a descida do *flood* exterior, paredes do átrio ficam interior. Concorda com o julgamento AEC de *"fachada = separa interior climatizado do exterior"*.

**Ray casting**: coincidentemente também correto neste caso (raio horizontal bate em parede oposta).

**Diferença**: o caso 1 (poço aberto) é onde as duas estratégias **divergem**; o caso 2 mostra que o *flood-fill* não confunde átrio coberto com poço aberto — ele respeita a topologia real.

### Caso 3 — Eixo estreito (*shaft*) de instalações

Duto vertical de <500mm para instalações hidráulicas ou elétricas, passando por todos os pavimentos, aberto ao telhado.

```
    ┌──────────────────┐
    │        │█│       │    █ = shaft (largura < voxel-size
    │  sala  │█│ sala  │        0.5m ⇒ NÃO é rasterizado como
    │        │█│       │        casca sólida; vira "túnel" no
    │────────┤█├───────│        grid uniforme)
    │        │█│       │
    │  sala  │█│ sala  │    voxel uniforme (0.5m): paredes do
    │        │█│       │    shaft recebem EXTERIOR (flood-fill
    │────────┤█├───────│    desce pelo shaft) — correto por
    │ garag. │█│ depos.│    acidente, mas ray casting também
    └──────────────────┘    falha aqui pela mesma razão que no
                            caso 1
```

**Voxel uniforme (0.5m)**: paredes internas do *shaft* são erroneamente classificadas como exterior porque o *flood-fill* atravessa o duto. Pode ser mitigado reduzindo `voxel-size`, mas isso multiplica o custo global por 8×.

**Hierarchical Voxel Flood-Fill**: refina adaptativamente apenas na vizinhança da casca do *shaft*. A resolução fina onde a geometria exige não paga o custo global — **motivação direta para a contribuição da Fase 5**.

**Ray casting**: falha análoga ao caso 1 (raio horizontal intercepta a parede oposta do *shaft*).

### Caso 4 — `FillGaps` e malhas imperfeitas

Modelos IFC reais frequentemente têm malhas com *gaps* de 1 voxel na casca (ver §5 de `IFC_BuildingEnvExtractor_Evaluation.md`): triângulos não se encontram perfeitamente nas arestas, erros de *tessellation* do OCCT, etc.

```
    após rasterização                  após FillGaps()
    ┌──────────────────┐              ┌──────────────────┐
    │░░░░░░░░░░░░░░░░░░│              │░░░░░░░░░░░░░░░░░░│
    │░██████ ██████████│  gap de      │░████████████████░│
    │░█             ·█░│  1 voxel     │░█   · · · · · █░│
    │░█   · · · · · █░│  na casca ──▶│░█   · · · · · █░│  casca
    │░█             ·█░│  (vazamento  │░█             ·█░│  selada
    │░███████████████░│  do exterior)│░███████████████░│
    │░░░░░░░░░░░░░░░░░░│              │░░░░░░░░░░░░░░░░░░│
    └──────────────────┘              └──────────────────┘

    ░ = Exterior   █ = Occupied   · = Interior
```

**Voxel *flood-fill* com `FillGaps`**: voxels `Unknown` cercados por 6 vizinhos face-adjacentes `Exterior` são promovidos a `Exterior` iterativamente. Fecha *gaps* de até 1 voxel sem mudar a topologia real.

**Ray casting**: sem mecanismo análogo — um *gap* na malha gera falsos positivos diretos (raio escapa por uma fresta que não existe no modelo físico).

### Por que isso sustenta a escolha da contribuição

- Os casos 1 e 3 isolam exatamente onde *ray casting* falha: **topologia aberta em escalas pequenas**. *Ray casting* não vê o caminho de fuga; *flood-fill* volumétrico vê.
- O caso 3 isola onde voxel **uniforme** é financeiramente proibitivo para resolver corretamente — o custo cresce cúbica com o refinamento. A contribuição original da Fase 5 ataca esse *tradeoff* preservando a robustez do *flood-fill*.
- Todos os quatro casos geram rastreabilidade por `GlobalId` (o voxel mantém a lista de ocupantes durante a rasterização), alinhados com o critério da pergunta de pesquisa.

---

## Otimizações Futuras

Backlog de melhorias que **não** estão no escopo da defesa de abr/2027, registradas aqui para documentação e para eventual continuidade pós-TCC.

> *Hierarchical Voxel Flood-Fill* **não está neste backlog** — está no escopo do trabalho como Fase 5 (contribuição original).

- **Paralelismo na rasterização.** Cada triângulo marca voxels independentes; um `Parallel.For` sobre a lista de triângulos (com sincronização na escrita do `HashSet<string>` de ocupantes) dá *speedup* quase linear em modelos grandes. Cuidado com determinismo — exige ordenação final estável (ver §Determinismo do Método).
- **SIMD no teste SAT triângulo-caixa.** A cascata de 13 eixos do Akenine-Möller (1997) é vetorizável via `System.Numerics.Vector<T>`: os três produtos escalares por eixo viram uma única instrução SIMD. Benefício esperado: ~3× no *hot loop* da rasterização.
- **Ordenação Morton dos voxels.** Indexar voxels por curva de Morton (Z-order) em vez de `[x,y,z]` linear melhora a localidade de cache durante o *flood-fill* e a contagem de vizinhos. Útil apenas se *profiling* apontar cache miss dominante.
- **Voxelização em GPU.** Shader de rasterização que escreve diretamente no *grid* de voxels 3D (técnica do `Voxelization Toolkit` em OpenCL). Descarta compatibilidade CPU-only — aceito só se modelos maiores (>10⁵ elementos) forem priorizados.
- **Terminação antecipada do *flood-fill*.** Parar `GrowExterior` quando todos os voxels `Occupied` adjacentes a exterior já foram tocados. Exige manter um contador de voxels de casca por elemento; complexidade adicional pode não valer o ganho (o *flood-fill* em si já é O(V) com constante pequena).
- **Cache persistente de `ShapeGeometry`.** Re-executar o pipeline no mesmo IFC hoje re-triangula tudo via xBIM. Um cache em disco por `(file-hash, EntityLabel)` cortaria tempo de *iteration loop* em testes de regressão.

Cada item entra como *issue* do repositório apenas se o *profiling* pós-P3 (com modelos selecionados em P4.2 e/ou comparação 3-vias da P5) apontar o gargalo real.

---

## IsExternal e LoD — Decisões de Design

### IsExternal não pertence ao Element

A propriedade `IsExternal` do IFC (`Pset_WallCommon`, etc.) é **não confiável** em modelos reais — o IFC_BuildingEnvExtractor da TU Delft ignora-a por padrão (`ignoreIsExternal_ = true`). O algoritmo *computa* exterioridade; incluir `IsExternal` no modelo de domínio criaria dualidade confusa.

**Schema atual (v1).** Não expõe `IsExternal` declarada — só `isExterior` computado por elemento.

**Schema alvo (v2, após P4.3).** Expõe comparação `computed` × `declared` por elemento, permitindo a métrica de validação *"em N% dos casos a classificação geométrica concordou com a propriedade declarada"*:

```json
{
  "globalId": "2O2Fr$t4X7Zf8NOew3FL9r",
  "computed": { "isExterior": true },
  "declared": { "isExternal": true },
  "agreement": true
}
```

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
| Voxel (5.0) | 5.0 (v.0) | **Não é LoD separado** — é saída do sistema de debug (ADR-17) via `GeometryDebug.Voxels()` |

**Rastreabilidade preservada em todos os LoDs.** Cada `LodOutput` carrega `ElementProvenance: IReadOnlyCollection<string>` (GlobalIds dos elementos que contribuíram), satisfazendo a exigência da questão de pesquisa *"preservando rastreabilidade semântica"*.

**CLI:** `--lod 0.0,1.0,2.2,3.2,4.1` seleciona quais gerar. Default: `3.2` (core). Saídas: arquivos separados por LoD (`report_lod32.json`, `footprint_lod00.geojson`, `shell_lod22.gltf`, …) — formato natural de cada nível; não forçamos schema unificado.

---

## Decisões Arquiteturais (ADRs)

Formato curto: decisão, motivo, consequência. Decisões históricas revogadas ficam registradas para rastreabilidade na dissertação.

### ADR-01 — [REVOGADA por ADR-09]

Previa `LeavesDeep()` recursivo em `Element` para navegar árvore profunda arbitrária. Análise pós-decisão mostrou que IFC real mantém agregações em 2 níveis — recursão é *overengineering*. Substituída por ADR-09 + ADR-11.

### ADR-02 — `IfcRelFillsElement` é ignorado no loader

**Decisão.** Janela, porta e parede são carregadas como `Element`s independentes. A relação "janela preenche void na parede" não é preservada via metadado IFC; é descoberta pelos algoritmos via geometria (bounding-box overlap, proximidade).

**Motivo.** Fiel ao princípio "geometria primeiro, IFC properties são hints". Mantém loader simples; não cria dependência em metadado que pode faltar em modelos de baixa qualidade.

**Consequência.** Algoritmos de classificação não recebem dica de "esta janela está em parede externa" — precisam inferir. Aceitável: é justamente o que o TCC se propõe a demonstrar.

### ADR-03 — Semântica de agregadores é fixa, sem flag CLI

**Decisão.** Uma só semântica de tratamento de agregadores (ADR-11) para todo o projeto. Não existe `--aggregate-mode flatten|tree|hybrid`.

**Motivo.** Menos superfície de bugs; testes mais previsíveis; documentação da dissertação mais simples; usuário final da ferramenta não precisa conhecer este detalhe interno.

**Consequência.** Se surgir um caso de modelo real que exige outro tratamento, a decisão precisa voltar ao plano antes de virar código.

### ADR-04 — `Face` = `Element` + `TriangleIds` + `Plane3d`

**Decisão.** `Face` referencia `Element` diretamente, carrega índices de triângulos no mesh do elemento (não duplica geometria) e um `Plane3d` ajustado por PCA (substitui `Normal + PointOnPlane` separados).

**Motivo.** Rastreabilidade forte (`face.Element.GlobalId` funciona direto) sem lookup externo; sem duplicação de geometria; `Plane3d` centraliza `Normal`, `PointOnPlane`, `Distance(p)`, `Project(p)`.

**Consequência.** Acoplamento `Face → Element` é aceitável — unidirecional, ambos em Core. Em serialização JSON, usar `[JsonIgnore]` em `Face.Element` e expor só `Element.GlobalId` evita ciclos.

### ADR-05 — `IElementFilter` em Core + default inclusivo + override CLI

**Decisão.** Filtro de tipos IFC é interface em Core. `DefaultElementFilter` traz uma lista hardcoded razoável. `XbimModelLoader` recebe `IElementFilter` por construtor. CLI aceita `--include-types X,Y,Z` e `--exclude-types A,B` para montar filtro programaticamente. Config opcional em `data/elementFilter.json` para persistência por modelo.

**Motivo.** Feedback explícito: *"o filtro deve ser facilmente alterado no futuro, até pelo usuário se necessário"*. Interface permite DI em testes, CLI permite override sem recompilar.

**Consequência.** `DefaultElementFilter` fica *opinativo* — inclui `IfcRailing`, exclui `IfcFooting`, etc. Decisões do default são documentadas e questionáveis em PR.

### ADR-06 — `BcfWriter` + Viewer em paralelo

**Decisão.** `BcfWriter` continua em `Cli/Output/` produzindo BCF a partir do JSON. O Viewer também produz BCF (após edição manual de rotulação). Ambos consomem o mesmo JSON.

**Motivo.** Pipeline + JSON é o caminho automatizado (reproduzível em CI). Viewer é o caminho assistido (curadoria humana). São usos distintos; um não substitui o outro.

**Consequência.** Há duas implementações de BCF no projeto. A do Viewer pode divergir (anotações manuais, viewpoints editados) da do CLI (viewpoints gerados). Compartilhar código via biblioteca BCF comum (`iabi.BCF` ou equivalente) quando possível.

### ADR-07 — Viewer MVP default; Completo como stretch goal (revisado por ADR-12; possível absorção por ADR-17)

**Decisão.** O entregável obrigatório do Viewer é o **MVP**: render 3D dos meshes coloridos por fachada, inspeção por elemento e filtro exterior/interior. **Edição manual de rotulação** e **export BCF** são *stretch goals* condicionais a stage gates (Precision/Recall do Stage 1 aceitáveis + tempo de cronograma). A versão anterior desta ADR tratava o Viewer Completo como obrigatório; ADR-12 reclassificou.

**Motivo.** Viewer Completo é o item de maior risco de cronograma e não é a questão de pesquisa. O MVP já satisfaz o critério #4 do TCC (≥4 ferramentas BIM) quando somado a Revit/ArchiCAD/FME/Solibri na validação. Edição + BCF entram apenas se houver folga após P1–P5.

**Consequência.** Contingência documentada: se Precision/Recall do Stage 1 forem insuficientes ou se cronograma estiver apertado, o Viewer permanece em escopo MVP e BCF é gerado pela CLI (ADR-06). Stage gates detalhados continuam na seção Viewer (§ Viewer).

> **Possível absorção (decisão em Fase 7, ver ADR-17).** O sistema de debug adotado (ADR-17) produz um viewer HTML local em `tools/debug-viewer/` a partir da Fase 3. Se esse viewer evoluir para UX amigável a especialistas AEC, o Viewer Blazor MVP pode ser absorvido — elimina-se o Viewer como projeto separado, energia concentra no debug-viewer que serve duplo propósito (dev + end-user). A decisão é adiada para Fase 7; até lá, Viewer segue como stretch goal de ADR-07 revisado.

### ADR-08 — Hierarquia `IfcEntity` + capability interfaces; identidade por GlobalId

**Decisão.** Domínio organizado como classe abstrata `IfcEntity` (carrega só `GlobalId`, `IfcType`, `Name` + `IEquatable` por `GlobalId`) com concretes `Element`, `ElementGroup`, `Space`, `Storey`. Capacidades geométricas vivem em interfaces ortogonais — `IIfcEntity` (root), `IBoxEntity` (`AxisAlignedBox3d BoundingBox`), `IMeshEntity` (`DMesh3 Mesh`) — implementadas conforme cada concrete oferece. `BoundingBox` é campo armazenado (não derivado de `Mesh.GetBounds()`) populado a partir de `XbimShapeInstance.BoundingBox` no loader.

**Motivo.** Core desacoplado de xBIM. Identidade por `GlobalId` herdada uma vez na base elimina duplicação de equality boilerplate em cada concrete. Interfaces ortogonais permitem dispatch polimórfico sem forçar todo entity a ter mesh — `Storey` é puro `IIfcEntity` (sem geometria); `Element`/`ElementGroup`/`Space` são `IMeshEntity + IBoxEntity`. Bbox armazenado desacopla o contrato de bbox do contrato de mesh, future-proofing lazy mesh sem quebrar consumidores.

**Consequência.** Sub-tipos podem implementar quaisquer capacidades aplicáveis sem afetar a base; clientes que querem só bbox usam `IBoxEntity` e ganham um conjunto polimórfico de Element + ElementGroup + Space. Metadado IFC além dos campos do domínio (Pset_*, material, tags) é buscado via `IIfcProductResolver` (ADR-10).

### ADR-09 — Agregação IFC de building elements tem 2 níveis fixos

**Decisão.** IFC real mantém `IfcRelAggregates` para building elements em exatamente 2 níveis (agregador → átomos). `Debug.Assert` no loader captura violação (child com `IsDecomposedBy` não-vazio); log warning em Release.

**Motivo.** Agregadores comuns (`IfcCurtainWall`, `IfcStair`, `IfcRamp`, `IfcRoof`) têm filhos construtivos diretos; ninguém aninha `IfcStair` dentro de `IfcStair`. Premissa informa o split do ADR-11 e evita recursão desnecessária.

**Consequência.** Loader simples, sem `LeavesDeep`. Se um modelo real violar a premissa, o assert falha em Debug e produz log em Release — trata-se excepcionalmente caso aconteça.

### ADR-10 — `IIfcProductResolver` na camada Ifc

**Decisão.** Interface em `IfcEnvelopeMapper.Ifc` (não em Core). `XbimIfcProductResolver` indexa `IfcStore.Instances.OfType<IIfcProduct>()` por `GlobalId` em `Dictionary`. Viewer, Cli, testes importam quando precisam de metadados IFC não previstos em `Containment`.

**Motivo.** Core permanece sem referência a xBIM. Resolver explicita que o consumidor está acoplando ao schema IFC. Index evita O(n) por lookup.

**Consequência.** Propriedades IFC são *hints* — algoritmos Core não dependem do resolver. Uso típico: Viewer mostra `Pset_WallCommon` ao clicar em elemento; BCF export lê material/tag; testes de integração acessam metadados específicos.

### ADR-11 — Split do modelo: `Element` (átomo) + `ElementGroup` (agregador composite com mesh mesclado)

**Decisão.** Loader retorna `ModelLoadResult(Elements, Groups, Spaces, Storeys, Metadata)`. `Element` sempre tem `Mesh` (descarta o que vem do IFC sem geometria). `ElementGroup` agrupa Elements de um composite IFC (`IfcCurtainWall`, `IfcRoof`, etc.) e expõe um único `Mesh` mesclado (geometria própria do composite + meshes dos filhos), construído eagerly no load via `DMesh3Extensions.Merge`.

**Motivo.** Modelo único com `Mesh` opcional e `Children` opcional criava estados inválidos (átomo com children, agregador sem children). O split elimina isso por construção. Algoritmos de detecção consomem só `model.Elements` — comportamento trivial, sem `LeavesDeep`. `Groups` servem à rastreabilidade (canto de cortina aparece em 2 fachadas, mas o `IfcCurtainWall` único é reportado pelo seu `GlobalId`). Mesh mesclado no Group elimina a distinção `OwnMesh?` + `Elements[].Mesh` que precedia a refactor — um único contrato `IMeshEntity.Mesh` para qualquer entity geométrica.

**Consequência.** `Element.GroupGlobalId` é back-ref opcional por `string` (evita ciclos em serialização). Filho sem geometria (ex: `IfcCurtainWallPanel` vazio) é descartado pelo loader — não vira `Element`, não entra em `Group.Elements`. Custo do merge: cada Group carrega triângulos duplicados (também presentes nos seus filhos); aceitável para 10–100 groups por modelo.

### ADR-12 — [REVOGADA por ADR-14 (estratégias) e ADR-17 (Viewer)]

Previa RayCasting como primária e Voxel como fallback, com `NormalsStrategy` como baseline trivial; ordem de fases P1 → P2 (RayCasting) → P3 → P4 (Voxel) → P5 (DBSCAN). ADR-14 inverteu (Voxel primária por robustez, RayCasting baseline externo) e descartou `NormalsStrategy`. ADR-17 substituiu o caminho de Viewer Blazor por debug-viewer + `[Conditional("DEBUG")]`.

**Permanecem válidos** (não-revogados): Stage 1 antes de Stage 2 (gate baseado em Precision/Recall aceitáveis, threshold calibrado em P2). F1 e Kappa removidos do plano de avaliação após leitura das referências canônicas — van der Vaart 2022 usa contagens manuais, Ying 2022 usa apenas Precision/Recall.

### ADR-13 — Aproveitamento máximo da stack para matemática e indexação espacial

**Decisão.** Matemática de detecção e agrupamento (plane-fit PCA, eigen solver, interseção triângulo-AABB, histograma de normais na esfera de Gauss) usa classes já presentes em `geometry4Sharp`. **`NetTopologySuite.STRtree` é 2D apenas** — usado exclusivamente no LoD 0 (projeção XY, ADR-15). Para queries 3D sobre `Element` o plano é: linear scan com AABB test (n típico ≤ 10⁴ — O(n) é aceitável); para queries triangulo-a-triangulo, `g4.DMeshAABBTree3` (BVH 3D nativo do geometry4Sharp). Nenhum `MathNet.Numerics` é adicionado; nenhum algoritmo clássico (Akenine-Möller tri-AABB) é re-implementado localmente.

**Motivo.** Investigação das ferramentas de referência (Voxelization Toolkit, IFC_BuildingEnvExtractor) mostrou que ambas escreveram voxel storage e flood-fill do zero, mas delegaram math fundamental a Eigen/OCCT/Boost. A stack .NET **não tem equivalente direto ao `Boost.Geometry rstar<Point3D>`** — tentar usar `STRtree` em 3D foi um erro da versão anterior desta ADR. Análise do hot path do algoritmo mostra que: (i) voxelização itera `elemento → triângulos → voxels` (não precisa indexar elementos); (ii) provenance é guardada em `grid[v].Elements` (não precisa query reversa indexada); (iii) DBSCAN opera em R³ unitário (Gauss sphere, não espaço físico); (iv) adjacência de faces é O(f²) com f pequeno. Linear scan basta. Se profiling futuro apontar gargalo, um octree custom (~150 linhas) resolve sem depender de lib.

**Consequência.** Mapeamento direto de decisões algorítmicas a classes .NET:

| Componente do plano | Classe / lib |
|---|---|
| `Face.FittedPlane` via PCA (ADR-04) | `g4.OrthogonalPlaneFit3` |
| Normais de mesh (ponderadas por área) | `g4.MeshNormals` |
| Eigen genérico (se portar `dimensionality_estimate`) | `g4.SymmetricEigenSolver` |
| Voxelização — interseção triângulo-AABB (P2 ✅) | SAT próprio (Akenine-Möller 1997) — `g4.IntrTriangle3Box3` ausente |
| Esfera de Gauss pré-discretizada (P4.3, opcional) | `g4.NormalHistogram` |
| BVH 3D de triângulos por mesh (ray casting P3 ✅) | `g4.DMeshAABBTree3` |
| Queries AABB 3D sobre `Element` | Linear scan com AABB pre-filter |
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
- Ordem das Fases: P1 (infra) → P2 (Voxel ponta-a-ponta + debug visual) → P3 (RayCasting baseline + JSON/BCF) → P4 (Domain refactor + IfcInspector + DbscanFacadeGrouper) → **P5 (Hierarchical Voxel — contribuição original)** → P6 (LoDs 0.x → 4.x) → P7 (Viewer MVP).
- Pseudocódigo 1A (Normais) removido do plano; 1B (RayCasting) reclassificado como baseline; 1C (Voxel) renomeado para 1A e expandido como primária com cascata 4-testes + 3 fases + `FillGaps`.
- Provenance em Voxel: cada voxel mantém `Elementos` (set de `GlobalId`) ao ser marcado ocupado; classificação final lê essa lista. Padrão replicado do `internalProducts_` do EnvExtractor.
- Contingência: se voxel em P2 falhar em fixtures com detalhes finos (ex: janelas <300mm) e não houver calibração satisfatória via `voxel-size`, reconsiderar voxel adaptativo ou (última opção) RayCasting como primária. Decisão documentada em novo ADR caso necessário.

**Ameaças à validade (registrar na dissertação).** Dropar Normais significa perder o baseline "trivial" clássico. Mitigação narrativa: RayCasting é baseline mais forte — argumento na banca será *"comparamos com método state-of-the-art validado, não com heurística ingênua"*. Perda da análise "voxel como fallback": reformulada como *"voxel como primária por robustez, raycast como comparação de precisão"* — narrativa mais clara.

**Atualização (2026-04-24) — revisão de método: 3-way comparison com contribuição original.**

- A **Fase 5 — Hierarchical Voxel Flood-Fill** (20/jul → 13/set/2026; 8 semanas) introduz uma terceira estratégia, implementada como **contribuição original do TCC**. Detalhamento no cronograma (§Fases) e em §Comportamento em casos geométricos chave.
- `VoxelFloodFillStrategy` (uniforme) é **reenquadrada como ablation baseline**: mesma família algorítmica que a contribuição (flood-fill 3-fases de van der Vaart 2022), difere apenas na discretização espacial. Sustenta o argumento *"o ganho vem da hierarquia, não de truques ortogonais"*.
- `RayCastingStrategy` (Ying 2022) **permanece como baseline externo** — caracteriza o tradeoff precisão-vs-robustez contra um método state-of-the-art de família algorítmica distinta.
- A comparação no capítulo de Resultados passa a ser **3-way** (voxel uniforme vs. Hierarchical Voxel Flood-Fill vs. ray casting), ancorada na bateria de casos geométricos chave (poço de luz, átrio coberto, eixo estreito, `FillGaps`).
- Esta atualização **não invalida** as decisões originais de ADR-14: mantém Voxel como família primária, mantém RayCasting apenas como baseline de comparação, mantém Normais descartada.

### ADR-15 — Adoção do framework LoD (Biljecki/van der Vaart)

**Decisão.** Adotar o sistema LoD de Biljecki et al. (2016), refinado por van der Vaart (2022) no IFC_BuildingEnvExtractor, como **sistema de saídas** do IfcEnvelopeMapper. 10 LoDs standard implementados via `ILodGenerator`; interfaces e DTOs em `Core/Pipeline/Lod/`, geradores concretos em `Engine/Lod/` (mesmo padrão de `Core/Pipeline/Reporting/` + `Core/Pipeline/Bcf/` entregues em PR #16/#19). Experimentais (b.0, c.1, c.2, d.1, d.2, e.1) descartados. LoD 0 via **projeção XY** (não convex hull — preserva formas L/U). LoD 5.0 (voxel) **subsumido pelo sistema de debug** (ADR-16), não é LoD separado.

**LoDs adotados:** `0.0, 0.2, 1.0, 1.2, 2.2, 3.2, 4.0, 4.1, 4.2`. A contribuição original do TCC (facade como agregado composto com provenance IFC) vive no **LoD 3.2**. LoDs 0.3/0.4/1.3/2.2-roof-inclinado e variantes experimentais descartados para conter escopo — detecção de superfícies inclinadas de telhado em níveis de footprint/block é overkill; em 3.2 já há semantic face classification que cobre o caso.

**Motivo.** (a) Posicionamento acadêmico forte: *"este trabalho estende o LoD 3.2 do framework Biljecki/van der Vaart introduzindo facade como entidade composta com provenance IFC"* é narrativa sólida para a banca. (b) Stage 1 + Stage 2 produzem o mesmo `DetectionResult + Facade[]` independente de LoD — os geradores são transformações de saída, não alteram o algoritmo core. (c) Múltiplos LoDs atendem múltiplos casos de uso (GIS LoD 0-1, modelagem urbana LoD 2, BIM LoD 3-4) — reforça o critério #4 do TCC (≥4 ferramentas BIM). (d) LoD 0 com projeção XY (em vez de convex hull) preserva forma exata; convex hull perderia informação em edifícios em L ou com poço de luz.

**Consequência.**
- 10 `ILodGenerator` implementations + `LodRegistry` em `Core/Pipeline/Lod/` (interfaces/DTOs) + `Engine/Lod/` (concretes), seguindo o padrão de `Core/Pipeline/Reporting/` e `Core/Pipeline/Bcf/`. **Sem novo projeto.**
- Remoção da seção "Sem sistema de LoD" (substituída por "Sistema de LoD adotado").
- CLI ganha flag `--lod <lista>` (default: `3.2`). Saídas em arquivos separados por LoD.
- Schema JSON v3 substitui v2 para o LoD 3.2; outros LoDs usam formatos naturais (GeoJSON para 0.x, glTF/OBJ para 2.x+, etc.).
- Rastreabilidade (`ElementProvenance: IReadOnlyCollection<string>` com `GlobalId`s) preservada em todos os LoDs — satisfaz a questão de pesquisa em qualquer nível de saída.

### ADR-16 — [REVOGADA por ADR-17]

Previa runtime `IDebugSink`/`NullDebugSink`/`GltfDebugSink` em projeto separado `IfcEnvelopeMapper.Debug/`. ADR-17 substituiu por classe estática `GeometryDebug` com `[Conditional("DEBUG")]`.

---

### ADR-17 — Debug geométrico via `[Conditional("DEBUG")]` + viewer HTTP em processo separado

**Decisão.** Classe estática `GeometryDebug` em `src/Engine/Visualization/` com cada método público marcado `[Conditional("DEBUG")]` — em builds Release, todas as chamadas são eliminadas pelo compilador no call site (zero IL, zero overhead, sem null-object pattern). Em builds Debug, cada método acumula shapes via `DebugSession` e serializa para `C:\temp\ifc-debug-output.glb` via atomic write (`.tmp` + `File.Move`) a cada chamada — o GLB está pronto para inspeção a qualquer breakpoint.

Arquitetura em duas camadas:
- **Camada A — `GeometryDebug` + `GltfSerializer` (obrigatória).** API de instrumentação chamada direto pelo algoritmo. SharpGLTF.Toolkit é a dependência.
- **Camada B — `DebugServer` em processo OS separado (debug only).** Projeto EXE standalone (`src/DebugServer/`) spawned via `Process.Start` por `DebugSession`. `HttpListener` loopback-only em `:5173` serve o HTML de `tools/debug-viewer/` (three.js modular) + o GLB corrente; browser faz polling. Processo separado contorna o freeze do debugger .NET com política `Suspend: All` que congelaria um servidor in-process.

**Localização: `src/Engine/Visualization/`** (não `Core`). `Voxels()` depende de `VoxelGrid3D` (Core), mas `GltfSerializer` traz `SharpGLTF.Toolkit` — dependência pesada que pertence a Engine pelo critério "se tem dep pesada, fica fora do Core".

**Motivo.** `IDebugSink` (ADR-16 revogada) adicionava DI em construtores, null-sink em produção, fan-out — complexidade desnecessária. `[Conditional("DEBUG")]` é o padrão idiomático do C# para instrumentação de desenvolvimento. GLB (binário auto-contido) em vez de glTF (JSON + .bin) porque o viewer carrega num único fetch. `C:\temp\` em vez de `%TEMP%` porque Chromium bloqueia `AppData\Local\Temp` para File System Access API.

**Consequência.**
- Strategies e grouper chamam `GeometryDebug.Mesh(...)`, `GeometryDebug.Voxels(...)` etc. diretamente. Zero `#if DEBUG` no código do algoritmo.
- `IfcEnvelopeMapper.Debug/` (placeholder original) descartado; código vive em `Engine/Visualization/` + projeto EXE `DebugServer`.
- **ADR-07 pode ser absorvida (Fase 7).** Se o debug-viewer evoluir para UX amigável a end-user, Viewer MVP Blazor é descartado e o debug-viewer assume duplo papel (dev + end-user).

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

## Schema JSON

Três versões, evoluindo conforme as fases entregam novas saídas:

- **v1 — atual** (PR #16, Fase 3): payload mínimo de detecção (Stage 1 + metadata).
- **v2 — alvo** (após Fase 4.3 / DBSCAN): adiciona blocos `facades`, `aggregates`, `diagnostics`.
- **v3 — alvo** (após Fase 6.4 / LoD 3.2; ver ADR-15): substitui v2 para o LoD 3.2 com `ElementProvenance` por LoD.

### Schema JSON v1 — atual

```json
{
  "schemaVersion": "1",
  "input": "C:\\…\\duplex.ifc",
  "strategy": "voxel",
  "config": {
    "voxelSize": 0.25,
    "numRays": null,
    "jitterDeg": null,
    "hitRatio": null
  },
  "exteriorCount": 54,
  "interiorCount": 93,
  "elements": [
    {
      "globalId": "01KzA4SPn5IOODwLEb5RNY",
      "ifcType": "IfcMember",
      "isExterior": true
    }
  ],
  "generatedAt": "2026-04-26T05:30:00.000+00:00",
  "durationSeconds": 0.46
}
```

Schema v1 cobre exatamente o que P3 entrega: detecção (Stage 1) + parâmetros + tempo. Não há `facades` (sem DBSCAN ainda), nem `aggregates`/`diagnostics`. O campo `config` carrega tunings nullable por estratégia: voxel preenche `voxelSize`, raycast preenche `numRays`/`jitterDeg`/`hitRatio`. Elementos ordenados por `GlobalId` (StringComparer.Ordinal) para output byte-estável.

### Schema JSON v2 — alvo (após Fase 4.3 / DBSCAN)

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

**Bloco `aggregates`.** Produzido a partir de `ModelLoadResult.Groups` (ADR-11). Lista cada `ElementGroup` com o conjunto de fachadas em que seus Elements participaram — útil para relatórios agrupados por cortina de vidro, escada, etc.

**Bloco `diagnostics`.** Coleta warnings do `XbimModelLoader` e dos Stages 1/2: elementos descartados por mesh vazio, triangulações convertidas por fan-fallback, faces *noise* do DBSCAN. Alimentado por `ILogger<T>` com sink em memória. Ver seção Determinismo e estratégia de testes.

### Schema JSON v3 — alvo (após Fase 6.4 / LoD 3.2)

Detalhamento em **ADR-15**: substitui v2 para o LoD 3.2 com `ElementProvenance: IReadOnlyCollection<string>` (GlobalIds) por LoD, satisfazendo a questão de pesquisa em qualquer nível de saída. Outros LoDs usam formatos naturais (GeoJSON para 0.x, glTF/OBJ para 2.x+).

---

## Viewer — Curadoria Assistida (ADR-07)

Segundo ponto de entrada: **ASP.NET Core Blazor Server + three.js**. Consome o mesmo `report.json` da CLI + o IFC original. A decisão sobre escopo (MVP Blazor vs absorção pelo debug-viewer) ocorre na **Fase 7** (out–dez/2026) — ver ADR-07 e ADR-17.

**MVP obrigatório:** render 3D colorido por fachada, filtro exterior/interior, inspeção por elemento (GlobalId, IfcType, `IIfcProductResolver`). Viewer **nunca re-executa o pipeline** — CLI = algoritmo automatizado, Viewer = revisão humana.

**Stretch goal** (condicional a Precision/Recall do Stage 1 aceitáveis e folga de cronograma): edição manual de rotulação + export BCF (ADR-06). Stage gate bloqueante: Viewer não inicia até pipeline produzir JSON válido (P3 concluído).

**Trabalhos Futuros (fora do escopo):** ingestão de BCF externo para re-calibrar algoritmo; re-execução sobre regiões editadas; histórico de rotulações; multi-usuário.

---

## Interface CLI

### Atual (Fase 3 — pós-PR #19)

```
ifcenvmapper detect --input <model.ifc> [opções]

Opções (já implementadas):
  -i, --input         <path>           IFC a analisar (obrigatório)
  -s, --strategy      <voxel|raycast>  Estratégia de detecção             [padrão: voxel — ADR-14]
  -v, --voxel-size    <metros>         Aresta do voxel (só com voxel)     [padrão: 0.25]
  -o, --output        <path>           Caminho do relatório (despacho por extensão):
                                          .json            → JsonReportWriter
                                          .bcf | .bcfzip   → BcfWriter
                                       Sem flag: console-only.

Exemplos:
  dotnet run --project src/Cli/Cli.csproj -- detect --input data/models/duplex.ifc
  dotnet run --project src/Cli/Cli.csproj -- detect -i duplex.ifc -s raycast -o report.json
  dotnet run --project src/Cli/Cli.csproj -- detect -i duplex.ifc -s voxel -v 0.5 -o report.bcf
```

### Alvo (a implementar em fases futuras)

```
  --grouper       <dbscan|...>                   Agrupamento em fachadas       (P4 — DBSCAN)
  --lod           <lista>                        LoDs a gerar (ADR-15)         (P6+; default `3.2`)
                                                 Válidos: 0.0,0.2,1.0,1.2,2.2,3.2,4.0,4.1,4.2
  --ground-truth  <labels.csv>                   Calcula contagens TP/FP/FN/TN  (P9 — Avaliação)
  --verbose                                      Logging detalhado              (qualquer fase)
  --num-rays      <int>       [raycast]          Raios por triângulo            (expor parâmetro hardcoded em RayCastingStrategy)
  --hit-ratio     <float>     [raycast]          Razão mínima exterior          (idem)
  --jitter-deg    <graus>     [raycast]          Cone de jitter da normal       (idem)

Exemplos (futuros):
  ifcenvmapper detect -i duplex.ifc --lod 1.0,3.2 -o report.json
  ifcenvmapper detect -i duplex.ifc --grouper dbscan -o report.json     # após Fase 4
  ifcenvmapper detect -i duplex.ifc --ground-truth data/ground-truth/duplex.csv
```

**Formato do ground truth CSV:**
```
GlobalId,IsExterior,Note
2O2Fr$t4X7Zf8NOew3FLne,true,
3xYmK9pQr2Wv7NLqZ1ABcd,false,
4D5eF1ABcdE2ghIJ3KLmnP,unknown,IfcSlab (auto)
```

`IsExterior` aceita `true` / `false` / `unknown` (esta última quando o IFC não declara o pset). `Note` é opcional — `GroundTruthGenerator.GenerateFromIfc` usa para indicar registros auto-gerados (`<IfcType> (auto)`).

---

## Modelos IFC Disponíveis

Modelos atuais em `data/models/` (5 arquivos vindos do voxelization_toolkit/tests/fixtures: `duplex`, `duplex_wall`, `schependom_foundation`, `demo2`, `covering`). A seleção final dos 5–8 modelos para experimentos será feita em P4.2 a partir de candidatos do BIMData R&D, OpenIFC Auckland, IFCNet RWTH Aachen e Purdue BIM Dataset — ver `00_Manuais_e_Referencias/datasets-ifc.md` para o catálogo completo, critérios de seleção e tabela de modelos selecionados (a preencher).

---

## Fases de Desenvolvimento

### Fase 0 — ✅ Spike: carregamento e triangulação (17/abr/2026 · 1 dia)
**Meta:** parsear um arquivo IFC real com xBIM e extrair geometria.
**Entrega:** `XbimModelLoader.Load()` v0 carrega `duplex.ifc` (157 elementos) e produz `Element` com `DMesh3` não-vazia. `Xbim3DModelContext.MaxThreads = 1` (workaround OCCT). Solução `.slnx`, pacotes NuGet básicos (Xbim.Essentials/Geometry, geometry4Sharp), CLI mínimo.

---

### Fase 1 — P1: Modelo refinado + testes-base + CI + Debug scaffold ✅ (17 → 19/abr/2026 · 3 dias)
**Meta:** absorver ADRs 02–17 no código e estabelecer infraestrutura de testes + debug geométrico antes de qualquer algoritmo novo.
**Entrega:** loader retorna `ModelLoadResult(Elements, Groups)` com filtro injetado e error handling tipado (`IfcLoadException`, `IfcGeometryException`). Domínio Core completo: `Element` (anêmico, ADR-08+11), `ElementGroup` (ADR-11), `Face/Envelope/Facade` (ADR-04), `DetectionResult/ElementClassification`. Interfaces de pipeline em Loading/Detection/Grouping; `XbimIfcProductResolver` (ADR-10); `GeometryDebug` scaffold com 10 métodos de primitivas (ADR-17). 34 testes unitários no CI (ubuntu-latest) + 2 integração local; CI GitHub Actions configurado.

---

### Fase 2 — P2: Validação da detecção + debug visual ✅ (19 → 24/abr/2026 · 5 dias)

**Meta:** Pipeline de detecção validado quantitativamente e inspecionável visualmente no debug-viewer.
**Referência canônica:** van der Vaart (2022) — IFC_BuildingEnvExtractor. Código-fonte em `Ferramentas/BuildingEnvExtractor/`.
**Entrega:** `VoxelFloodFillStrategy : IDetectionStrategy` (3 fases + `FillGaps`, ADR-14) + `PcaFaceExtractor : IFaceExtractor` (`OrthogonalPlaneFit3`); SAT triângulo-AABB próprio (Akenine-Möller 1997, `g4.IntrTriangle3Box3` ausente). Operações geométricas refatoradas como extension methods em `Core/Extensions/` (commit `f179d26`). Validação quantitativa: TP/FP/FN/TN + Precision/Recall via `EvaluationPipeline` em `duplex.ifc` (escolha metodológica: contagem estilo van der Vaart 2022 + Precision/Recall estilo Ying 2022; F1/Kappa descartados — ADR-12). Determinismo: ordenação estável por `GlobalId` com `StringComparer.Ordinal`.

**Debug (ADR-17, entregue divergindo do plano original):** `DebugSession` mantém estado e serve GLB via HTTP server em processo helper OS separado (commit `3148c34`) — em vez de `Flush()` para `%TEMP%`. `tools/debug-viewer/` modular (HTML + three.js, 6 arquivos), auto-start via `dotnet run`, picking voxel+elemento. Bloco de atualização em ADR-17.

**Stage gate para P4 e P6.3** liberado; P6.1 e P6.2 (LoDs 0.x e 1.x) podem iniciar independentemente do gate.

**Marco paralelo — Spike Viewer Blazor: cancelado.** Debug-viewer já cobre "carregar mesh + render + click → GlobalId"; absorção do Viewer MVP fica para Fase 7 (ADR-07 × ADR-17).

---

### Fase 3 — P3: RayCasting baseline + JSON + BCF ✅ (25 → 26/abr/2026 · 2 dias)

**Meta:** comparação Voxel vs RayCasting tabelada; output JSON e BCF mínimo operacionais.
**Entrega:** `RayCastingStrategy : IDetectionStrategy` (Ying 2022, ADR-14) — BVH global via `g4.DMeshAABBTree3` (ADR-13) + mapa de ownership por triângulo para auto-hit; `GeometryDebug.Line(...)` para raios (ADR-17). Ablation em `duplex.ifc` (Voxel P=0.849/R=0.918 vs RayCasting P=0.568/R=0.939) e `demo2.ifc` confirma tradeoff precision×recall da literatura. `DegradedFixtureTests` (enclosure 6 paredes ± gap) documenta leakage volumétrico do voxel × falha por face do raycast. Tabela comparativa em `data/results/strategy-comparison.md` (gitignored) regenerada por `StrategyComparisonTests`.

**Output:** `JsonReportWriter` (PR #16, schema **v1** — sem `facades`/`aggregates`); `BcfWriter` (PR #19, BCF 2.1 — um tópico por elemento exterior, viewpoint via `Components/Selection/Component@IfcGuid`); CLI `--strategy` + `--output` com despacho por extensão (`.json`/`.bcf`/`.bcfzip`); `ILogger<T>` ambient via `AppLog`. 168/168 testes verdes.

> RayCasting é baseline de comparação, não fallback de produção (ADR-14). Se Voxel falhar em fixtures críticos, a resposta é calibrar Voxel, não trocar estratégia.

---

### Fase 4 — P4: Domain refactor + IfcInspector + DbscanFacadeGrouper (27/abr → 19/jul/2026) · 12 semanas

**Meta agregada:** infraestrutura de domínio madura (P4.1) + ferramenta de inspeção que permite escolha informada dos modelos experimentais (P4.2) + `Facade[]` completo via DBSCAN sobre esfera de Gauss (P4.3).

---

**P4.1 — Domain refactor (27/abr → 03/mai/2026 · 1 semana)**

**Meta:** Hierarquia de entidades de domínio com base abstrata + interfaces de capacidade, preparando o terreno para o `IfcInspector` (P4.2) e dando bbox barato sem materializar mesh.
**Critério de sucesso:** todos os testes existentes verdes; `Element`/`Group` herdam de `IfcEntity` e implementam `IMeshEntity` + `IBoxEntity`; `BoundingBox` é campo armazenado populado a partir de `XbimShapeInstance.BoundingBox`.

- [ ] Interfaces de capacidade (siblings — `IMeshEntity` **não** estende `IBoxEntity`): `IIfcEntity`, `IBoxEntity`, `IMeshEntity` em `Core/Domain/`
- [ ] Classe abstrata `IfcEntity` em `Core/Domain/IfcEntity.cs` (`GlobalId`, `IfcType`, `Name` + igualdade por `GlobalId`)
- [ ] Renomear `Containment` → `Containment` (mecânico, ~10 arquivos)
- [ ] `Element` herda `IfcEntity`, implementa `IMeshEntity` + `IBoxEntity`; remove duplicação de igualdade
- [ ] `ElementGroup` herda `IfcEntity`, implementa `IMeshEntity` + `IBoxEntity`; substitui `OwnMesh?` por `Mesh` mesclado (próprio + filhos)
- [ ] `BoundingBox` torna-se campo armazenado, populado a partir de `XbimShapeInstance.BoundingBox` (já em coords mundo, conforme docs xBIM)
- [ ] Conversor `XbimGeometryConversions.ToAxisAlignedBox3d` em `src/Ifc/Loading/`
- [ ] `XbimModelLoader.Load` atualizado para popular bbox armazenado e mesh mesclado de `ElementGroup`

---

**P4.2 — IfcInspector + seleção de modelos (04/mai → 31/mai/2026 · 4 semanas)**

**Meta:** Ferramenta de inspeção rápida (sem geometria triangulada para Fase A) e candidatos a átrio identificados, permitindo selecionar 5–8 modelos finais para os experimentos.
**Critério de sucesso:** `inspect-all` em `data/models/candidates/` produz CSV agregado com flags de candidato a átrio; tabela "Modelos Selecionados" em `00_Manuais_e_Referencias/datasets-ifc.md` preenchida com 5–8 finais cobrindo tipologias diversas + ≥1 átrio coberto + ≥1 pátio aberto.

**P4.2.a — Domínio espacial + metadata loader (1 semana)**
- [ ] `Space`, `Storey` em `Core/Domain/Spatial/` (herdam `IfcEntity`)
- [ ] `ModelMetadata` em `Core/Pipeline/Loading/` (schema, authoring tool, project name)
- [ ] Estender `ModelLoadResult` com `Spaces`, `Storeys`, `Metadata` (atualizar ~6 call sites de `new ModelLoadResult(...)`)
- [ ] `XbimMetadataLoader.LoadMetadata(path)` — sem `Xbim3DModelContext`, retorna apenas metadados + contagens (~1–3s por modelo, ~10× mais rápido que o full Load)

**P4.2.b — Camada de inspeção (`src/Ifc/Inspection/`) — Fases A–E (2 semanas)**
- [ ] `IfcInspector.cs` — orquestrador
- [ ] **Fase A — Básico:** `BasicAnalyzer` (contagens por tipo, schema IFC, ferramenta de autoria, andares) — usa metadata loader
- [ ] **Fase B — Spaces:** `SpaceAnalyzer` (top-N spaces por volume, candidatos a átrio por aspecto vertical `Z_extent / sqrt(footprint)` ≥ 1.5 + busca textual em `LongName` por keywords `atrium / átrio / courtyard / pátio / lobby / void / well`)
- [ ] **Fase C — Batch:** `inspect-all` em diretório → CSV agregado (uma linha por arquivo, colunas chave: `file, schema, tool, walls, doors, windows, slabs, top_space_aspect, has_atrium_keyword, glass_roof_count, courtyard_count`)
- [ ] **Fase D — Roofs:** `RoofAnalyzer` (count + materiais associados via `IfcRelAssociatesMaterial`, detecção de cobertura vítrea por keywords `glass / vidro / glazed / transparent`)
- [ ] **Fase E — Footprint:** `FootprintAnalyzer` (projeção 2D de paredes externas, detecção de anéis interiores no concave hull para courtyards abertos via `NetTopologySuite`)
- [ ] CLI subcommands: `inspect` (single file) + `inspect-all` (directory)

**P4.2.c — Seleção dos modelos (1 semana)**
- [ ] Rodar `inspect-all` nos 5 modelos atuais — validar saída
- [ ] Baixar 10–15 candidatos do BIMData R&D (GitHub) + IFCNet (RWTH Aachen) + OpenIFC Auckland
- [ ] Selecionar 5–8 finais (cobertura: tipologias diversas + ≥1 átrio coberto + ≥1 pátio aberto + ≥1 pilotis se possível)
- [ ] Preencher tabela "Modelos Selecionados" em `00_Manuais_e_Referencias/datasets-ifc.md`

---

**P4.3 — DbscanFacadeGrouper (01/jun → 19/jul/2026 · 7 semanas)**

**Meta:** `Facade[]` completo com DBSCAN + QuikGraph.
**Pré-requisito:** Precision/Recall do Stage 1 aceitáveis — gate de P2 (thresholds calibrados após primeira medição; ver ADR-12). Calibrar DBSCAN antes de detecção confiável é desperdício.
**Critério de sucesso:** facades coerentes por plano dominante em 3+ modelos; WWR calculado por fachada. Debug-viewer permite inspecionar Gauss sphere + clusters.

- [ ] `DbscanFacadeGrouper : IFacadeGrouper` (DBSCAN sobre esfera de Gauss + QuikGraph para conectividade); chama `GeometryDebug.Points(...)` / `GeometryDebug.Lines(...)` internamente (ADR-17)
- [ ] **Instrumentação de debug crítica** (ADR-17): normais da esfera de Gauss como `GeometryDebug.Points()`, arestas do grafo de adjacência como `GeometryDebug.Lines()`, fachadas finais como `GeometryDebug.Triangles()` coloridas por `facadeId`
- [ ] Calibração empírica de ε e minPoints em fixtures — **usando debug-viewer para visualização** (Camada B de ADR-17)
- [ ] Opção: pré-filtro via `g4.NormalHistogram` (ADR-13) se ruído justificar
- [ ] Testes unitários do grouper + regressão por snapshot
- [ ] Schema JSON v2: adicionar blocos `facades` + `aggregates` ao `JsonReportWriter`

---

### Fase 5 — P5: *Hierarchical Voxel Flood-Fill* (contribuição original) (20/jul → 13/set/2026) · 8 semanas

**Meta:** implementar a estratégia de voxelização hierárquica e comparar com as duas baselines (Uniforme, *Ray Casting*) em precisão, *recall* e tempo de execução.
**Referência canônica:** van der Vaart (2022) para o *flood-fill* 3-fases; contribuição metodológica original para a hierarquia adaptativa e os critérios de refinamento.
**Pré-requisito:** Fase 3 (baselines Voxel + *Ray Casting*) concluída. Fase 4.3 (DBSCAN) **não é dependência algorítmica** — HVFF é Stage 1 (detecção), independente do agrupamento; a sequência calendar P4 → P5 é por priorização (front-load da contribuição). *Ground truth* da Fase 8 (Avaliação Experimental, paralela) já disponível em ≥3 modelos.
**Critério de sucesso:** (a) 3 estratégias rodam sobre os mesmos fixtures; (b) tabela com contagens TP/FP/FN/TN + Precisão/Recall + tempo de execução por estratégia × modelo; (c) análise por caso geométrico (poço de luz, *shaft* estreito) mostra onde cada estratégia falha ou acerta; (d) seção de Resultados da dissertação inclui a comparação 3-vias como figura principal da contribuição.

**P5.1 — Estrutura hierárquica de voxels (3 semanas)**
Ref: estruturas adaptativas (octree); ADR-13 (sem dependência externa).
- [ ] `HierarchicalVoxelGrid` — octree com níveis `L0 → L1 → ... → Lmax`; célula-folha carrega o mesmo estado (`Unknown/Occupied/Exterior/Interior/Void`) e lista de ocupantes por `GlobalId` já usados em `VoxelGrid3D`
- [ ] Critério de refinamento: célula na resolução `Li` refina para `Li+1` se `Occupied` **e** vizinhança contém mistura de estados (heurística inicial; calibrar empiricamente)
- [ ] Testes unitários da estrutura de dados (`IsInBounds`, `Neighbors*`, `WorldToCell`, transição entre níveis)

**P5.2 — `HierarchicalVoxelFloodFillStrategy` (3 semanas)**
- [ ] `HierarchicalVoxelFloodFillStrategy : IDetectionStrategy` — mesmo contrato de saída (`DetectionResult`)
- [ ] Rasterização multi-nível: SAT triângulo-caixa reutiliza `Core/Extensions/AxisAlignedBox3dExtensions` (Fase 2); nenhuma matemática nova
- [ ] *Flood-fill* atravessando níveis (propagação exterior desce nas folhas refinadas e sobe nas células grossas do espaço livre)
- [ ] Instrumentação `GeometryDebug` por nível + por fase (ADR-17) — crítica para *debug* visual no viewer
- [ ] Determinismo: ordenação estável por `GlobalId` na classificação final (mesma política de `VoxelFloodFillStrategy`)

**P5.3 — Comparação 3-vias + escrita dos Resultados (2 semanas)**
- [ ] `EvaluationPipeline` ampliado: executa 3 estratégias sobre o mesmo `ModelLoadResult`, compila tabela comparada
- [ ] Cobertura dos casos geométricos chave: pelo menos 1 *fixture* com poço de luz aberto, 1 com *shaft* de instalações, 1 com átrio coberto (ver §Comportamento em casos geométricos chave)
- [ ] Escrita da seção de Resultados: tabela 3-vias + discussão por caso + *ameaças à validade*
- [ ] Figura principal da contribuição: lado-a-lado dos três *grids* finais em um modelo com poço de luz

> Se a variante hierárquica **não** superar a uniforme em nenhuma métrica, a dissertação relata o resultado negativo e reforça o *flood-fill* uniforme como estado-da-arte prático — ainda é contribuição publicável (ablação rigorosa). Ver §Ameaças à validade.

---

### Fase 6 — P6: LoDs 0.x → 4.x (14/set → 15/nov/2026) · 9 semanas

**Meta:** espectro completo de representações geométricas via `--lod`, do footprint 2D ao element-wise, com rastreabilidade preservada por `ElementProvenance` em todos os níveis (ADR-15).
**Pré-requisito:** `Facade[]` entregue na Fase 4 (consumido a partir do LoD 3.x).
**Critério de sucesso:** LoDs 0.0, 0.2, 1.0, 1.2, 2.2, 3.2, 4.0, 4.1, 4.2 selecionáveis via `--lod`; testes unitários por gerador.

**P6.0 — Infra LoD (absorvido em P6.1)**
- [ ] `ILodGenerator` interface + `LodOutput` record + `LodRegistry` (foundational scaffold; concretes seguem nas sub-fases abaixo)

**P6.1 — LoD 0.x: Footprints 2D (2 semanas)**
Ref: Biljecki et al. (2016) — CityGML LoD framework; ADR-15.
- [ ] `Lod00FootprintXYGenerator` — projeção XY via `NetTopologySuite` (`STRtree` 2D + `UnaryUnionOp`); ADR-13
- [ ] `Lod02StoreyFootprintsGenerator` — footprints por `IfcBuildingStorey` via `IfcRelContainedInSpatialStructure`
- [ ] Testes unitários

**P6.2 — LoD 1.x: Blocos extrudados (1 semana)**
Ref: Biljecki et al. (2016); ADR-15.
- [ ] `Lod10ExtrudedBboxGenerator` — bloco extrudado do AABB global do modelo
- [ ] `Lod12StoreyBlocksGenerator` — bloco extrudado por pavimento
- [ ] Testes unitários

**P6.3 — LoD 2.x: Superfícies detalhadas (1 semana)**
Ref: Biljecki et al. (2016); ADR-15.
- [ ] `Lod22DetailedRoofWallsStoreysGenerator` — cobertura + paredes exteriores + lajes por pavimento
- [ ] Testes unitários

**P6.4 — LoD 3.x: Semantic shell (3 semanas)**
Ref: Biljecki et al. (2016); van der Vaart (2022); ADR-15.
- [ ] `Lod32SemanticShellGenerator` — consome `Facade[]` da Fase 4.3 (DBSCAN); seção `facades` + WWR
- [ ] Testes unitários Lod32

**P6.5 — LoD 4.x: Element-wise (stretch goal, 2 semanas)**
Ref: Biljecki et al. (2016); ADR-15.
- [ ] `Lod40ElementWiseGenerator` — todos os elementos 1:1
- [ ] `Lod41ExteriorElementsGenerator` — só os com face exterior
- [ ] `Lod42MergedSurfacesGenerator` — faces coplanares fundidas
- [ ] Testes unitários por gerador

---

### Fase 7 — P7: Viewer MVP OU absorção pelo debug-viewer (16/nov → 27/dez/2026) · 6 semanas

**Meta:** decisão sobre Viewer (ADR-07 × ADR-17) + implementação.
**Critério de sucesso:** usuário especialista consegue abrir artefatos e navegar resultados.

**Decisão sobre Viewer (ADR-07 × ADR-17):**
Nesta fase, avaliar o estado do `tools/debug-viewer/` (entregue em Fase 3):
- Se UX do debug-viewer estiver amigável a especialistas AEC → **absorver** o papel do Viewer pelo debug-viewer; descartar criação do projeto Viewer Blazor; energia concentra em polimento do debug-viewer.
- Se debug-viewer for adequado só para dev (UI técnica) → **Viewer MVP Blazor segue**:
    - [ ] `Components/`: render 3D por elemento colorido por fachada (consome LoD 3.2)
    - [ ] Filtro exterior/interior, inspeção (GlobalId, IfcType, `IIfcProductResolver`)
    - [ ] Overlay opcional de ground truth CSV
- Documentar decisão em ADR novo (ADR-18) na data.

**Stretch goal (condicional):**
- [ ] Edição manual de rotulação e export BCF editado — mantido como extensão opcional (ADR-07 original)

---

### Fase 8 — *Ground Truth* & Avaliação Experimental (mai – nov/2026, paralela)
**Meta:** validar o método contra rótulos manuais de especialistas.
**Critério de sucesso:** tabela com contagens TP/FP/FN/TN + Precision/Recall por modelo e por tipologia; ≥75% de concordância simples (*percent agreement*) entre especialistas na rotulação.

- [ ] Selecionar 3–5 modelos IFC de tipologias diferentes (planta retangular, L, curva/irregular)
- [ ] Protocolo de rotulação (critérios, ferramenta — provavelmente Viewer MVP, resolução de divergências)
- [ ] Recrutar 5+ profissionais AEC
- [ ] *Percent agreement* entre especialistas (contagem direta de rótulos concordantes / total)
- [ ] Tabela de resultados para a dissertação

---

### Fase 9 — Entrega (mar–abr/2027)
**Meta:** finalizar documentação, testes de usabilidade e publicação.
**Critério de sucesso:** defesa da Etapa 4 em abr/2027; repositório público e reproduzível.

- [ ] Testes de usabilidade do viewer com ≥3 especialistas AEC (debug-viewer ou MVP Blazor, conforme decidido em P7)
- [ ] README final (instalação, uso, exemplos, *workaround* Google Drive)
- [ ] Publicação no GitHub como repositório público
- [ ] Artefatos da dissertação: tabelas de resultado, figuras, links para reprodução

> **Nota:** Não há saída de IFC enriquecido. O modelo original não é modificado. Resultados são exclusivamente JSON + BCF.

---

## Critérios de Sucesso do TCC

A ferramenta é bem-sucedida academicamente quando:

1. **O método funciona de ponta a ponta** em modelos IFC reais de diferentes tipologias
2. **Resultados são mensuráveis**: Precisão e Recall calculados contra ground truth rotulado por especialistas
3. **Rastreabilidade preservada**: cada face detectada e cada fachada agrupada são rastreáveis ao `Element` de origem
4. **Aplicabilidade demonstrada**: WWR por fachada calculado a partir dos resultados de detecção
5. **O resultado é reproduzível**: qualquer pessoa com .NET 8 pode rodar `dotnet run` e obter os mesmos números

---

