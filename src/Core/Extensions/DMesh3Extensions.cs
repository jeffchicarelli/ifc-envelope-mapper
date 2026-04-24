using g4;

namespace IfcEnvelopeMapper.Core.Extensions;

public static class DMesh3Extensions
{
    /// <summary>
    /// Merges many meshes into one. Triangle winding is preserved; vertex indices
    /// are offset so the combined index space stays consistent. Deleted vertices
    /// and triangles in the sources are skipped.
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

    /// <summary>
    /// Builds a new mesh containing only the given triangles from <paramref name="source"/>.
    /// Each triangle gets its own three vertices (no vertex sharing), so the result has
    /// <c>3 × triangleCount</c> vertices. Triangle ids that no longer exist in the source
    /// are silently skipped.
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
