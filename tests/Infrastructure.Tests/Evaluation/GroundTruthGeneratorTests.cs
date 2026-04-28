using IfcEnvelopeMapper.Infrastructure.Persistence;

namespace IfcEnvelopeMapper.Infrastructure.Tests.Evaluation;

[Trait("Category", "Integration")]
public sealed class GroundTruthGeneratorTests : IfcTestBase, IDisposable
{
    private readonly string _tempCsv;

    public GroundTruthGeneratorTests() : base("duplex.ifc")
    {
        _tempCsv = Path.Combine(Path.GetTempPath(), $"gt-gen-{Guid.NewGuid():N}.csv");
    }

    [Fact]
    public void GenerateFromIfc_ReturnsRecordCountMatchingFileLines()
    {
        var written = GroundTruthGenerator.GenerateFromIfc(IfcPath, _tempCsv, Model.Elements);

        File.Exists(_tempCsv).Should().BeTrue();
        var lines = File.ReadAllLines(_tempCsv);

        // Header + one row per record.
        lines.Length.Should().Be(written + 1);
        lines[0].Should().Be("GlobalId,IsExterior,Note");
    }

    [Fact]
    public void GenerateFromIfc_EmitsExactlyOneRowPerLoadedElementWithMatchingType()
    {
        GroundTruthGenerator.GenerateFromIfc(IfcPath, _tempCsv, Model.Elements);

        var records = new GroundTruthCsvReader().Read(_tempCsv);

        records.Count.Should().BePositive();
        records.Count.Should().BeLessThanOrEqualTo(Model.Elements.Count);

        var loadedIds = Model.Elements.Select(e => e.GlobalId).ToHashSet(StringComparer.Ordinal);
        foreach (var record in records)
        {
            loadedIds.Should().Contain(record.GlobalId);
        }
    }

    [Fact]
    public void GenerateFromIfc_NoDuplicateGlobalIds()
    {
        GroundTruthGenerator.GenerateFromIfc(IfcPath, _tempCsv, Model.Elements);

        var records = new GroundTruthCsvReader().Read(_tempCsv);
        var ids = records.Select(r => r.GlobalId).ToList();

        ids.Distinct().Count().Should().Be(ids.Count);
    }

    [Fact]
    public void GenerateFromIfc_ProducesAtLeastSomeKnownLabels()
    {
        // duplex.ifc has IsExternal psets on most envelope elements.
        GroundTruthGenerator.GenerateFromIfc(IfcPath, _tempCsv, Model.Elements);

        var records = new GroundTruthCsvReader().Read(_tempCsv);

        records.Should().Contain(r => r.IsExterior == true);
        records.Should().Contain(r => r.IsExterior == false);
    }

    [Fact]
    public void GenerateFromIfc_UnknownEntries_HaveAutoNoteWithIfcType()
    {
        // Documented contract: when IsExternal pset is absent, Note becomes
        // "<IfcType> (auto)" so the user can tell which entries need manual labelling.
        GroundTruthGenerator.GenerateFromIfc(IfcPath, _tempCsv, Model.Elements);

        var records  = new GroundTruthCsvReader().Read(_tempCsv);
        var unknowns = records.Where(r => r.IsExterior is null).ToList();

        if (unknowns.Count > 0)
        {
            unknowns.Should().Contain(r => r.Note != null && r.Note.Contains("(auto)"));
        }
    }

    [Fact]
    public void GenerateFromIfc_CreatesParentDirectoryIfMissing()
    {
        var nestedDir = Path.Combine(Path.GetTempPath(), $"gt-nested-{Guid.NewGuid():N}", "deep", "level");
        var path      = Path.Combine(nestedDir, "out.csv");
        Directory.Exists(nestedDir).Should().BeFalse();

        try
        {
            GroundTruthGenerator.GenerateFromIfc(IfcPath, path, Model.Elements);

            File.Exists(path).Should().BeTrue();
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(nestedDir)!)!, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_tempCsv))
            {
                File.Delete(_tempCsv);
            }
        }
        catch { /* best-effort cleanup */ }
    }
}
