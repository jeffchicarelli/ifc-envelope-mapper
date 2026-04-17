using g4;

namespace IfcEnvelopeMapper.Core.Building;

public sealed class BuildingElement
{
    public string GlobalId { get; }
    public string IfcType { get; }
    public DMesh3 Mesh { get; }
    public AxisAlignedBox3d BoundingBox { get; }
    public Vector3d Centroid => BoundingBox.Center;

    public BuildingElement(string globalId, string ifcType, DMesh3 mesh)
    {
        GlobalId = globalId;
        IfcType = ifcType;
        Mesh = mesh;
        BoundingBox = mesh.GetBounds();
    }
}
