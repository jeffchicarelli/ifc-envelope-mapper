using g4;
using IfcEnvelopeMapper.Domain.Extensions;
using IfcEnvelopeMapper.Domain.Interfaces;

namespace IfcEnvelopeMapper.Domain.Tests.Extensions;

public class BoxEntityExtensionsTests
{
    private const double TOLERANCE = 1e-10;

    [Fact]
    public void BoundingBox_SingleEntity_ReturnsThatEntitysBox()
    {
        var box      = new AxisAlignedBox3d(new Vector3d(-1, -2, -3), new Vector3d(4, 5, 6));
        var entities = new[] { new BoxEntityStub(box) };

        var result = entities.BoundingBox();

        result.Min.Distance(box.Min).Should().BeLessThan(TOLERANCE);
        result.Max.Distance(box.Max).Should().BeLessThan(TOLERANCE);
    }

    [Fact]
    public void BoundingBox_DisjointEntities_UnionContainsAll()
    {
        var entities = new[]
        {
            new BoxEntityStub(new AxisAlignedBox3d(new Vector3d(0, 0, 0), new Vector3d(1, 1, 1))),
            new BoxEntityStub(new AxisAlignedBox3d(new Vector3d(5, 5, 5), new Vector3d(6, 6, 6))),
        };

        var result = entities.BoundingBox();

        result.Min.Distance(new Vector3d(0, 0, 0)).Should().BeLessThan(TOLERANCE);
        result.Max.Distance(new Vector3d(6, 6, 6)).Should().BeLessThan(TOLERANCE);
    }

    [Fact]
    public void BoundingBox_OverlappingEntities_UnionExtendsBoundsOfBoth()
    {
        var entities = new[]
        {
            new BoxEntityStub(new AxisAlignedBox3d(new Vector3d(0, 0, 0), new Vector3d(3, 3, 3))),
            new BoxEntityStub(new AxisAlignedBox3d(new Vector3d(2, 2, 2), new Vector3d(5, 5, 5))),
        };

        var result = entities.BoundingBox();

        result.Min.Distance(new Vector3d(0, 0, 0)).Should().BeLessThan(TOLERANCE);
        result.Max.Distance(new Vector3d(5, 5, 5)).Should().BeLessThan(TOLERANCE);
    }

    [Fact]
    public void BoundingBox_NegativeCoordinates_AreHandledCorrectly()
    {
        // Regression guard: a naive implementation that initialised
        // min/max from Vector3d.Zero would break for a model entirely in the
        // negative octant.
        var entities = new[]
        {
            new BoxEntityStub(new AxisAlignedBox3d(new Vector3d(-10, -10, -10), new Vector3d(-5, -5, -5))),
            new BoxEntityStub(new AxisAlignedBox3d(new Vector3d(-3,  -3,  -3),  new Vector3d(-1, -1, -1))),
        };

        var result = entities.BoundingBox();

        result.Min.Distance(new Vector3d(-10, -10, -10)).Should().BeLessThan(TOLERANCE);
        result.Max.Distance(new Vector3d(-1,  -1,  -1)).Should().BeLessThan(TOLERANCE);
    }

    [Fact]
    public void BoundingBox_LazyEnumerable_IsMaterialisedOnce()
    {
        // Select() yields a non-IList sequence, forcing the `?? .ToList()` fallback.
        // The yields counter proves the source is enumerated only once.
        var yields = 0;
        IEnumerable<IBoxEntity> Source()
        {
            yields++; yield return new BoxEntityStub(new AxisAlignedBox3d(new Vector3d(0, 0, 0), new Vector3d(1, 1, 1)));
            yields++; yield return new BoxEntityStub(new AxisAlignedBox3d(new Vector3d(2, 2, 2), new Vector3d(3, 3, 3)));
        }

        var result = Source().BoundingBox();

        yields.Should().Be(2);
        result.Max.Distance(new Vector3d(3, 3, 3)).Should().BeLessThan(TOLERANCE);
    }

    [Fact]
    public void BoundingBox_EmptySequence_Throws()
    {
        var entities = Array.Empty<IBoxEntity>();

        var act = () => entities.BoundingBox();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*empty*");
    }

    private sealed class BoxEntityStub : IBoxEntity
    {
        private readonly AxisAlignedBox3d _box;
        public BoxEntityStub(AxisAlignedBox3d box) => _box = box;
        public AxisAlignedBox3d GetBoundingBox() => _box;
    }
}
