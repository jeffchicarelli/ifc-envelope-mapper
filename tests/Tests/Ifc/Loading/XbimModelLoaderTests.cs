using IfcEnvelopeMapper.Ifc.Loading;


namespace IfcEnvelopeMapper.Tests.Ifc.Loading;

[Trait("Category", "Integration")]
public sealed class XbimModelLoaderTests : IfcTestBase
{
    public XbimModelLoaderTests() : base("duplex.ifc") { }

    [Fact]
    public void Load_Duplex_ReturnsElements()
    {
        Model.Elements.Should().NotBeEmpty();
        Model.Elements.Should().AllSatisfy(e =>
        {
            e.GlobalId.Should().NotBeNullOrEmpty();
            e.IfcType.Should().NotBeNullOrEmpty();
        });
    }

    [Fact]
    public void Load_Duplex_AllElementsHaveGeometry()
    {
        Model.Elements.Should().AllSatisfy(e =>
            e.GetMesh().TriangleCount.Should().BeGreaterThan(0));
    }

    [Fact]
    public void Load_FileNotFound_ThrowsIfcLoadException()
    {
        var loader = new XbimModelLoader();
        var badPath = Path.Combine(Path.GetTempPath(), "nonexistent.ifc");

        var act = () => loader.Load(badPath);

        act.Should().Throw<IfcLoadException>()
           .Which.ModelPath.Should().Be(badPath);
    }
}
