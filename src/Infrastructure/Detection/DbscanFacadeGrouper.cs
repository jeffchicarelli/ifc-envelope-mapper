using System.Diagnostics;
using g4;
using Microsoft.Extensions.Logging;
using QuikGraph;
using QuikGraph.Algorithms;
using IfcEnvelopeMapper.Domain.Services;
using IfcEnvelopeMapper.Domain.Surface;
using IfcEnvelopeMapper.Infrastructure.Visualization.Api;
using static IfcEnvelopeMapper.Infrastructure.Diagnostics.AppLog;

namespace IfcEnvelopeMapper.Infrastructure.Detection;

/// <summary>
/// Groups exterior <see cref="Face"/>s from an <see cref="Envelope"/> into
/// <see cref="Facade"/>s using DBSCAN over the Gauss sphere followed by a
/// QuikGraph connected-components split inside each orientation cluster.
/// <list type="bullet">
/// <item><description>DBSCAN clusters faces by angular distance between normals
///   (ε = <see cref="EpsilonDeg"/>°, metric = arccos(n₁·n₂)). Noise faces —
///   those that do not belong to any dense cluster — are silently discarded;
///   they represent ambiguously-oriented geometry.</description></item>
/// <item><description>Within each orientation cluster, QuikGraph separates
///   physically disconnected groups (e.g. a north-facing wall on opposite sides
///   of a courtyard) into independent <see cref="Facade"/>s via undirected
///   connected-components over a centroid-distance adjacency graph.</description></item>
/// <item><description>Facades are returned ordered by <see cref="Facade.AzimuthDegrees"/>
///   ascending — facade-00 is always the most-northward orientation.</description></item>
/// </list>
/// </summary>
public sealed class DbscanFacadeGrouper : IFacadeGrouper
{
    /// <summary>Angular tolerance for normal clustering (degrees). Default 15°.</summary>
    public double EpsilonDeg { get; }

    /// <summary>Minimum number of faces to form an orientation cluster. Default 3.</summary>
    public int MinFaces { get; }

    /// <summary>
    /// Maximum centroid distance (metres) for two faces to be considered adjacent in
    /// the spatial connectivity graph. Default 3.0 m — larger than typical wall
    /// thickness (~0.3 m) but smaller than typical facade-to-facade gap.
    /// </summary>
    public double AdjacencyM { get; }

    /// <summary>Creates a grouper with the given DBSCAN and adjacency parameters.</summary>
    public DbscanFacadeGrouper(
        double epsilonDeg = 15.0,
        int    minFaces   = 3,
        double adjacencyM = 3.0)
    {
        EpsilonDeg = epsilonDeg;
        MinFaces   = minFaces;
        AdjacencyM = adjacencyM;
    }

    /// <inheritdoc/>
    public IReadOnlyList<Facade> Group(Envelope envelope)
    {
        if (envelope.Faces.Count == 0)
        {
            return [];
        }

        // Step 1: DBSCAN — cluster faces by angular distance on the Gauss sphere.
        // Each face's outward normal is a point on the unit sphere; angular distance
        // = arccos(n₁·n₂). Clusters correspond to dominant facade orientations.
        var (clusters, noiseCount) = GaussSphereDbscan(envelope.Faces);

        Log.LogInformation(
            "DBSCAN: {ClusterCount} orientation clusters, {NoiseCount} noise faces discarded",
            clusters.Count, noiseCount);

        var facades   = new List<Facade>();
        var facadeIdx = 0;

        foreach (var clusterFaces in clusters)
        {
            // Step 2: QuikGraph — split disconnected groups within the same
            // orientation cluster. Two north-facing walls 50 m apart must become
            // two Facades, not one. Build an undirected adjacency graph: edge
            // exists if centroid distance ≤ AdjacencyM. Each connected component
            // becomes an independent Facade.
            var graph = new UndirectedGraph<Face, Edge<Face>>();
            graph.AddVertexRange(clusterFaces);

            for (var i = 0; i < clusterFaces.Count; i++)
            {
                for (var j = i + 1; j < clusterFaces.Count; j++)
                {
                    var dist = (clusterFaces[i].Centroid - clusterFaces[j].Centroid).Length;
                    if (dist <= AdjacencyM)
                    {
                        graph.AddEdge(new Edge<Face>(clusterFaces[i], clusterFaces[j]));
                    }
                }
            }

            var labels = new Dictionary<Face, int>();
            graph.ConnectedComponents(labels);

            // Step 3: one Facade per connected component.
            foreach (var component in labels
                         .GroupBy(kv => kv.Value)
                         .Select(g => (IReadOnlyList<Face>)g.Select(kv => kv.Key).ToList()))
            {
                var dominant = DominantNormal(component);
                var azimuth  = Azimuth(dominant);

                facades.Add(new Facade(
                    id:             $"facade-{facadeIdx++:D2}",
                    envelope:       envelope,
                    faces:          component,
                    facadeShell:    new DMesh3(),
                    dominantNormal: dominant,
                    azimuthDegrees: azimuth));
            }
        }

        // Sort ascending by azimuth so facade-00 is always the most-northward.
        var ordered = facades.OrderBy(f => f.AzimuthDegrees).ToList();

        EmitDebug(ordered);

        return ordered;
    }

    private const double MinFacadeArea = 1e-10;

    // Area-weighted average of all face normals — large faces dominate the
    // orientation so a small tilted triangle does not skew the result.
    private static Vector3d DominantNormal(IReadOnlyList<Face> faces)
    {
        var totalArea = faces.Sum(f => f.Area);
        if (totalArea < MinFacadeArea)
        {
            return faces[0].Normal;
        }

        var sum = faces.Aggregate(Vector3d.Zero, (acc, f) => acc + f.Normal * f.Area);
        return (sum / totalArea).Normalized;
    }

    // Compass bearing of the XY projection of n, clockwise from +Y (north).
    // Range: [0, 360). North = 0°, East = 90°, South = 180°, West = 270°.
    private static double Azimuth(Vector3d n)
    {
        return (Math.Atan2(n.x, n.y) * 180.0 / Math.PI + 360.0) % 360.0;
    }

    // ── DBSCAN on the Gauss sphere ──────────────────────────────────────────
    // No NuGet package supports a custom metric over an arbitrary space, so we
    // implement the classic algorithm directly. Complexity O(N²) on face count —
    // acceptable for N < 1000 (typical per-model face count).

    private (List<List<Face>> Clusters, int NoiseCount) GaussSphereDbscan(
        IReadOnlyList<Face> faces)
    {
        var epsilonRad = EpsilonDeg * Math.PI / 180.0;
        var n         = faces.Count;
        var visited   = new bool[n];
        var inCluster = new bool[n];
        var clusters  = new List<List<Face>>();

        for (var i = 0; i < n; i++)
        {
            if (visited[i])
            {
                continue;
            }

            visited[i] = true;

            var neighbors = RegionQuery(faces, i, epsilonRad);
            if (neighbors.Count < MinFaces)
            {
                continue;
            }

            var cluster = new List<Face>();
            clusters.Add(cluster);
            ExpandCluster(faces, i, neighbors, cluster, inCluster, epsilonRad, visited);
        }

        var noiseCount = inCluster.Count(b => !b);
        return (clusters, noiseCount);
    }

    private static List<int> RegionQuery(IReadOnlyList<Face> faces, int idx, double epsilonRad)
    {
        var result = new List<int>();
        for (var j = 0; j < faces.Count; j++)
        {
            var angle = Math.Acos(Math.Clamp(faces[idx].Normal.Dot(faces[j].Normal), -1.0, 1.0));
            if (angle <= epsilonRad)
            {
                result.Add(j);
            }
        }

        return result;
    }

    private void ExpandCluster(
        IReadOnlyList<Face> faces, int seedIdx, List<int> seeds,
        List<Face> cluster, bool[] inCluster, double epsilonRad, bool[] visited)
    {
        AddToCluster(faces, seedIdx, cluster, inCluster);

        var queue = new Queue<int>(seeds);
        while (queue.Count > 0)
        {
            var j = queue.Dequeue();
            if (!visited[j])
            {
                visited[j] = true;
                var newNeighbors = RegionQuery(faces, j, epsilonRad);
                if (newNeighbors.Count >= MinFaces)
                {
                    foreach (var k in newNeighbors)
                    {
                        queue.Enqueue(k);
                    }
                }
            }

            if (!inCluster[j])
            {
                AddToCluster(faces, j, cluster, inCluster);
            }
        }
    }

    private static void AddToCluster(
        IReadOnlyList<Face> faces, int idx, List<Face> cluster, bool[] inCluster)
    {
        inCluster[idx] = true;
        cluster.Add(faces[idx]);
    }

    // ── Debug ───────────────────────────────────────────────────────────────

    // 8-colour palette cycling over facade index — wraps for models with >8 facades.
    private static readonly string[] _debugPalette =
        ["#00cc44", "#cc0000", "#0088ff", "#ff8800",
         "#8800cc", "#00cccc", "#cccc00", "#888888"];

    // Colour each facade's faces with a distinct colour so the debug viewer
    // shows orientation grouping at a glance. Eliminated entirely in Release
    // (call site removed by [Conditional]).
    [Conditional("DEBUG")]
    private static void EmitDebug(IReadOnlyList<Facade> facades)
    {
        for (var i = 0; i < facades.Count; i++)
        {
            var color = Color.FromHex(_debugPalette[i % _debugPalette.Length]);
            foreach (var face in facades[i].Faces)
            {
                GeometryDebug.Send(face.Element, color);
            }
        }
    }
}
