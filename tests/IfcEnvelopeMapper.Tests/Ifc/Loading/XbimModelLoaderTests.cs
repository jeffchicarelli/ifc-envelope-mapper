using IfcEnvelopeMapper.Ifc.Loading;

namespace IfcEnvelopeMapper.Tests.Ifc.Loading;

[Trait("Category", "Integration")]
public sealed class XbimModelLoaderTests
{
    private static string FindDuplex()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "data", "models", "duplex.ifc");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("duplex.ifc not found in any parent directory");
    }

    [Fact]
    public void Load_Duplex_ReturnsElements()
    {
        var loader = new XbimModelLoader();
        var path = FindDuplex();

        var result = loader.Load(path);

        result.Elements.Should().NotBeEmpty();
        result.Elements.Should().AllSatisfy(e =>
        {
            e.GlobalId.Should().NotBeNullOrEmpty();
            e.IfcType.Should().NotBeNullOrEmpty();
        });
    }

    [Fact]
    public void Load_Duplex_AllElementsHaveGeometry()
    {
        var loader = new XbimModelLoader();
        var path = FindDuplex();

        var result = loader.Load(path);

        result.Elements.Should().AllSatisfy(e =>
            e.Mesh.TriangleCount.Should().BeGreaterThan(0));
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
