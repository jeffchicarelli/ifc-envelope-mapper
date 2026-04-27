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
        IfcPath = TestPaths.FindModel("duplex.ifc");
        Model   = new XbimModelLoader().Load(IfcPath);
    }
}
