using g4;

namespace IfcEnvelopeMapper.Domain.Extensions;

/// <summary>Extension methods on <see cref="g4.DMesh3"/>.</summary>
public static class DMesh3Extensions
{
    private const double DEGENERATE_NORMAL_SQ = 1e-20;

    /// <summary>
    /// Merges many meshes into one. Triangle winding is preserved; vertex indices are offset so the combined index space stays consistent. Deleted
    /// vertices and triangles in the sources are skipped.
    /// </summary>
    public static DMesh3 Merge(this IEnumerable<DMesh3> meshes)
    {
        var merged = new DMesh3();
        foreach (var m in meshes)
        {
            var offset = merged.VertexCount;
            for (var vid = 0; vid < m.MaxVertexID; vid++)
            {
                if (m.IsVertex(vid))
                {
                    merged.AppendVertex(m.GetVertex(vid));
                }
            }

            for (var tid = 0; tid < m.MaxTriangleID; tid++)
            {
                if (!m.IsTriangle(tid))
                {
                    continue;
                }

                var t = m.GetTriangle(tid);
                merged.AppendTriangle(new Index3i(t.a + offset, t.b + offset, t.c + offset));
            }
        }

        return merged;
    }

    /// <summary>Translates every vertex of <paramref name="mesh"/> by <paramref name="offset"/> in place. Triangle indices and topology are unchanged.</summary>
    public static void Translate(this DMesh3 mesh, Vector3d offset)
    {
        foreach (var vid in mesh.VertexIndices())
        {
            mesh.SetVertex(vid, mesh.GetVertex(vid) + offset);
        }
    }

    /// <summary>
    /// Computes the geometric centroid and outward unit normal of triangle <paramref name="tid"/>. Returns <c>false</c> if the triangle is
    /// degenerate (zero area), in which case <paramref name="centroid"/> and <paramref name="normal"/> are set to <see cref="Vector3d.Zero"/>.
    /// </summary>
    public static bool TryGetTriangleCentroidAndNormal(this DMesh3 mesh, int tid, out Vector3d centroid, out Vector3d normal)
    {
        var t = mesh.GetTriangle(tid);
        var v0 = mesh.GetVertex(t.a);
        var v1 = mesh.GetVertex(t.b);
        var v2 = mesh.GetVertex(t.c);
        var n = (v1 - v0).Cross(v2 - v0);

        if (n.LengthSquared < DEGENERATE_NORMAL_SQ)
        {
            centroid = Vector3d.Zero;
            normal = Vector3d.Zero;

            return false;
        }

        centroid = (v0 + v1 + v2) / 3.0;
        normal = n.Normalized;

        return true;
    }

    /// <summary>
    /// Builds a new mesh containing only the given triangles from <paramref name="source"/>. Each triangle gets its own three vertices (no vertex
    /// sharing), so the result has <c>3 × triangleCount</c> vertices. Triangle ids that no longer exist in the source are silently skipped.
    /// </summary>
    public static DMesh3 ExtractTriangles(this DMesh3 source, IEnumerable<int> triangleIds)
    {
        var mesh = new DMesh3();
        foreach (var tid in triangleIds)
        {
            if (!source.IsTriangle(tid))
            {
                continue;
            }

            var t = source.GetTriangle(tid);
            var a = mesh.AppendVertex(source.GetVertex(t.a));
            var b = mesh.AppendVertex(source.GetVertex(t.b));
            var c = mesh.AppendVertex(source.GetVertex(t.c));

            mesh.AppendTriangle(new Index3i(a, b, c));
        }

        return mesh;
    }
}
