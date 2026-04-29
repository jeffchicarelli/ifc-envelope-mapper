# Plano de Implementação — IfcEnvelopeMapper

> Documento vivo. Atualizar a cada sessão de desenvolvimento.
> Última atualização: 2026-04-28.

---

## 1. Visão Geral

### 1.1 Sumário

**Problema.** Identificar elementos de fachada em modelos IFC é trabalho manual em ferramentas BIM. Este TCC investiga se um método **puramente geométrico** — sem usar metadados como `IsExternal` — consegue automatizar essa identificação preservando rastreabilidade ao `GlobalId` IFC de cada elemento, em **todos os níveis de detalhe** do framework CityGML / Biljecki, do *footprint* 2D ao *element-wise*.

**Método.** Pipeline progressivo em camadas LoD com identidade de elemento preservada em cada nível:

1. **Detecção** (`IEnvelopeDetector`) — três estratégias comparadas: voxel + *flood-fill* uniforme (ablation baseline, van der Vaart 2022); *ray casting* por face (baseline externo, Ying 2022); voxelização hierárquica com *flood-fill* (contribuição original).
2. **Agrupamento em fachadas** (`IFacadeGrouper`) — DBSCAN sobre esfera de Gauss + componentes conectados em adjacência espacial → `Facade[]`.
3. **Computação multi-LoD** (`ILoDComputer`) — produz LoD0 a LoD5 a partir do mesmo `DetectionResult`, mantendo `IElement` referenciados em cada nível.
4. **Extração de *features* topológicas** (`IFeatureExtractor`) — poço de ventilação, recesso, átrio, recuo, beiral, balanço, etc. como *queries* sobre as camadas LoD.

**Saídas.** JSON, BCF 2.1, IFC enriquecido (caminho de volta ao BIM authoring), GLB para visualização. Ver §5.

**Validação.** Estratégia triangulada em três camadas: (C1) comparação automatizada com IFC_BuildingEnvExtractor (van der Vaart, 2022); (C2) auto-rotulação documentada do pesquisador como *ground truth* primário; (C3) revisão visual por profissionais AEC mediante *survey* baseado em arquivos BCF e capturas de tela. Métricas: contagens TP/FP/FN/TN + Precisão/Recall por estratégia × modelo. Detalhamento em §3.6.

### 1.2 Status atual

**Encerrado:**

- Fase 0 ✅ Spike (1 dia)
- Fase 1 ✅ Modelo refinado + CI + scaffold de debug (3 dias)
- Fase 2 ✅ Voxel + EvaluationPipeline (5 dias)
- Fase 3 ✅ RayCasting + JSON + BCF (2 dias)
- Fase 4 ✅ Refatoração de domínio + API de Visualização + DBSCAN + Clean Architecture (2 dias)

**Em andamento:** abertura da Fase 5 — Arquitetura Multi-LoD.

**Próximas fases:** Fase 5 (10 sem) → Fase 6 (8 sem) → Fase 7 (6 sem) → Fase 8 (paralela) → Fase 9 (entrega abr/2027). Detalhes em §8.

---

## 2. Conceitos e Terminologia

| Termo | Definição |
|---|---|
| **IFC** | Industry Foundation Classes — padrão aberto ISO 16739 para intercâmbio de dados BIM. |
| **BIM** | Building Information Modelling — metodologia de representação digital integrada de uma edificação. |
| **xBIM** | Xbim Toolkit — biblioteca .NET para leitura, escrita e consulta de modelos IFC. |
| **BCF** | BIM Collaboration Format — formato de marcação de modelos BIM para revisão e comunicação. |
| **CityGML** | Padrão OGC para modelos 3D de cidades. Versão 3.0 (Kutzner et al., 2020) define a taxonomia de superfícies (`WallSurface`, `RoofSurface`, `GroundSurface`, `OuterCeilingSurface`, `OuterFloorSurface`, `ClosureSurface`) adotada neste trabalho. |
| **LoD** *(Level of Detail)* | Nível de detalhe de um modelo 3D. Referência: Biljecki, Ledoux & Stoter (2016); estendido com LoD4/LoD5 por van der Vaart, Arroyo Ohori & Stoter (2025). |
| **Envoltório** *(building envelope)* | Conjunto de todas as superfícies exteriores de uma edificação que separam o interior do externo, em todas as orientações (paredes, coberturas, pisos, aberturas). Definido pela função de separação interior/exterior (ASHRAE 90.1; Sadineni et al., 2011). |
| **Casca geométrica** *(building shell)* | Superfície envolvente computada por operações geométricas (voxelização, *alpha wrapping*) sem rastreabilidade aos elementos IFC de origem. Excluída como abordagem deste trabalho. |
| **Fachada** *(facade)* | Região contínua da superfície exterior do envoltório, caracterizada por uma orientação dominante (vetor normal médio das faces convergentes). Inclui qualquer orientação — sem limiar angular arbitrário. Elementos IFC são *participantes*: um elemento de canto participa de duas fachadas (relação muitos-para-muitos). |
| **Face** | Unidade atômica de superfície exterior: triângulos de um elemento IFC pertencentes a um mesmo plano ajustado. Preserva rastreabilidade ao `Element` de origem. |
| **Plano dominante** | Direção média de um grupo de normais detectado por DBSCAN sobre a esfera de Gauss. |
| **Direção** *(Top/Side/Bottom — Topo/Lateral/Base)* | Categorização de uma face exterior pela orientação da normal. Limiar angular: 70° em relação ao plano horizontal — convenção CityGML 3.0 (Donkers et al., 2015). |
| **Materialidade** *(Physical/Virtual/Both — Físico/Virtual/Ambos)* | Classificação de uma região de superfície: **Físico** (lastrada por `IElement` IFC), **Virtual** (`ClosureSurface` CityGML — superfície que fecha um vazio lógico), **Ambos** (mista, ex.: parede com janela). |
| **Contorno** *(outline)* | Polígono 2D fechado (anel externo + zero ou mais anéis internos) representando a fronteira de uma região. Cada `Outline` carrega `IElement` referenciados. |
| **Projeção** *(footprint)* | Contorno ao nível do solo. Pode ser por pavimento (LoD0.2) ou da edificação inteira (LoD0.0). |
| **Poço de ventilação** *(airwell)* | Anel interior presente *simultaneamente* na projeção LoD0 do solo e no contorno LoD2 do telhado — vazio vertical aberto ao céu. Caso adversarial canônico. |
| **Poço de luz** *(light shaft)* | Anel interior presente na projeção LoD0 mas **fechado** no LoD2 — vazio vertical coberto por telhado ou *skylight*. |
| **Recesso** *(recess)* | Concavidade no anel exterior da projeção (diferença entre *convex hull* e anel externo) **com pelo menos uma abertura** (`IfcWindow`/`IfcDoor`) voltada para a concavidade. Definição não ancorada em literatura *peer-reviewed* — proposta como contribuição metodológica (ver §7 ADR-15 e §9). |
| **Classe de espaço** *(Indoor/SemiIndoor/SemiOutdoor/Outdoor — Interno/Semi-interno/Semi-externo/Externo)* | Classificação topológica de espaços interiores (LoD4) pelo número de lados materializados. Ancorada em Chun, Kwok & Tamura (2004) sobre *transitional spaces*; Spagnolo & de Dear (2003) para o caso subtropical. **Interno** (6 lados Físicos), **Semi-interno** (4–5 lados + topo Físico), **Semi-externo** (2–4 lados + topo Virtual), **Externo** (≤1). A métrica N-Sides é a formalização computável proposta por este trabalho. |
| **Ground truth** | Conjunto de rótulos de referência produzido por rotulação manual de especialistas AEC. Base para contagens TP/FP/FN/TN e cálculo de Precisão e Recall. |
| **TP / FP / FN / TN** | Contagens da matriz de confusão de classificação binária. Estilo de reporte seguindo van der Vaart (2022). |
| **Precisão / Recall** | Métricas derivadas das contagens, conforme Ying et al. (2022, Eq. 12–13). F1 e Kappa intencionalmente fora — não aparecem nas referências canônicas (van der Vaart 2022 usa contagens, Ying 2022 usa Precisão/Recall). |
| **DBSCAN** | *Density-Based Spatial Clustering of Applications with Noise* — algoritmo de *clustering* sem número fixo de grupos. Usado para agrupar normais na esfera de Gauss. |
| **BVH** | *Bounding Volume Hierarchy* — estrutura de aceleração espacial para *ray casting*. |
| **WWR** | *Window-to-Wall Ratio* — razão entre área de janelas e área total de parede por fachada. |

---

## 3. Objetivo e Método de Pesquisa

### 3.1 Pergunta de pesquisa

> Como projetar e avaliar uma ferramenta de software para inferência automática de fachadas em modelos IFC, baseada exclusivamente em geometria 3D, que preserve rastreabilidade entre superfícies detectadas e elementos IFC originais, e seja arquitetonicamente extensível para futuras estratégias e formatos de saída?

A pergunta articula três dimensões da contribuição: (i) o **método geométrico** *(geometry-first)*, sem dependência de metadados IFC; (ii) a **preservação de rastreabilidade** ao `GlobalId` IFC em todos os níveis de detalhe — lacuna documentada em van der Vaart (2025); (iii) a **extensibilidade arquitetural** sob *Clean Architecture* — atributo de qualidade central a um trabalho de Engenharia de Software, materializado pelo desacoplamento das estratégias de detecção via portas e adaptadores.

**Sub-questão técnica (Fase 6).** Um esquema de voxelização hierárquica com *flood-fill* supera a voxelização uniforme (van der Vaart, 2022) e o *ray casting* (Ying et al., 2022) em precisão e *recall* sobre modelos IFC reais, preservando rastreabilidade ao `GlobalId` e agregação em fachadas por plano dominante?

### 3.2 Três estratégias de detecção

| Critério | Voxel uniforme | Voxel hierárquico (contribuição) | Ray Casting (baseline externo) |
|---|---|---|---|
| **Papel** | *ablation baseline* | estratégia proposta | comparação algorítmica |
| **Referência principal** | van der Vaart (2022); Liu et al. (2021) | original | Ying et al. (2022) |
| **Princípio** | discretização em voxels uniformes + 3 fases *flood-fill* | voxelização multi-resolução (octree) + *flood-fill* atravessando níveis | raio por face na direção da normal escapa sem interceptar outro elemento |
| **Robustez a meshes malformados** | alta — voxel contorna *gaps* | alta (mesma família) | baixa — raio sensível a *gaps* |
| **Precisão em detalhes finos** | limitada pelo tamanho do voxel | alta — refina onde a geometria exige | alta — cada face testada individualmente |
| **Custo de refinamento global** | cresce como O(V) com refinamento cúbico | proporcional à área da casca, não ao volume total | independente de voxel |
| **Rastreabilidade** | preservada via `grid[v].Elementos` | preservada (mesmo padrão) | nativa — raio por face do elemento |

A `NormalsStrategy` (variante trivial) foi descartada por não contribuir comparação científica relevante (ver §7 ADR-14).

### 3.3 Limitações documentadas e motivação para a contribuição

**Voxel uniforme** (van der Vaart, 2022). A resolução do voxel limita a precisão geométrica — *features* menores que o tamanho do voxel desaparecem. Com voxel de 0,5 m (resolução típica recomendada pelo autor), recuos de 300 mm são invisíveis ao algoritmo. Refinar para capturar detalhe fino multiplica o custo computacional **cubicamente**: 0,5 m → 0,25 m exige 8× mais voxels, com impacto correspondente em memória e tempo de execução. A própria avaliação técnica do `IFC_BuildingEnvExtractor` documenta que a ferramenta é projetada para abstração em escala GIS (LoD0–LoD2 / CityJSON), não para verificação de precisão arquitetônica.

**Ray casting** (Ying, 2022). Duas classes de falha documentadas:

1. **Sensibilidade a malhas malformadas.** Um *gap* de triangulação na casca gera falso positivo direto — o raio escapa por uma fresta que não existe no modelo físico. Auto-interseções produzem *hits* espúrios contra o próprio elemento. Modelos IFC reais raramente têm casca topologicamente perfeita (ver §6.3).
2. **Falha topológica em geometria de bolso.** Em poço de ventilação central ou *shaft* estreito, raios horizontais a partir das paredes internas batem na parede oposta do mesmo poço, classificando-as como interior — falso negativo sistemático no caso adversarial canônico (ver §6.1).

**Síntese e motivação.** Voxel uniforme **acerta a topologia** (poço de ventilação aberto → exterior) mas paga custo global cúbico para resolver detalhe local. Ray casting tem **precisão por face** mas falha topologicamente em concavidades. Nenhuma das duas combina robustez topológica com custo proporcional à complexidade da casca. Esta é a lacuna que a **voxelização hierárquica com *flood-fill*** (Fase 6, contribuição original) ataca: refina adaptativamente apenas na vizinhança da casca e dos detalhes finos (janelas, *shafts*, recessos), mantendo o *flood-fill* volumétrico em células grandes onde o espaço livre é simples. A robustez topológica vem de manter a família algorítmica do van der Vaart (2022); o ganho de eficiência vem do refinamento espacial adaptativo.

### 3.4 Métricas de avaliação

Contagens TP/FP/FN/TN + Precisão e Recall por estratégia × modelo (Ying et al. 2022, Eq. 12–13). F1 e Kappa intencionalmente fora — não aparecem nas referências canônicas. Determinismo: ordenação estável por `GlobalId` com `StringComparer.Ordinal` antes de serializar.

### 3.5 Objetivos

**Objetivo geral.** Projetar, implementar e avaliar uma ferramenta de software de linha de comando, em C#/.NET, capaz de identificar automaticamente fachadas em modelos IFC mediante inferência geométrica pura, preservando rastreabilidade aos elementos IFC originais e disponibilizada em arquitetura extensível para servir como base reutilizável a desenvolvedores e pesquisadores AEC.

**Objetivos específicos.**

1. Implementar *pipeline* geométrico em quatro estágios — carga, detecção, agrupamento, *multi-LoD* — sob *Clean Architecture*, com separação rigorosa entre domínio, aplicação, infraestrutura e CLI.
2. Comparar experimentalmente as estratégias de detecção (voxel + *flood-fill* uniforme e *ray casting* por face) em Precisão e Recall sobre modelos IFC públicos.
3. Validar resultados em três camadas complementares (ver §3.6).
4. Produzir saídas em múltiplos formatos preservando rastreabilidade ao `GlobalId` IFC: JSON · BCF 2.1 · IFC enriquecido (`Pset_IEM_*` + `IfcGroup`) · GLB.
5. Demonstrar extensibilidade arquitetural por meio de pelo menos uma extensão concreta do *pipeline* — por exemplo, substituição da estratégia de detecção sem modificação dos demais estágios.

### 3.6 Estratégia de validação — *ground truth* triangulado

| Camada | O que é | Quem faz | Função |
|---|---|---|---|
| **C1** | Comparação automatizada com IFC_BuildingEnvExtractor (van der Vaart, 2022) sobre os mesmos modelos. | Automatizada (script). | Reduz viés de rotulador único; referencia o estado da arte. |
| **C2** | Auto-rotulação documentada do pesquisador como *ground truth* primário, com diretrizes explícitas e registro do tempo dedicado por modelo. | Pesquisador. | Base operacional para as métricas de Precisão e Recall. |
| **C3** | Revisão visual por profissionais AEC mediante *survey* baseado em capturas de tela e arquivos BCF. **Profissionais não rotulam do zero — apenas revisam** os resultados produzidos pela ferramenta. | 5–10 profissionais AEC. | Validação qualitativa de coerência arquitetônica e utilidade prática. |

A triangulação é robusta e operacionalmente viável: profissionais AEC não precisam instalar a ferramenta de pesquisa para participar; a comparação automatizada com a ferramenta canônica complementa a rotulação humana; e o *survey* (C3) é executável via Google Forms a partir de imagens. Detalhamento do instrumento C3 em `02_Etapa2_Questionario/esboco-survey-especialistas.md`.

---

## 4. Arquitetura

### 4.1 Stack de tecnologias

| Biblioteca | NuGet | Uso |
|---|---|---|
| **xBIM Essentials** | `Xbim.Essentials` | leitura de modelos IFC, schema IFC4 |
| **xBIM Geometry** | `Xbim.Geometry` | triangulação de geometria via `Xbim3DModelContext` |
| **geometry4Sharp** | `geometry4Sharp` | mesh 3D (`DMesh3`), BVH (`DMeshAABBTree3`), normais, *plane-fit* PCA, *eigen*, esfera de Gauss |
| **SharpGLTF** | `SharpGLTF.Toolkit` | escrita de GLB para visualização e debug (ADR-17) |
| **System.CommandLine** | `System.CommandLine` | parser de argumentos da CLI |
| **Microsoft.Extensions.Logging** | `Microsoft.Extensions.Logging` | *logging* ambiente via `AppLog` |
| **NetTopologySuite** | `NetTopologySuite` | polígonos 2D, união, anéis internos, *convex hull* (LoD0) |
| **LibTessDotNet** | `LibTessDotNet` | triangulação de polígono-com-furos (NTS → `g4.DMesh3`) |
| **QuikGraph** | `QuikGraph` | grafo de adjacência espacial, componentes conectados |

C#/.NET 8 + xUnit + FluentAssertions. **Política de bibliotecas:** usar a biblioteca quando ela cobrir o caso de uso; substituir por implementação própria apenas se a dependência não for extensivamente utilizada no projeto.

### 4.2 Estrutura do projeto (Clean Architecture)

```
┌────────────────────────────────────────────────────────────────────────────┐
│                          Cli  (composition root)                            │
│   Program.cs · DI wiring · DetectCommand                                    │
└──────────────┬───────────────────────────────────┬─────────────────────────┘
               │                                   │
               ▼                                   ▼
┌──────────────────────────────────┐   ┌──────────────────────────────────────┐
│           Application             │   │           Infrastructure              │
│  Orquestração · portas · builders │   │  Implementações concretas            │
│                                   │   │                                      │
│  Ports                            │   │  Loading                             │
│  ─                                │   │  ─                                   │
│  IModelLoader                     │   │  XbimModelLoader · ElementFilter     │
│  IBcfWriter                       │   │                                      │
│  IJsonReportWriter                │   │  Detection                           │
│  IGroundTruthReader               │   │  ─                                   │
│                                   │   │  VoxelFloodFillDetector              │
│  Reports                          │   │  RayCastingDetector                  │
│  ─                                │   │  HierarchicalVoxelFloodFillDetector  │
│  JsonReportBuilder                │   │  PcaFaceExtractor                    │
│  BcfBuilder                       │   │  DbscanFacadeGrouper                 │
│  DetectionReport · BcfPackage     │   │                                      │
│                                   │   │  Lod                                 │
│  Evaluation                       │   │  ─                                   │
│  ─                                │   │  LoD{0,1,2,4,5}Computer              │
│  EvaluationService                │   │  MeshToPolygonConverter              │
│  MetricsCalculator                │   │  PolygonToMeshConverter              │
│                                   │   │                                      │
│                                   │   │  Features                            │
│                                   │   │  ─                                   │
│                                   │   │  AirwellDetector · RecessDetector    │
│                                   │   │  AtriumDetector · OverhangDetector   │
│                                   │   │  ...                                 │
│                                   │   │                                      │
│                                   │   │  Persistence                         │
│                                   │   │  ─                                   │
│                                   │   │  JsonReportWriter · BcfWriter        │
│                                   │   │  EnrichedIfcWriter                   │
│                                   │   │  GroundTruthCsvReader                │
│                                   │   │                                      │
│                                   │   │  Visualization (ADR-17)              │
│                                   │   │  ─                                   │
│                                   │   │  GeometryDebug · Scene · Color       │
│                                   │   │  GltfSerializer · ViewerHelper       │
└──────────────┬───────────────────┘   └──────────────┬───────────────────────┘
               │                                      │
               └──────────────────┬───────────────────┘
                                  ▼
┌────────────────────────────────────────────────────────────────────────────┐
│                                Domain                                       │
│                  Modelo de negócio puro · sem xBIM                          │
│                                                                             │
│  Capability interfaces            Surface types                             │
│  ─                                ─                                         │
│  IIfcEntity                       Envelope · Facade · Face                  │
│  IBoxEntity                       Outline · Footprint · Direction           │
│  IMeshEntity                      WallSurface · RoofSurface · GroundSurface │
│  IElement                         OuterCeilingSurface · OuterFloorSurface   │
│                                   RoofPatch · RoofShell · BlockModel ·      │
│  Pipeline interfaces              StoreyBlock                               │
│  ─                                                                          │
│  IEnvelopeDetector                Voxel · Espaços                           │
│  IFaceExtractor                   ─                                         │
│  IFacadeGrouper                   VoxelGrid3D · VoxelState                  │
│  ILoDComputer                     Building · Storey · Space                 │
│  IFeatureExtractor                SpaceClass · EnclosureProfile             │
│                                                                             │
│  Detection · Avaliação                                                      │
│  ─                                                                          │
│  DetectionResult · ElementClassification · StrategyConfig                   │
│  DetectionCounts · EvaluationResult · GroundTruthRecord                     │
│                                                                             │
│  Extensions (math)                                                          │
│  ─                                                                          │
│  AxisAlignedBox3d · DMesh3 · Plane3d · Vector3d · VoxelGrid3D · BoxEntity   │
└────────────────────────────────────────────────────────────────────────────┘

┌────────────────────────────────────────────────────────────────────────────┐
│  DebugServer  (EXE não-referenciado · ADR-17)                               │
│  HttpListener loopback :5173 → tools/debug-viewer/ (HTML + three.js)        │
│  Spawnado via Process.Start por ViewerHelper · processo OS isolado para     │
│  sobreviver ao freeze do debugger .NET com Suspend: All                     │
└────────────────────────────────────────────────────────────────────────────┘
```

**Regra de dependência (Clean Architecture).** As setas apontam apenas para dentro: `Cli → Application → Domain` e `Cli → Infrastructure → {Domain, Application}`. `Domain` não importa nada de fora. `DebugServer` é EXE *standalone*, não-referenciado por gerenciado — é *spawnado* via `Process.Start` apenas em *runtime*, fora do grafo de dependências de *build*.

| Projeto | Dependências externas |
|---|---|
| `Domain` | `geometry4Sharp` |
| `Application` | `Domain` |
| `Infrastructure` | `Domain`, `Application`, `Xbim.Essentials`, `Xbim.Geometry`, `NetTopologySuite`, `LibTessDotNet`, `SharpGLTF.Toolkit`, `QuikGraph` |
| `Cli` | `Application`, `Infrastructure`, `System.CommandLine` |
| `DebugServer` | nenhuma |

Projetos de teste: `Domain.Tests` (sem IFC), `Infrastructure.Tests` (com xBIM), `Integration.Tests` (ponta-a-ponta).

### 4.3 Modelo de domínio

#### Entidades principais

| Entidade | Camada | Responsabilidade |
|---|---|---|
| `Element` | Infrastructure | `IIfcProduct` + *lazy mesh*/*bbox*; composites via `Children[]` |
| `Storey` | Infrastructure | `IfcBuildingStorey` + elevação; obrigatório (§4.6) |
| `Building` | Domain | *Aggregate root*: `Storeys[]`, contornos, *features* |
| `Space` | Domain | Volume interior derivado de `VoxelGrid3D.GrowInterior()` + classe |
| `Face` | Domain | Região planar exterior, rastreável a `Element` |
| `Facade` | Domain | `Face[]` agrupadas por orientação dominante |
| `Outline` / `Footprint` | Domain | Polígono 2D + Direção + elevação |
| `BlockModel` / `RoofShell` / `RoofPatch` | Domain | Saídas LoD1 / LoD2 |

#### Interfaces de capacidade (Domain)

- `IIfcEntity` — `GlobalId`, `Name`
- `IBoxEntity` — `GetBoundingBox()` *lazy*
- `IMeshEntity` — `GetMesh()` *lazy*
- `IElement = IIfcEntity + IBoxEntity + IMeshEntity`
- `IProductEntity` (Infrastructure) — navegação xBIM (`GetIfcProduct/Site/Building/Storey`)

Ortogonais; sem classe abstrata comum. Cada concrete implementa apenas o que faz sentido + `IEquatable<T>` próprio. Razões em §7 ADR-08 + ADR-11.

#### Interfaces de pipeline

| Interface | Camada | Stage |
|---|---|---|
| `IModelLoader` | Application | 0 |
| `IEnvelopeDetector` | Domain | 1 |
| `IFacadeGrouper` | Domain | 2 |
| `ILoDComputer` | Domain | 3 |
| `IFeatureExtractor` | Domain | 4 |
| `IReportWriter` (família) | Application | 5 |

### 4.4 Pipeline em 5 estágios

```
IFC Model (Site → Building → Storey → Elements — hierarquia obrigatória, §4.6)
   │
   ▼
[Stage 0 — IModelLoader.Load]                ModelLoadResult { Building, Storeys, Elements }
   │
   ▼
[Stage 1 — IEnvelopeDetector.Detect]          DetectionResult { Envelope, Classifications }
   │
   ▼
[Stage 2 — IFacadeGrouper.Group]              Facade[]                  (LoD3)
   │
   ▼
[Stage 3 — ILoDComputer.ComputeAll]           MultiLoDResult
   │                                              ├─ LoD0 footprint(s)
   │                                              ├─ LoD1 block model
   │                                              ├─ LoD2 roof shell
   │                                              ├─ LoD3 (link a Stage 1+2)
   │                                              ├─ LoD4 spaces
   │                                              └─ LoD5 voxel grid
   ▼
[Stage 4 — IFeatureExtractor.Extract]         BuildingFeatures
   │                                              Airwell, LightShaft, Recess, Atrium,
   │                                              Setback, Eave, Cantilever, ...
   ▼
[Stage 5 — Reports]                           JSON v3 / BCF / IFC enriquecido / GLB
```

Cada estágio é descrito abaixo no formato uniforme: implementação · referências · passos.

#### Stage 0 — `IModelLoader.Load`

**Implementação:** `Infrastructure/Ifc/Loading/XbimModelLoader.cs`

1. `IfcStore.Open(path)` → STEP parsing (mantém o store **aberto** pela vida do `ModelLoadResult`)
2. `Xbim3DModelContext.CreateContext()` (`MaxThreads=1`, *workaround* OCCT)
3. Validação de hierarquia espacial (Site → Building → Storey → Elements; rejeita modelos incompletos com exceção tipada)
4. Para cada `IIfcElement` filtrado por `ElementFilter`: vira `Element` *standalone* ou *composite* (com `Children` populado)
5. Retorna `ModelLoadResult(Building, Storeys, Elements, Composites) : IDisposable`

#### Stage 1 — `IEnvelopeDetector.Detect`

**Implementação:** `Infrastructure/Detection/{VoxelFloodFill, RayCasting}Detector.cs`
**Referências:** van der Vaart (2022); Liu et al. (2021); Akenine-Möller (1997) — Voxel. Ying et al. (2022) — Ray Casting.

Voxel *flood-fill*: SAT triângulo-AABB cascata 4-testes → cada voxel ocupado guarda lista de `GlobalId`s → `FillGaps` (fechamento morfológico pré-*flood*) → 3 fases (`GrowExterior` → `GrowInterior` → `GrowVoid`) → classifica elemento como exterior se ≥1 voxel ocupado por ele tem vizinho-26 marcado Exterior. Faces extraídas via `PcaFaceExtractor` (`g4.OrthogonalPlaneFit3`).

Ray Casting: BVH global (`g4.DMeshAABBTree3`) + mapa triângulo→elemento → `numRaios` raios por triângulo a partir de `centroid + ε·normal` com *jitter* de ±5° → triângulo é exterior se `escapes / numRaios ≥ hitRatio`.

#### Stage 2 — `IFacadeGrouper.Group`

**Implementação:** `Infrastructure/Detection/DbscanFacadeGrouper.cs`
**Referência:** Ester & Kriegel (1996) — DBSCAN.

DBSCAN com distância angular `arccos(n1·n2)`, ε=15°, *minPoints*=3 → cada *cluster* é uma orientação dominante. Faces sem *cluster* são descartadas como ruído. Componentes conectados em adjacência espacial (centroide ≤ 3 m) separam superfícies fisicamente desconexas (ex.: parede norte frontal vs. parede norte do poço de luz). Fachadas ordenadas por azimute crescente (facade-00 = mais setentrional).

#### Stage 3 — `ILoDComputer.ComputeAll`

**Implementação:** `Infrastructure/Lod/LoD{0,1,2,4,5}Computer.cs`
**Referências:** Biljecki, Ledoux & Stoter (2016); van der Vaart, Arroyo Ohori & Stoter (2025); Kutzner, Chaturvedi & Kolbe (2020) — CityGML 3.0; Donkers et al. (2015) — limiar angular 70°.

Cada LoD é uma transformação do `DetectionResult + Facade[]` preservando `IElement` em todos os níveis.

- **LoD0** — por pavimento: projeta `AxisAlignedBox3d` no plano XY → união via `NetTopologySuite.UnaryUnionOp` produz `Polygon` com anel externo e anéis internos (= candidatos a poço de ventilação ou poço de luz).
- **LoD1** — extrusão *quad-strip* da projeção LoD0 entre elevações do `IfcBuildingStorey`; *watertight* por construção sobre `g4.DMesh3`, sem *boolean union*.
- **LoD2** — filtra `Envelope.Faces` por `Direction.Top`, reaproveita o agrupador DBSCAN (`OrientedSurfaceDbscan` com filtro de orientação) → `RoofPatch[]`; união XY produz `TopOutline`.
- **LoD4** — para cada *room* de `VoxelGrid3D.GrowInterior()`: examina os 6 lados (Topo, Base, 4 cardinais) verificando se há voxels `Occupied` na fronteira → conta lados materializados (`NSidesEnclosed` ∈ [0, 6]) → classifica como **Interno** / **Semi-interno** / **Semi-externo** / **Externo** (formalização de Chun, Kwok & Tamura 2004).
- **LoD5** — `VoxelGrid3D` exposto como artefato (já produzido no Stage 1).

#### Stage 4 — `IFeatureExtractor.Extract`

**Implementação:** `Infrastructure/Features/{Airwell, LightShaft, Recess, Atrium, ...}Detector.cs`
**Referências:** Rojas-Fernández et al. (2017) — *aspect ratio* pátio vs. poço de luz; definições topológicas próprias (ver §7 ADR-15).

*Features* são consultas sobre as camadas LoD — não algoritmos de detecção novos.

- **Poço de ventilação** *(airwell)* — *hole* presente em `LoD0.Holes ∩ LoD2.TopOutline.Holes`.
- **Poço de luz** *(light shaft)* — *hole* em `LoD0.Holes ∖ LoD2.TopOutline.Holes` (fechado no topo por *skylight* ou laje).
- **Recesso** — concavidade `convexHull(LoD0.OuterRing) ∖ LoD0.OuterRing` com pelo menos uma abertura (`IfcWindow`/`IfcDoor`) na região; sem abertura, descartado.
- **Recuo** *(setback)* — `LoD0.2.Storey[i].Outer ∖ LoD0.2.Storey[i+1].Outer` quando área > limiar.
- **Beiral** *(overhang/eave)* — `LoD2.TopSilhouette ∖ LoD1.TopSilhouette`.
- **Átrio** *(atrium)* — `LoD4.Space` com topo de materialidade Virtual e *vertical span* > 1 pavimento.
- **Pátio** vs. **Poço de luz**: átrio com *horizontal aspect / vertical aspect > 1* é pátio (largo > alto); o inverso é poço de luz (Rojas-Fernández 2017).
- **Balanço, platibanda, forro externo, sacada, terraço, galeria coberta** — definidos analogamente como *queries* em LoD1/LoD2/LoD4 com critérios de direção e materialidade.

#### Stage 5 — Saídas

**Implementação:** `Application/Reports/{JsonReportBuilder, BcfBuilder, EnrichedIfcBuilder}.cs`; `Infrastructure/Persistence/{JsonReportWriter, BcfWriter, EnrichedIfcWriter}.cs`.

Cinco formatos de saída — ver §5.

### 4.5 Ponte 2D ↔ 3D (`NetTopologySuite` ↔ `g4`)

NetTopologySuite opera sobre `Polygon` 2D (plano XY); a visualização e as malhas operam sobre `g4.DMesh3` (3D). Duas conversões são necessárias.

1. **NTS Polygon → g4.DMesh3** (visualização — direção primária). Coloca o polígono 2D em 3D na elevação z, triangula com furos via LibTessDotNet (*ear-clipping* com suporte a *holes*), emite como `DMesh3` para a *pipeline* GLB.
2. **g4.DMesh3 → NTS Polygon** (produção LoD0 — direção secundária). Para cada elemento, projeta o `AxisAlignedBox3d` no plano XY → retângulo NTS; `UnaryUnionOp.Union` produz `Polygon` com anel externo e anéis internos.

Encapsulação: `PolygonToMeshConverter` e `MeshToPolygonConverter` em `Infrastructure/Lod/Conversion/`. Isolam NTS e LibTessDotNet do resto do código.

### 4.6 Constraints sobre modelos IFC aceitos

A hierarquia espacial completa deve estar presente:

```
IfcSite → IfcBuilding → IfcBuildingStorey → IfcProduct (elementos)
```

- `IfcBuilding` é obrigatório. Modelos contendo apenas Site com elementos são rejeitados.
- `IfcBuildingStorey` é obrigatório. Sem fallback a *clustering* de elevações de bbox.
- Elementos devem estar contidos em pavimento via `IfcRelContainedInSpatialStructure`.
- A geometria deve triangular pelo *pipeline* xBIM. Geometria malformada em elementos individuais é registrada e o elemento é excluído; o pipeline não rejeita o modelo inteiro por uma falha pontual.

A rejeição acontece no `XbimModelLoader.Load()` com exceção tipada (`MissingSpatialHierarchyException`) carregando o caminho do modelo e o tipo da entidade ausente.

---

## 5. Saídas e Workflow

### 5.1 Fluxo de trabalho

```
                          ┌────────────────────────┐
                          │   Arquivo IFC (input)  │
                          │   Site/Building/       │
                          │   Storey/Elements      │
                          └───────────┬────────────┘
                                      ▼
                  ┌───────────────────────────────────────┐
                  │  Stage 0 — XbimModelLoader.Load        │
                  │  validação de hierarquia espacial      │
                  └───────────────────┬───────────────────┘
                                      ▼
                  ┌───────────────────────────────────────┐
                  │  Stage 1 — IEnvelopeDetector.Detect    │
                  │  Voxel ou RayCasting                   │
                  └───────────────────┬───────────────────┘
                                      ▼
                  ┌───────────────────────────────────────┐
                  │  Stage 2 — IFacadeGrouper.Group        │
                  │  DBSCAN sobre esfera de Gauss          │
                  └───────────────────┬───────────────────┘
                                      ▼
                  ┌───────────────────────────────────────┐
                  │  Stage 3 — ILoDComputer.ComputeAll     │
                  │  LoD0 / LoD1 / LoD2 / LoD4 / LoD5      │
                  └───────────────────┬───────────────────┘
                                      ▼
                  ┌───────────────────────────────────────┐
                  │  Stage 4 — IFeatureExtractor.Extract   │
                  │  poço de ventilação / poço de luz /    │
                  │  recesso / átrio / recuo / beiral /    │
                  │  balanço / ...                         │
                  └───────────────────┬───────────────────┘
                                      ▼
       ┌─────────┬────────────┬──────┴──────┬────────────────┐
       ▼         ▼            ▼              ▼                ▼
  ┌─────────┐ ┌────────┐ ┌──────────┐ ┌────────────┐ ┌──────────────┐
  │  JSON   │ │  BCF   │ │ IFC      │ │ GLB / 3D   │ │ CityJSON     │
  │ report  │ │(topics)│ │enriquec. │ │  viewer    │ │ (opcional)   │
  └─────────┘ └────────┘ └──────────┘ └────────────┘ └──────────────┘
       │         │            │              │                │
       ▼         ▼            ▼              ▼                ▼
  análise    revisão      volta ao       figuras de       interop
  downstream visual       BIM auth.      defesa           GIS
```

### 5.2 Cinco formatos de saída

| Saída | Formato | Propósito | Consumidor |
|---|---|---|---|
| **JSON** *(report)* | JSON v3 | análise *machine-readable* | dashboards, scripts, ferramentas downstream |
| **BCF** | BCF 2.1 (zip XML) | revisão visual com *viewpoints* | BIMcollab, Solibri, Revit Issues |
| **IFC enriquecido** | IFC4 + Pset_IEM_* + IfcGroup | caminho de volta ao BIM authoring | Revit, ArchiCAD, BlenderBIM |
| **GLB** *(mesh 3D)* | glTF binary | visualização, defesa | three.js *viewer*, gltf-viewer.donmccurdy.com |
| **CityJSON** *(opcional)* | OGC CityJSON | interoperabilidade GIS | QGIS, ArcGIS, FME |

O modelo IFC original **não é modificado in-place** — o IFC enriquecido é gravado como `*_enriched.ifc` ao lado do original.

### 5.3 IFC enriquecido — caminho de volta ao BIM

Mecanismo principal pelo qual o trabalho realimenta a cadeia BIM. Duas vias, usadas juntas:

**(a) *Property sets* customizados.** Cada `IfcProduct` analisado recebe Psets adicionais via `IfcRelDefinesByProperties`:

```
Pset_IEM_Facade
  ├─ FacadeId          (IfcLabel)       ex.: "facade-03"
  ├─ FacadeAzimuth     (IfcReal)        ex.: 87.5
  ├─ FacadeDirection   (IfcLabel)       Top|Side|Bottom
  ├─ ContributingArea  (IfcAreaMeasure) ex.: 12.4
  └─ DetectionStrategy (IfcLabel)       VoxelFloodFill | RayCasting

Pset_IEM_LoDClassification
  ├─ SurfaceType   (IfcLabel)  WallSurface | RoofSurface | GroundSurface | ...
  ├─ Direction     (IfcLabel)  Top | Side | Bottom
  └─ FacesCount    (IfcInteger)
```

Um usuário do Revit pode abrir o modelo enriquecido, clicar em qualquer parede, e ver o resultado da análise no painel de propriedades — sem *plugin*.

**(b) Grupos de agregação.** Cada *feature* detectada vira um `IfcGroup` com os elementos contribuintes como membros via `IfcRelAssignsToGroup`:

```
IfcGroup "facade-03"
  └─ Members: [IfcWall#123, IfcWindow#456, IfcDoor#789, ...]

IfcGroup "airwell-01"
  └─ Members: [paredes que delimitam o poço]
```

Permite que ferramentas BIM exibam a fachada como um conjunto selecionável, executem *takeoffs*, etc.

xBIM (`Xbim.Ifc`) suporta escrita IFC nativamente. Plugins de autoria BIM (Revit, ArchiCAD, BlenderBIM, Solibri) são direção futura, fora do escopo da defesa.

### 5.4 Schema JSON v3

```json
{
  "schemaVersion": "3",
  "run": {
    "model": "duplex.ifc",
    "strategy": "voxel",
    "grouper": "dbscan",
    "timestamp": "2026-04-28T14:30:00Z",
    "parameters": { "voxelSize": 0.5 }
  },
  "summary": {
    "totalElements": 142,
    "exteriorElements": 38,
    "facadeCount": 4,
    "lodCount": 6,
    "featureCount": { "airwells": 1, "recesses": 0, "atria": 0 },
    "evaluation": {
      "truePositives": null,  "falsePositives": null,
      "falseNegatives": null, "trueNegatives": null,
      "precision": null,      "recall": null
    }
  },
  "classifications": [
    {
      "globalId": "2O2Fr$t4X7Zf8NOew3FLne",
      "ifcType": "IfcWall",
      "computed": { "isExterior": true, "facadeIds": ["facade-01"] },
      "declared": { "isExternal": true },
      "agreement": true
    }
  ],
  "facades": [
    {
      "id": "facade-01",
      "dominantNormal": [0.0, -1.0, 0.0],
      "azimuthDegrees": 180.0,
      "metrics": { "totalArea": 245.6, "wallArea": 198.2, "windowArea": 47.4, "wwr": 0.239 }
    }
  ],
  "lod0": {
    "perStorey": [
      { "storeyId": "...", "outerRing": [...], "holes": [...], "elevation": 0.0 }
    ],
    "wholeBuilding": { "outerRing": [...], "holes": [...] },
    "roofOutline":   { "outerRing": [...], "holes": [...] }
  },
  "lod1": {
    "storeys": [
      { "storeyId": "...", "elevationBottom": 0.0, "elevationTop": 3.0, "volume": 320.5 }
    ],
    "totalVolume": 962.3
  },
  "lod2": {
    "patches": [
      { "id": "roof-01", "dominantNormal": [...], "slopeDeg": 0.0, "elementIds": [...] }
    ],
    "topOutline": { "outerRing": [...], "holes": [...] }
  },
  "lod4": {
    "spaces": [
      {
        "id": "...", "storeyId": "...",
        "class": "Indoor",
        "nSidesEnclosed": 6,
        "volume": 42.5,
        "boundaryElementIds": [...]
      }
    ]
  },
  "lod5": { "voxelGridUri": "voxels.bin", "size": 0.5 },
  "features": {
    "airwells":    [ { "polygon": [...], "verticalSpan": 9.0, "boundaryElementIds": [...] } ],
    "lightShafts": [],
    "recesses":    [],
    "atria":       [],
    "courtyards":  [],
    "lightWells":  []
  },
  "diagnostics": {
    "elementsSkipped": 2,
    "reasons": [ { "globalId": "...", "ifcType": "...", "reason": "empty mesh" } ]
  }
}
```

Quando `--ground-truth` é fornecido, `summary.evaluation` é preenchido com TP/FP/FN/TN + Precisão e Recall (sem F1, sem Kappa). Cada lista de *features* carrega `boundaryElementIds` ou `contributingElementIds` — a rastreabilidade que CityJSON perde é o diferencial direto da nossa saída.

---

## 6. Casos Geométricos Chave

Esta seção documenta os padrões geométricos onde as estratégias divergem. É a base do argumento de defesa: por que *flood-fill* volumétrico é preferível ao *ray casting* em IFC real, e por que uma variante hierárquica é defensável como contribuição.

### 6.1 Poço de luz / poço de ventilação aberto

Cavidade vertical aberta no topo, cercada por paredes em todos os lados horizontais. Frequente em edifícios residenciais e comerciais. Inclui o caso de *shaft* estreito de instalações (duto vertical < 500 mm) que passa por todos os pavimentos.

```
        céu (exterior)
            │
            ▼
    ┌───────────────────────┐       ── telhado ──
    │         │     │       │
    │  sala   │ poço│ sala  │       ↓ flood-fill desce
    │         │     │       │         pelo topo aberto
    │─────────┤░░░░░├───────│
    │         │░░░░░│       │       ── piso ──
    │  sala   │░░░░░│ sala  │         raio horizontal de
    │         │░░░░░│       │         parede interna bate
    └───────────────────────┘         na parede oposta →
                                      classifica como
                                      EXTERIOR (falso positivo)
```

- **Voxel uniforme:** *flood-fill* parte do canto do *grid* expandido, atinge o céu acima do telhado e desce pelo poço. Paredes que dão para o poço são corretamente marcadas como exterior. Em *shaft* estreito (largura < voxel-size), pode falhar — *flood-fill* atravessa o duto. Mitigável reduzindo `voxel-size`, mas custo cresce 8× por refinamento.
- **Ray casting:** raio horizontal a partir da parede interna do poço intercepta a parede oposta do mesmo poço → classifica como interior. Falso negativo.
- **Voxelização hierárquica** *(Hierarchical Voxel Flood-Fill)*: refina adaptativamente apenas na vizinhança da casca do poço/shaft. Resolução fina onde a geometria exige, sem inflar o custo total. **Motivação direta para a contribuição da Fase 6.**

### 6.2 Átrio coberto com *skylight*

Cavidade vertical fechada no topo por vidro (exterior declarado, mas topologicamente selada).

```
    ─── vidro do skylight ───       ↓ flood-fill NÃO desce
     ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓         (skylight = casca sólida
    ┌───────────────────────┐         após rasterização)
    │         │     │       │
    │ quarto  │átrio│ quarto│       paredes do átrio: topo-
    │─────────┤     ├───────│       logicamente isoladas do
    │         │     │       │       exterior → INTERIOR
    │ sala    │     │ cozin │
    └───────────────────────┘
```

- **Voxel *flood-fill*:** correto — *skylight* vedado impede a descida do *flood* exterior, paredes do átrio ficam interior. Concorda com o julgamento AEC de *"fachada = separa interior climatizado do exterior"*.
- **Ray casting:** coincidentemente correto (raio horizontal bate em parede oposta).
- **Diferença em relação ao 6.1:** o *flood-fill* não confunde átrio coberto com poço aberto — respeita a topologia real.

### 6.3 Falhas de malha — `FillGaps`

Modelos IFC reais frequentemente têm malhas com *gaps* de 1 voxel na casca: triângulos que não se encontram perfeitamente nas arestas, erros de *tessellation* do OCCT, etc.

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

- **Voxel *flood-fill* com `FillGaps`:** voxels `Unknown` *sandwiched* entre `Occupied` em eixos opostos são promovidos a `Occupied` iterativamente — fechamento morfológico que sela a casca antes que o *flood* exterior vaze pela fresta.
- **Ray casting:** sem mecanismo análogo — um *gap* na malha gera falsos positivos diretos (raio escapa por uma fresta que não existe no modelo físico).

---

## 7. Decisões Arquiteturais (ADRs)

Formato curto: decisão, motivo, consequência.

### ADR-02 — `IfcRelFillsElement` é ignorado no *loader*

Janelas, portas e paredes são carregadas como `Element`s independentes; a relação "janela preenche *void* na parede" é descoberta via geometria (*bbox overlap*, proximidade), não via metadado. **Motivo:** geometria primeiro, propriedades IFC são *hints*; *loader* simples sem dependência em metadado que pode faltar. **Consequência:** algoritmos inferem essa dica — é justamente o que o TCC se propõe a demonstrar.

### ADR-03 — Semântica de agregadores é fixa, sem *flag* CLI

Uma só semântica de tratamento de agregadores (ver ADR-11) para todo o projeto; não existe `--aggregate-mode`. **Motivo:** menos superfície de *bugs*, testes mais previsíveis, documentação mais simples. **Consequência:** caso real que exija outro tratamento volta ao plano antes de virar código.

### ADR-04 — `Face` = `Element` + `TriangleIds` + `Plane3d`

`Face` referencia `Element` diretamente, carrega índices de triângulos no *mesh* do elemento (sem duplicar geometria) e um `Plane3d` ajustado por PCA. **Motivo:** rastreabilidade forte sem *lookup* externo; `Plane3d` centraliza `Normal`, `PointOnPlane`, `Distance(p)`, `Project(p)`. **Consequência:** acoplamento `Face → Element` é unidirecional, ambos em Domain; em serialização JSON, `[JsonIgnore]` em `Face.Element` evita ciclos.

### ADR-05 — `ElementFilter` em Infrastructure + *default* inclusivo + *override* CLI

Filtro de tipos IFC vive em `Infrastructure/Ifc/Loading/ElementFilter.cs`; `XbimModelLoader` recebe `ElementFilter` por construtor; CLI aceita `--include-types` e `--exclude-types`. **Motivo:** *"o filtro deve ser facilmente alterado no futuro, até pelo usuário"* — construtor configurável permite DI em testes; CLI permite *override* sem recompilar. **Consequência:** *default* é opinativo (inclui `IfcRailing`, exclui `IfcFooting`, etc.) e questionável em PR.

### ADR-06 — `BcfWriter` na CLI + Viewer em paralelo

`BcfWriter` produz BCF a partir do JSON (caminho automatizado, reproduzível em CI); o *Viewer* também produz BCF (após edição manual). Ambos consomem o mesmo JSON. **Motivo:** *pipeline* + JSON é o caminho automatizado, *Viewer* é o caminho assistido — usos distintos. **Consequência:** duas implementações de BCF; compartilhar código via biblioteca BCF comum quando possível.

### ADR-07 — Viewer MVP *default*; completo como *stretch goal*

Entregável obrigatório é o **MVP**: render 3D colorido por fachada, inspeção por elemento, filtro exterior/interior. Edição manual e *export* BCF são *stretch goals* condicionais a *stage gates*. **Motivo:** *Viewer* completo é o item de maior risco de cronograma e não é a questão de pesquisa; MVP já satisfaz o critério #4 do TCC. **Consequência:** se Precisão/Recall forem insuficientes ou o cronograma apertar, *Viewer* permanece em escopo MVP; absorção pelo `tools/debug-viewer/` (ADR-17) é decidida na Fase 7.

### ADR-08 — *Capability interfaces* sem base abstrata; identidade por `GlobalId`

Domínio organizado por interfaces ortogonais — `IIfcEntity`, `IBoxEntity`, `IMeshEntity`, `IElement` — sem classe abstrata comum; cada *concrete* implementa apenas o que faz sentido + `IEquatable<T>` próprio. **Motivo:** hierarquia rígida atritava entre `Storey` (sem *mesh*) e `Element` (com *mesh*); trocar duplicação por hierarquia plana mantém cada *concrete* autocontido e permite *dispatch* polimórfico onde importa. **Consequência:** cada *concrete* carrega ~5 linhas de `Equals`/`GetHashCode`; identidade segue por `GlobalId`.

### ADR-09 — Agregação IFC tem 2 níveis fixos

IFC real mantém `IfcRelAggregates` para *building elements* em exatamente 2 níveis (agregador → átomos); `Debug.Assert` no *loader* captura violação. **Motivo:** agregadores comuns (`IfcCurtainWall`, `IfcStair`, `IfcRamp`, `IfcRoof`) têm filhos construtivos diretos — ninguém aninha `IfcStair` dentro de `IfcStair`. **Consequência:** *loader* simples, sem `LeavesDeep`; violação produz *log warning* em Release.

### ADR-10 — Acesso direto via `IfcProductContext`

`Element._ctx.Product` é o caminho primário para qualquer *metadata* IFC — retorno O(1). `IProductEntity` em `Infrastructure/Ifc/` expõe a navegação xBIM sem poluir `Domain`. **Motivo:** `Domain` permanece sem referência a xBIM; acesso via *bundle* imutável elimina *lookup* e indexação. **Consequência:** propriedades IFC são *hints* — algoritmos de `Domain` não tocam xBIM; *metadata* é lida via `_ctx.Product` nos consumidores de `Infrastructure`.

### ADR-11 — Modelo único: `Element` átomo ou *composite* via `Children`

Não há classe `ElementGroup`. *Composites* IFC (`IfcCurtainWall`, `IfcRoof`, etc.) são `Element` *instances* com `Children` populado; átomos têm `Children = []`. O *composite* tem seu próprio `_lazyMesh` que mescla os *meshes* filhos via `DMesh3Extensions.Merge` no primeiro acesso. **Motivo:** forma única elimina duplicação de tipo — algoritmos iteram `model.Elements` tratando ambos pela mesma interface; quem precisa dos painéis lê `element.Children`. **Consequência:** filho sem geometria é descartado pelo *loader*; *composite* materializado carrega triângulos duplicados — aceitável para 10–100 *composites* por modelo.

### ADR-13 — Stack matemática reutilizada de `geometry4Sharp`

Toda matemática de detecção (*plane-fit* PCA, *eigen*, SAT triângulo-AABB, BVH 3D, esfera de Gauss) usa classes do `geometry4Sharp`; `NetTopologySuite` entra apenas para 2D (LoD0); sem `MathNet`. **Motivo:** ferramentas de referência (van der Vaart, Voxelization Toolkit) escreveram voxel/*flood-fill* mas delegaram *math* a Eigen/OCCT — replicamos o padrão; *linear scan* + AABB pré-filtro substitui *R-tree* 3D para n ≤ 10⁴. **Consequência:** mapeamento direto em §4.1; se *profiling* futuro apontar gargalo de indexação 3D, *octree custom* (~150 linhas) resolve sem dependência externa.

### ADR-14 — Voxel uniforme primária + RayCasting *baseline*; Normais descartada

Estratégia de produção é `VoxelFloodFillDetector` (van der Vaart 2022 + cascata 4-testes, 3 fases, `FillGaps`); `RayCastingDetector` (Ying 2022) permanece como *baseline* no capítulo de Resultados; `NormalsStrategy` descartada. **Motivo:** voxel é robusto por design em IFC real — *malformed meshes* são norma; *baseline* trivial (Normais) prova contribuição zero, RayCasting caracteriza *tradeoff* substantivo. **Consequência:** Fase 6 introduz a voxelização hierárquica como contribuição original; comparação no capítulo de Resultados é *3-way* (uniforme vs. hierárquico vs. *ray casting*).

### ADR-15 — Adoção do *framework* LoD (Biljecki / van der Vaart)

Adotar Biljecki, Ledoux & Stoter (2016), estendido por van der Vaart, Arroyo Ohori & Stoter (2025), como arquitetura progressiva de saídas; camadas via `ILoDComputer` em `Infrastructure/Lod/` (Stage 3). LoD0 via projeção XY de *bbox* (preserva forma L/U; mantém rastreabilidade); LoD5 é o `VoxelGrid3D` da detecção, exposto como artefato. **Motivo:** *"pipeline IFC-nativo multi-LoD que preserva identidade de elemento em todos os níveis"* é lacuna real — van der Vaart 2025 perde *provenance* abaixo do LoD3 nos *shells* CityJSON; múltiplos LoDs atendem múltiplos casos de uso (GIS LoD0–1, modelagem urbana LoD2, BIM LoD3–4). **Consequência:** `ILoDComputer` + `LoD{0,1,2,4,5}Computer` + `IFeatureExtractor` (Stage 4); taxonomia CityGML 3.0 introduzida organicamente (Fase 5); *Recess* com abertura é definição metodológica original sem equivalente *peer-reviewed*; CLI ganha `--lod <lista>`; schema JSON v3 substitui v2 com blocos LoD.

### ADR-17 — Debug geométrico via `[Conditional("DEBUG")]` + *viewer* HTTP em processo separado

Classe estática `GeometryDebug` em `Infrastructure/Visualization/Api/`; cada método público marcado `[Conditional("DEBUG")]` — em Release, todas as chamadas são eliminadas pelo compilador no *call site*. Em Debug, cada método acumula *shapes* via `Scene` e serializa GLB via *atomic write*. *Viewer* HTTP roda em processo OS separado (`DebugServer` EXE *standalone*) para sobreviver ao *freeze* do *debugger* .NET. **Motivo:** `IDebugSink` (versão anterior) adicionava DI em construtores e *null-sink* em produção — complexidade desnecessária; `[Conditional("DEBUG")]` é o padrão idiomático do C# para instrumentação. **Consequência:** *detectors* chamam `GeometryDebug.Send(...)` direto; zero `#if DEBUG` no código do algoritmo; convenção de cor: TP=verde, TN=cinza, FP=vermelho, FN=laranja; absorção do Viewer Blazor pelo `tools/debug-viewer/` é decidida na Fase 7.

---

## 8. Plano de Fases

### Fase 0 — Spike: carregamento e triangulação ✅ (17/abr/2026 · 1 dia)

**Meta.** Parsear um arquivo IFC real com xBIM e extrair geometria.
**Entrega.** `XbimModelLoader.Load()` v0 carrega `duplex.ifc` (157 elementos) e produz `Element` com `DMesh3` não-vazia. `Xbim3DModelContext.MaxThreads = 1` (*workaround* OCCT). Solução `.slnx`, pacotes NuGet básicos, CLI mínimo.

### Fase 1 — Modelo refinado + testes-base + CI + Debug scaffold ✅ (17 → 19/abr/2026 · 3 dias)

**Meta.** Estabelecer infraestrutura de testes + debug geométrico antes de qualquer algoritmo novo.
**Entrega.** *Loader* retorna `ModelLoadResult(Elements, Composites)` com filtro injetado e *error handling* tipado. Domínio Core completo: `Element`, `Face`/`Envelope`/`Facade`, `DetectionResult`/`ElementClassification`. `XbimIfcProductResolver`; `GeometryDebug` *scaffold* com 10 métodos. 34 testes unitários no CI + 2 integração local; CI GitHub Actions configurado.

### Fase 2 — Validação da detecção + debug visual ✅ (19 → 24/abr/2026 · 5 dias)

**Meta.** Pipeline de detecção validado quantitativamente e inspecionável visualmente.
**Referência canônica.** van der Vaart (2022) — IFC_BuildingEnvExtractor.
**Entrega.** `VoxelFloodFillDetector` (3 fases + `FillGaps`) + `PcaFaceExtractor` (`OrthogonalPlaneFit3`); SAT triângulo-AABB próprio (Akenine-Möller 1997). Validação quantitativa: TP/FP/FN/TN + Precisão/Recall via `EvaluationService` em `duplex.ifc`. *Debug-viewer* modular HTML+three.js em processo OS separado.

### Fase 3 — RayCasting baseline + JSON + BCF ✅ (25 → 26/abr/2026 · 2 dias)

**Meta.** Comparação Voxel vs. RayCasting tabelada; saída JSON e BCF mínimo operacionais.
**Entrega.** `RayCastingDetector` (Ying 2022) — BVH global via `g4.DMeshAABBTree3` + mapa de *ownership* por triângulo para *auto-hits*. *Ablation* em `duplex.ifc` (Voxel P=0.849/R=0.918 vs. RayCasting P=0.568/R=0.939) confirma *tradeoff* da literatura. `JsonReportWriter` (schema v1) + `BcfWriter` (BCF 2.1); CLI `--strategy` + `--output`. 168/168 testes verdes.

### Fase 4 — Refatoração de Domínio + API de Visualização + DBSCAN + Clean Architecture ✅ (27 → 28/abr/2026 · 2 dias)

**Meta.** Infraestrutura de domínio madura, API de debug visual ergonômica, agrupamento em fachadas pronto, Clean Architecture estabelecida.
**Entrega.** `IElement` agrega `IIfcEntity + IBoxEntity + IMeshEntity`; `Element` segura `IIfcProduct` direto com *lazy mesh*/*bbox*; *composites* como `Element` com `Children[]`. Renomeações no domínio: `IDetectionStrategy` → `IEnvelopeDetector`; `VoxelFloodFillStrategy` → `VoxelFloodFillDetector`; `RayCastingStrategy` → `RayCastingDetector`; `ReportBuilder` → `JsonReportBuilder`; `EvaluationPipeline` → `EvaluationService`. API de visualização: `Color` *value type* com cores nomeadas + `Color.FromHex`; `GeometryDebug.Send(IElement, Color)`; `GeometryDebug.Enabled` *runtime flag*; `IfcTestBase` centraliza carga + cache de `ModelLoadResult`. `DbscanFacadeGrouper : IFacadeGrouper` (DBSCAN sobre esfera de Gauss + QuikGraph para componentes conectados) — em `duplex.ifc`: 4 *clusters* de orientação (N/E/S/W) + 13 *noise faces* descartadas; schema JSON v2 com blocos `facades` + `aggregates`. Migração para Clean Architecture: separação rigorosa em quatro projetos (`Domain` / `Application` / `Infrastructure` / `Cli`); `Domain` sem qualquer dependência xBIM. 124 testes verdes (66 Domain sem xBIM + 57 Infrastructure + 1 Integration), cobertura XML doc completa nas APIs públicas.

### Fase 5 — Arquitetura Multi-LoD (29/abr → 07/jul/2026 · 10 semanas)

**Meta agregada.** Introduzir o *framework* LoD de Biljecki & van der Vaart como camada arquitetônica do *pipeline* (Stage 3 + Stage 4), preservando rastreabilidade ao `IElement` em todos os níveis de saída — diferencial direto em relação a van der Vaart 2025, que perde *provenance* abaixo do LoD3.

**Referências canônicas.** Biljecki, Ledoux & Stoter (2016); van der Vaart, Arroyo Ohori & Stoter (2025); Kutzner, Chaturvedi & Kolbe (2020); Donkers et al. (2015); Chun, Kwok & Tamura (2004); Spagnolo & de Dear (2003); Rojas-Fernández et al. (2017).

**Bibliotecas novas.** `NetTopologySuite` (engine único de polígono 2D), `LibTessDotNet` (triangulação polígono-com-furos para conversão NTS → `g4.DMesh3`).

**Critério de sucesso.** Todos os LoDs (0/1/2/4/5) selecionáveis na CLI; LoD3 enriquecido com `Direction` e tipos de superfície CityGML; *features* topológicas detectadas como *queries*; saídas em JSON v3, BCF estendido, IFC enriquecido (Pset_IEM_* + IfcGroup) e GLB com *overlays* 2D.

#### P5.1 — LoD0 + Poço de ventilação + Recesso (4 semanas)

Sub-fase de abertura: contornos 2D e *features* topológicas básicas.

- [ ] `Direction` enum no `Face` (Topo/Lateral/Base — limiar 70°)
- [ ] `Polygon2D`, `Outline`, `Footprint` (Polygon2D + Direção + Elevação)
- [ ] `LoD0Computer` — projeção *bbox* → `UnaryUnionOp` → anel externo + furos; por pavimento + *whole-building*
- [ ] `AirwellDetector` — furos em LoD0 ∩ LoD2 (LoD2 reconciliado em P5.2)
- [ ] `RecessDetector` — concavidade no anel exterior com pelo menos uma abertura
- [ ] Conversores `MeshToPolygonConverter` e `PolygonToMeshConverter` (LibTessDotNet)
- [ ] `GeometryDebug.Send(Polygon, double elevation, Color)` para *overlay* 2D
- [ ] Schema JSON v2 → v3 com blocos `lod0` + `features.airwells` + `features.recesses`
- [ ] Testes nos 7 modelos `data/models/airwell/*.ifc`

#### P5.2 — LoD1 + LoD2 + tipos de superfície CityGML (3 semanas)

Sub-fase de extrusão e telhado: `BlockModel`, `RoofShell`, primeiros tipos CityGML.

- [ ] `BlockModel` (`StoreyBlock` por pavimento — *quad-strip extrusion*)
- [ ] `LoD1Computer` (orquestrador)
- [ ] *Refator*: `DbscanFacadeGrouper` → `OrientedSurfaceDbscan` aceita filtro de `Direction`; `IFacadeGrouper.Group` segue *wrapper* de `Direction.Lateral`
- [ ] `RoofPatch`, `RoofShell` (*top outline* + *patches* por orientação)
- [ ] `LoD2Computer` (DBSCAN com `Direction.Topo`)
- [ ] Tipos CityGML 3.0: `WallSurface`, `RoofSurface`, `GroundSurface`
- [ ] Reconciliação de furos: `LoD0.Holes ∩ LoD2.TopOutline` → poço de ventilação vs. poço de luz
- [ ] `OverhangDetector`, `SetbackDetector`, `CantileverDetector`, `EaveDetector`, `ParapetDetector`
- [ ] Schema JSON v3 estendido com `lod1`, `lod2`, *features* relacionadas
- [ ] **Saída IFC enriquecido**: *writer* de `Pset_IEM_Facade` + `IfcGroup` por *feature* usando `Xbim.Ifc`

#### P5.3 — LoD4 + LoD5 + classificação de espaços (3 semanas)

Sub-fase final: espaços interiores, *voxel grid* exposto, métricas N-Sides, mais tipos CityGML.

- [ ] `Space` (volume *mesh* + pavimento + classe), `EnclosureProfile` (Topo/Lateral/Base materialidade), `SpaceClass` enum (Interno/Semi-interno/Semi-externo/Externo)
- [ ] `Building` (*aggregate root*: `Storeys[]` + `Outlines` + `BuildingFeatures`)
- [ ] `LoD4Computer` — materializa *rooms* a partir de `VoxelGrid3D.GrowInterior()`; classifica via N-Sides metric
- [ ] `LoD5Computer` — expõe `VoxelGrid3D` como artefato
- [ ] Tipos CityGML adicionais: `OuterCeilingSurface` (forro externo), `OuterFloorSurface` (terraço)
- [ ] `AtriumDetector`, `CourtyardDetector`, `LightWellDetector`, `LoggiaDetector`, `SoffitDetector`, `BalconyDetector`, `TerraceDetector`
- [ ] Schema JSON v3 estendido com `lod4`, `lod5`, *features* relacionadas
- [ ] CityJSON *writer* (opcional / *stretch*) — interoperabilidade GIS

Materialidade refinada V/P/B + `ClosureSurface` é tratada após a Fase 6 quando houver folga de cronograma — não é bloqueante para a defesa.

### Fase 6 — Voxelização Hierárquica com Flood-Fill (08/jul → 01/set/2026 · 8 semanas)

**Meta.** Implementar a estratégia de voxelização hierárquica (contribuição original) e comparar com as duas baselines (uniforme, *ray casting*) em precisão, *recall* e tempo.
**Referência canônica.** van der Vaart (2022) para o *flood-fill* 3-fases; contribuição metodológica original para a hierarquia adaptativa e os critérios de refinamento.
**Critério de sucesso.** (a) 3 estratégias rodam sobre os mesmos *fixtures*; (b) tabela com TP/FP/FN/TN + Precisão/Recall + tempo por estratégia × modelo; (c) análise por caso geométrico mostra onde cada estratégia falha ou acerta; (d) seção de Resultados da dissertação inclui a comparação 3-vias como figura principal.

#### P6.1 — Estrutura hierárquica de voxels (3 semanas)

- [ ] `HierarchicalVoxelGrid` — *octree* com níveis L0 → L1 → ... → Lmax; célula-folha carrega o mesmo estado e lista de ocupantes por `GlobalId`
- [ ] Critério de refinamento: célula `Li` refina para `Li+1` se `Occupied` **e** vizinhança contém mistura de estados
- [ ] Testes unitários da estrutura de dados

#### P6.2 — `HierarchicalVoxelFloodFillDetector` (3 semanas)

- [ ] `HierarchicalVoxelFloodFillDetector : IEnvelopeDetector` — mesmo contrato (`DetectionResult`)
- [ ] Rasterização multi-nível: SAT triângulo-caixa reaproveita extensions da Fase 2
- [ ] *Flood-fill* atravessando níveis (propagação exterior desce nas folhas refinadas e sobe nas células grossas)
- [ ] Instrumentação `GeometryDebug` por nível + por fase (ADR-17)
- [ ] Determinismo: ordenação estável por `GlobalId`

#### P6.3 — Comparação 3-vias + escrita dos Resultados (2 semanas)

- [ ] `EvaluationService` ampliado: 3 estratégias sobre o mesmo `ModelLoadResult`, tabela comparada
- [ ] Cobertura dos casos geométricos chave (poço de luz, *shaft*, átrio coberto)
- [ ] Escrita da seção de Resultados: tabela 3-vias + discussão por caso + ameaças à validade
- [ ] Figura principal da contribuição

Se a variante hierárquica não superar a uniforme em nenhuma métrica, a dissertação relata o resultado negativo e reforça o *flood-fill* uniforme como estado-da-arte prático — ainda é contribuição publicável (ablação rigorosa).

### Fase 7 — Viewer MVP ou absorção pelo debug-viewer (02/set → 13/out/2026 · 6 semanas)

**Meta.** Decisão sobre Viewer (ADR-07 × ADR-17) + implementação.
**Critério de sucesso.** Especialista AEC consegue abrir artefatos e navegar resultados.

Avaliar o estado do `tools/debug-viewer/`:
- Se UX estiver amigável a especialistas → absorver o papel do Viewer pelo *debug-viewer*; descartar o projeto Blazor; energia concentra em polimento.
- Se *debug-viewer* for adequado só para desenvolvimento → **Viewer MVP Blazor** segue: render 3D por elemento colorido por fachada (consome LoD3); filtro exterior/interior; inspeção (`GlobalId`, `IfcType`, `IIfcProductResolver`); *overlay* opcional de *ground truth* CSV.

Decisão documentada em ADR novo na data.

### Fase 8 — Ground Truth e Avaliação Experimental (paralela, mai – nov/2026)

**Meta.** Validar o método contra rótulos manuais de especialistas.
**Critério de sucesso.** Tabela com TP/FP/FN/TN + Precisão/Recall por modelo e por tipologia; ≥75% de concordância simples (*percent agreement*) entre especialistas.

- [ ] Selecionar 3–5 modelos IFC de tipologias diferentes (planta retangular, L, curva/irregular)
- [ ] Protocolo de rotulação (critérios, ferramenta — provavelmente Viewer MVP, resolução de divergências)
- [ ] Recrutar 5+ profissionais AEC
- [ ] *Percent agreement* entre especialistas (contagem direta de rótulos concordantes / total)
- [ ] Tabela de resultados para a dissertação

### Fase 9 — Entrega (mar – abr/2027)

**Meta.** Finalizar documentação, testes de usabilidade e publicação.
**Critério de sucesso.** Defesa da Etapa 4 em abr/2027; repositório público e reproduzível.

- [ ] Testes de usabilidade do *viewer* com ≥3 especialistas AEC
- [ ] README final (instalação, uso, exemplos)
- [ ] Publicação no GitHub como repositório público
- [ ] Artefatos da dissertação: tabelas de resultado, figuras, *links* para reprodução

---

## 9. Critérios de Sucesso do TCC

A ferramenta é bem-sucedida academicamente quando:

1. **O método funciona de ponta a ponta** em modelos IFC reais de diferentes tipologias.
2. **Resultados são mensuráveis**: Precisão e Recall calculados contra *ground truth* triangulado em três camadas — comparação automatizada com IFC_BuildingEnvExtractor (C1) + auto-rotulação documentada do pesquisador (C2) + revisão visual por profissionais AEC via *survey* (C3). Ver §3.6.
3. **Rastreabilidade preservada em todos os LoDs**: cada face, fachada, contorno, espaço e *feature* detectada é rastreável ao `IElement` de origem — diferencial direto contra van der Vaart (2025), que perde *provenance* abaixo do LoD3.
4. **Aplicabilidade demonstrada**: WWR por fachada calculado a partir dos resultados; *features* topológicas (poço de ventilação, recesso, átrio, balanço) computadas a partir das camadas LoD.
5. **Saída de volta ao BIM**: IFC enriquecido com `Pset_IEM_*` + `IfcGroup` lê-se nativamente em Revit, ArchiCAD, BlenderBIM — fechando o ciclo BIM → análise → BIM.
6. **O resultado é reproduzível**: qualquer pessoa com .NET 8 pode rodar `dotnet run` e obter os mesmos números.
7. **Extensibilidade arquitetural demonstrada**: a *Clean Architecture* com portas e adaptadores permite ao menos uma extensão concreta do *pipeline* — por exemplo, substituição da estratégia de detecção sem modificação dos demais estágios — alinhando o trabalho à natureza do MBA em Engenharia de Software.

---

## Apêndice A — Otimizações Futuras

Backlog de melhorias **fora do escopo** da defesa de abr/2027.

- **Paralelismo na rasterização.** Cada triângulo marca voxels independentes; um `Parallel.For` sobre a lista de triângulos dá *speedup* quase linear em modelos grandes. Exige ordenação final estável.
- **SIMD no teste SAT triângulo-caixa.** A cascata de 13 eixos do Akenine-Möller (1997) é vetorizável via `System.Numerics.Vector<T>`. Benefício esperado: ~3× no *hot loop*.
- **Ordenação Morton dos voxels.** Indexar voxels por curva de Morton (Z-order) melhora a localidade de cache. Útil apenas se *profiling* apontar *cache miss* dominante.
- **Voxelização em GPU.** *Shader* de rasterização que escreve diretamente no *grid* de voxels 3D. Aceito apenas se modelos maiores (>10⁵ elementos) forem priorizados.
- **Cache persistente de `ShapeGeometry`.** Re-executar o pipeline no mesmo IFC re-triangula tudo via xBIM. Cache em disco por `(file-hash, EntityLabel)` cortaria tempo do *iteration loop* em testes de regressão.
- **Materialidade V/P/B refinada + `ClosureSurface`.** Subdivisão por região de cada face (parede-com-janela = parede Físico + abertura Virtual). Adiciona profundidade ao modelo, não novidade.
- **Plugins de autoria BIM.** Revit (.NET, mais natural), ArchiCAD, BlenderBIM (via subprocesso/REST), Solibri (regras de *code-checking* contra HKBD APP-130). Pós-defesa.

Cada item entra como *issue* do repositório apenas se o *profiling* pós-Fase 6 apontar o gargalo real.

---

## Apêndice B — Modelos IFC Disponíveis

Modelos atuais em `data/models/`:
- 5 arquivos *fixture* do *voxelization_toolkit*: `duplex`, `duplex_wall`, `schependom_foundation`, `demo2`, `covering`
- 7 modelos `airwell/*.ifc` *purpose-built* (control, big-recess, small-recess, round, with-one-balcony, t-shape, rounded-corners)
- `IfcOpenHouse_IFC4.ifc` e variantes auxiliares

A seleção final dos modelos para os experimentos (5–8 cobrindo tipologias diversas + ≥1 átrio coberto + ≥1 pátio aberto + ≥1 *pilotis* se possível) será feita ao longo da Fase 5. Catálogo completo, critérios de seleção e tabela de modelos selecionados em `00_Manuais_e_Referencias/datasets-ifc.md` (a preencher).
