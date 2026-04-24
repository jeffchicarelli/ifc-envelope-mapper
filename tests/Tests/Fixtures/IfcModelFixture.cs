using IfcEnvelopeMapper.Core.Pipeline.Loading;
using IfcEnvelopeMapper.Ifc.Loading;

namespace IfcEnvelopeMapper.Tests.Fixtures;

// Shared per-test-class fixture: loads duplex.ifc once and exposes the path
// + the loaded model. Used by integration tests that need both the xBIM-loaded
// elements AND the on-disk path (e.g. EvaluationPipeline takes a path to
// auto-generate the ground-truth CSV from the source IFC).
//
// Usage:
//   public sealed class MyTests : IClassFixture<IfcModelFixture>
//   {
//       private readonly IfcModelFixture _fixture;
//       public MyTests(IfcModelFixture fixture) => _fixture = fixture;
//   }
//
// Loading duplex.ifc takes ~3s; sharing the fixture across tests in a class
// keeps that cost off every individual test.
public sealed class IfcModelFixture
{
    public string IfcPath { get; }
    public ModelLoadResult Model { get; }

    public IfcModelFixture()
    {
        IfcPath = FindUpward(Path.Combine("data", "models", "duplex.ifc"))
                  ?? throw new FileNotFoundException(
                      "duplex.ifc not found in any parent directory of " + Directory.GetCurrentDirectory());

        Model = new XbimModelLoader().Load(IfcPath);
    }

    private static string? FindUpward(string relative)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
