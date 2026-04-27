using IfcEnvelopeMapper.Ifc.Loading;

namespace IfcEnvelopeMapper.Tests.Fixtures;

// Per-test-class fixture for demo2.ifc. Symmetric with IfcModelFixture
// (which loads duplex). Tests against a second real-world model contribute
// to the strategy comparison required by P3.1.
public sealed class Demo2ModelFixture
{
    public string IfcPath { get; }
    public ModelLoadResult Model { get; }

    public Demo2ModelFixture()
    {
        IfcPath = TestPaths.FindModel("demo2.ifc");
        Model   = new XbimModelLoader().Load(IfcPath);
    }
}
