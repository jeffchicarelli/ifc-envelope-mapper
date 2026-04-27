using IfcEnvelopeMapper.Engine.Pipeline.Evaluation;
using IfcEnvelopeMapper.Engine.Pipeline.Evaluation.Types;

namespace IfcEnvelopeMapper.Tests.Engine.Pipeline.Evaluation;

public class GroundTruthCsvReaderTests : IDisposable
{
    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), $"gt-{Guid.NewGuid():N}.csv");

    public void Dispose()
    {
        if (File.Exists(_tempPath))
        {
            File.Delete(_tempPath);
        }
    }

    private IReadOnlyList<GroundTruthRecord> ReadFromContent(string content)
    {
        File.WriteAllText(_tempPath, content);
        return GroundTruthCsvReader.Read(_tempPath);
    }

    // ───── Header handling ─────

    [Fact]
    public void Read_EmptyFile_Throws()
    {
        var act = () => ReadFromContent(string.Empty);

        act.Should().Throw<FormatException>().WithMessage("*header mismatch*");
    }

    [Fact]
    public void Read_WrongHeader_Throws()
    {
        var act = () => ReadFromContent("Id,Label,Comment\n");

        act.Should().Throw<FormatException>().WithMessage("*header mismatch*");
    }

    [Fact]
    public void Read_HeaderOnly_ReturnsEmptyList()
    {
        var records = ReadFromContent("GlobalId,IsExterior,Note\n");

        records.Should().BeEmpty();
    }

    [Fact]
    public void Read_HeaderWithSurroundingWhitespace_IsAccepted()
    {
        // Leading/trailing whitespace on the header line is trimmed before comparison.
        var records = ReadFromContent("  GlobalId,IsExterior,Note  \n");

        records.Should().BeEmpty();
    }

    // ───── IsExterior parsing ─────

    [Fact]
    public void Read_TriStateLabels_AreParsedToBoolNullable()
    {
        var content =
            "GlobalId,IsExterior,Note\n" +
            "a,true,\n" +
            "b,false,\n" +
            "c,unknown,\n";

        var records = ReadFromContent(content);

        records.Should().HaveCount(3);
        records[0].IsExterior.Should().BeTrue();
        records[1].IsExterior.Should().BeFalse();
        records[2].IsExterior.Should().BeNull();
    }

    [Fact]
    public void Read_IsExteriorIsCaseInsensitive()
    {
        var content =
            "GlobalId,IsExterior,Note\n" +
            "a,TRUE,\n" +
            "b,False,\n" +
            "c,Unknown,\n";

        var records = ReadFromContent(content);

        records[0].IsExterior.Should().BeTrue();
        records[1].IsExterior.Should().BeFalse();
        records[2].IsExterior.Should().BeNull();
    }

    [Fact]
    public void Read_InvalidIsExterior_Throws()
    {
        var content =
            "GlobalId,IsExterior,Note\n" +
            "a,maybe,\n";

        var act = () => ReadFromContent(content);

        act.Should().Throw<FormatException>().WithMessage("*IsExterior must be*");
    }

    // ───── Note handling ─────

    [Fact]
    public void Read_MissingNoteColumn_YieldsNullNote()
    {
        // Only 2 columns provided — Note defaults to null.
        var content =
            "GlobalId,IsExterior,Note\n" +
            "a,true\n";

        var records = ReadFromContent(content);

        records[0].Note.Should().BeNull();
    }

    [Fact]
    public void Read_EmptyNote_IsNormalizedToNull()
    {
        // Trailing comma + empty → null, not "".
        var content =
            "GlobalId,IsExterior,Note\n" +
            "a,true,\n";

        var records = ReadFromContent(content);

        records[0].Note.Should().BeNull();
    }

    [Fact]
    public void Read_NoteWithCommas_IsPreservedInFull()
    {
        // Split(',', 3) keeps extra commas inside the Note — the reader is
        // intentionally not a full CSV grammar.
        var content =
            "GlobalId,IsExterior,Note\n" +
            "a,true,interior wall, north wing\n";

        var records = ReadFromContent(content);

        records[0].Note.Should().Be("interior wall, north wing");
    }

    [Fact]
    public void Read_NoteWithSurroundingWhitespace_IsTrimmed()
    {
        var content =
            "GlobalId,IsExterior,Note\n" +
            "a,true,  facade panel  \n";

        var records = ReadFromContent(content);

        records[0].Note.Should().Be("facade panel");
    }

    // ───── Line handling ─────

    [Fact]
    public void Read_BlankLines_AreSkipped()
    {
        var content =
            "GlobalId,IsExterior,Note\n" +
            "\n" +
            "a,true,\n" +
            "   \n" +
            "b,false,\n";

        var records = ReadFromContent(content);

        records.Should().HaveCount(2);
        records[0].GlobalId.Should().Be("a");
        records[1].GlobalId.Should().Be("b");
    }

    [Fact]
    public void Read_LineWithSingleColumn_Throws()
    {
        var content =
            "GlobalId,IsExterior,Note\n" +
            "onlyId\n";

        var act = () => ReadFromContent(content);

        act.Should().Throw<FormatException>().WithMessage("*expected at least 2 columns*");
    }

    [Fact]
    public void Read_GlobalIdIsNotTrimmed()
    {
        // GlobalId goes through as-is (no trim). This guards against accidental
        // "normalization" that would make IFC GlobalIds stop matching.
        var content =
            "GlobalId,IsExterior,Note\n" +
            " 1abc$XYZ ,true,\n";

        var records = ReadFromContent(content);

        records[0].GlobalId.Should().Be(" 1abc$XYZ ");
    }
}
