# Plano de Implementação — IfcEnvelopeMapper

> Documento vivo. Atualizar a cada sessão de desenvolvimento.
> Última atualização: 2026-04-10

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
| **geometry3Sharp** | `geometry3Sharp` | Mesh 3D (`DMesh3`), ray casting, BVH, normais de face | Core + Geometry |
| **NetTopologySuite** | `NetTopologySuite` | Geometria 2D, operações de containment e projeção em plano | Geometry |
| **DBSCAN** | `DBSCAN` (NuGet) | Clustering de normais sobre a esfera de Gauss | Algorithms |
| **QuikGraph** | `QuikGraph` | Grafo de adjacência espacial, componentes conectados | Algorithms |
| **System.CommandLine** | `System.CommandLine` | Parser de argumentos CLI | Cli |

**Política de bibliotecas:** usar bibliotecas externas agora e substituir por implementação própria somente se uma biblioteca não for extensivamente utilizada no projeto. Não prematuramente otimizar.

---

## Modelo de Domínio

### Hierarquia conceitual

```
Envelope (totalidade das faces exteriores com rastreabilidade)
    └── input para →
        Facade[] (região de superfície por plano dominante)
            └── Face[] (superfície atômica exterior — unidade primária)
                └── BuildingElement (rastreável ao IFC)

Relação Facade ↔ BuildingElement: MUITOS-PARA-MUITOS
  - Uma Face pertence a exatamente 1 BuildingElement e 1 Facade
  - Um BuildingElement pode ter Faces em 0, 1 ou N Facades
  - Uma Facade agrega Faces de M BuildingElements diferentes
```

> **Envelope não contém Facade[]** — é input para o `IFacadeGrouper`, que produz `Facade[]`.
> **Facade referencia Envelope** (parent) e contém um subconjunto de `Face[]`.
> **Facade.Elements** retorna os elementos que possuem ≥1 Face nesta região.

### BuildingElement — sealed class

```csharp
/// Elemento IFC com sua geometria triangulada.
/// Sem IIfcProduct — Core não depende de xBIM.
/// Sem ObjectType — algoritmos não dependem de metadados IFC.
public sealed class BuildingElement
{
    public string GlobalId { get; }
    public string IfcType { get; }              // "IfcWall", "IfcWindow"...
    public DMesh3 Mesh { get; }
    public AxisAlignedBox3d BoundingBox { get; } // computado no ctor (eager, O(n vértices))
    public Vector3d Centroid => BoundingBox.Center;

    public BuildingElement(string globalId, string ifcType, DMesh3 mesh)
    {
        GlobalId = globalId;
        IfcType = ifcType;
        Mesh = mesh;
        BoundingBox = new AxisAlignedBox3d(mesh.GetBounds());
    }
}
```

**Por que `sealed class` e não `record`?** `DMesh3` não implementa value equality — `record` geraria comparação por referência disfarçada de comparação por valor.

**Por que sem `IIfcProduct`?** Acoplaria Core ao xBIM. O `XbimModelLoader` (em Ifc) extrai os dados necessários e constrói `BuildingElement` com tipos primitivos.

### Face — superfície atômica exterior

```csharp
/// Conjunto de triângulos de um BuildingElement que pertencem a um mesmo plano.
/// Inferida geometricamente — não existe no IFC.
/// Referência direta a BuildingElement para rastreabilidade.
public sealed class Face
{
    public BuildingElement Element { get; }
    public IReadOnlyList<int> TriangleIds { get; }  // índices na DMesh3 do elemento
    public Plane3d FittedPlane { get; }
    public Vector3d Normal => FittedPlane.Normal;
    public double Area { get; }
    public Vector3d Centroid { get; }
}
```

### Envelope — casca + faces exteriores

```csharp
/// Resultado do Stage 1: casca geométrica + faces exteriores com rastreabilidade.
/// É input para o IFacadeGrouper — não contém Facade[].
public sealed class Envelope
{
    public DMesh3 Shell { get; }                    // casca geométrica (malha fundida)
    public IReadOnlyList<Face> Faces { get; }       // faces exteriores com rastreabilidade
    public IEnumerable<BuildingElement> Elements
        => Faces.Select(f => f.Element).DistinctBy(e => e.GlobalId);
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
        => Faces.Select(f => f.Element).DistinctBy(e => e.GlobalId);
}
```

### Interfaces do pipeline

```csharp
// Port de carregamento — implementado em Ifc, definido em Core (DIP)
public interface IModelLoader
{
    IReadOnlyList<BuildingElement> Load(string path);
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

## Estrutura do Projeto (5 projetos)

```
IfcEnvelopeMapper/
├── docs/
│   └── plano.md                          ← este arquivo
│
├── src/
│   ├── IfcEnvelopeMapper.Core/           ← domínio puro + interfaces (ports)
│   │   ├── Building/
│   │   │   └── BuildingElement.cs
│   │   ├── Envelope/
│   │   │   ├── Envelope.cs
│   │   │   ├── Facade.cs
│   │   │   └── Face.cs
│   │   ├── Pipeline/
│   │   │   ├── IModelLoader.cs
│   │   │   ├── IDetectionStrategy.cs
│   │   │   └── IFacadeGrouper.cs
│   │   └── Reporting/
│   │       ├── DetectionResult.cs
│   │       └── ElementClassification.cs
│   │   [deps: geometry3Sharp]
│   │
│   ├── IfcEnvelopeMapper.Geometry/       ← operações geométricas stateless
│   │   └── GeometricOps.cs
│   │   [deps: Core, geometry3Sharp, NetTopologySuite]
│   │
│   ├── IfcEnvelopeMapper.Ifc/            ← integração xBIM
│   │   └── XbimModelLoader.cs            ← implementa IModelLoader
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
│   └── IfcEnvelopeMapper.Cli/            ← entry point, output writers
│       ├── Commands/
│       │   └── DetectCommand.cs          ← orquestra o pipeline
│       ├── Output/
│       │   ├── JsonReportWriter.cs
│       │   └── BcfWriter.cs
│       └── Program.cs
│       [deps: Core, Ifc, Algorithms, System.CommandLine]
│
├── tests/
│   └── IfcEnvelopeMapper.Tests/          ← todos os testes (xUnit)
│
├── data/
│   ├── models/                           ← arquivos IFC para testes
│   ├── results/                          ← outputs JSON gerados pela CLI
│   └── ground-truth/                     ← rotulação manual por especialistas (CSV)
│
└── IfcEnvelopeMapper.sln
```

### Por que 5 projetos?

`Core` concentra o domínio e as interfaces sem depender de infraestrutura (exceto `geometry3Sharp` para tipos geométricos). `Geometry` isola operações geométricas puras, reutilizáveis entre strategies. `Ifc` encapsula toda a complexidade do xBIM e pode ser substituído por outra biblioteca de leitura IFC sem tocar o domínio. `Algorithms` contém as strategies e o agrupamento — a parte mais experimental do projeto. `Cli` é o único ponto de entrada e o único lugar que conhece writers de output.

### Dependency Inversion

**`IModelLoader` fica em Core, não em Ifc.** A interface pertence ao consumidor, não ao provedor. `XbimModelLoader` implementa `IModelLoader` e fica em Ifc; Core não sabe que xBIM existe.

**`IFacadeGrouper` fica em Core, não em Algorithms.** `DbscanFacadeGrouper` (e futuros groupers) implementam a interface e ficam em Algorithms.

**Sem `IReportWriter` em Core.** A CLI produz um `DetectionResult` e chama writers concretos. Nenhuma abstração é necessária neste ponto.

### Diagrama de dependências (sem circular)

```
Core ← Geometry ← Algorithms ← Cli
Core ← Ifc               ↗
Core ←────────────────────
```

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
var elements = loader.Load(modelPath);                     // IModelLoader
var result   = strategy.Detect(elements);                  // IDetectionStrategy → DetectionResult
var facades  = grouper.Group(result.Envelope);             // IFacadeGrouper → Facade[]
var report   = ReportBuilder.Build(result, facades, runMeta);
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
FUNÇÃO Load(ifcPath) → BuildingElement[]
    // Ref: xBIM Toolkit — Xbim3DModelContext (Lockley et al.)
    model ← XbimModel.Open(ifcPath)
    context ← Xbim3DModelContext(model)
    context.CreateContext()

    elementos ← []
    PARA CADA product EM model.Instances.OfType<IIfcProduct>():
        SE product NÃO É tipo construtivo relevante:
            CONTINUE    // Filtra: IfcWall, IfcWindow, IfcDoor, IfcCurtainWall,
                        //         IfcSlab, IfcRoof, IfcColumn, IfcBeam, IfcRailing

        shapeInstances ← context.ShapeInstancesOf(product)
        mesh ← TriangularMesh vazia
        PARA CADA shape EM shapeInstances:
            geometria ← context.ShapeGeometry(shape)
            mesh.Append(geometria.Triangles, shape.Transformation)

        SE mesh tem triângulos:
            elementos.Add(BuildingElement(
                globalId: product.GlobalId,
                ifcType: product.GetType().Name,
                mesh: mesh
            ))

    RETORNAR elementos
```

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
    // Ref: geometry3Sharp — DMeshAABBTree3 (BVH para ray-triangle intersection)

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
  ]
}
```

Quando `--ground-truth` é fornecido, `precision`, `recall` e `f1` são preenchidos automaticamente.

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

### Fase 0 — Spike: validar que o pipeline básico funciona
**Meta:** parsear um arquivo IFC real com xBIM e extrair geometria de um elemento.
**Critério de sucesso:** logar no console os triângulos e normais de pelo menos um `IfcWall` do `duplex.ifc`.

- [ ] Criar solução `.sln` com os 5 projetos e estrutura de pastas
- [ ] Adicionar pacotes NuGet (ver tabela de bibliotecas)
- [ ] Escrever `Program.cs` mínimo: abrir IFC, iterar elementos, logar tipos
- [ ] Implementar `XbimModelLoader.Load()` — extrair triangulated mesh via `Xbim3DModelContext`
- [ ] Confirmar que normais de face são acessíveis e fazem sentido geométrico

---

### Fase 1 — Estratégia primária + pipeline ponta a ponta
**Meta:** implementar a primeira estratégia de detecção, agrupamento em fachadas e relatório JSON.
**Critério de sucesso:** classificar todos os elementos do `duplex.ifc`; inspecionar manualmente se os resultados fazem sentido visual.

**Detecção (Stage 1):**
- [ ] Implementar `GeometricOps` (FaceNormals, ExternalFaces, ExternalFaceRatio, ComputeBuildingCentroid)
- [ ] Implementar `NormalsStrategy : IDetectionStrategy` (candidata mais simples)
- [ ] Produzir `DetectionResult` (Envelope + ElementClassification[])

**Agrupamento (Stage 2):**
- [ ] Implementar `DbscanFacadeGrouper : IFacadeGrouper`
- [ ] DBSCAN sobre normais + QuikGraph para componentes conectados
- [ ] Produzir `Facade[]`

**Saída:**
- [ ] Implementar `ReportBuilder` e `JsonReportWriter`
- [ ] Calcular WWR por fachada no bloco `metrics`
- [ ] CLI mínima: `ifcenvmapper detect duplex.ifc --strategy normals`
- [ ] Inspeção manual dos resultados com IFC viewer (BIMvision ou xBIM WebUI)

---

### Fase 2 — Estratégias alternativas
**Meta:** implementar as demais estratégias para exploração e seleção da mais adequada.
**Critério de sucesso:** resultados comparáveis entre estratégias; seleção fundamentada da estratégia primária.

**Ray Casting:**
- [ ] Implementar `RayCastingStrategy` usando geometry3Sharp BVH
- [ ] Testes unitários: ray-triangle intersection, BVH queries

**Voxel / Flood-fill:**
- [ ] Implementar `VoxelGrid3D` e voxelização de meshes trianguladas
- [ ] Implementar flood-fill 3D (BFS a partir do exterior)
- [ ] Implementar `VoxelFloodFillStrategy`

**Seleção:**
- [ ] Comparar resultados preliminares das 3 estratégias em 2-3 modelos
- [ ] Selecionar e documentar a estratégia primária com justificativa
- [ ] Documentar alternativas investigadas para capítulo de Discussão

---

### Fase 3 — Ground Truth e Avaliação Experimental
**Meta:** validar o método contra rótulos manuais de especialistas.
**Critério de sucesso:** tabela Precisão/Recall/F1 por modelo e por tipologia.

- [ ] Selecionar 3–5 modelos IFC de tipologias diferentes
- [ ] Definir protocolo de rotulação (critérios, ferramenta, resolução de divergências)
- [ ] Recrutar 5+ profissionais AEC para rotulação
- [ ] Implementar leitor de ground truth CSV → `Dictionary<string, bool>`
- [ ] Calcular Precisão, Recall, F1, Kappa de Cohen
- [ ] Implementar `BcfWriter` para validação qualitativa com especialistas
- [ ] Gerar tabela de resultados para o TCC

---

### Fase 4 — Entrega
**Meta:** finalizar documentação e publicar ferramenta como open-source.

- [ ] README com instruções de instalação e uso
- [ ] Publicar no GitHub como repositório público

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

**Objetivo:** completar a Fase 0 (spike).

1. Criar a solução `.sln` com os 5 projetos
2. Instalar pacotes NuGet
3. Escrever `XbimModelLoader.Load()` mínimo — abrir `duplex.ifc` e listar elementos com seus tipos IFC
4. Extrair triangulated mesh de um `IfcWall` via `Xbim3DModelContext`
5. Confirmar que normais de face são acessíveis e fazem sentido geométrico

**Arquivo IFC de entrada:** `duplex.ifc` (em `data/models/`)
