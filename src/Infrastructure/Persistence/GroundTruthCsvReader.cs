using IfcEnvelopeMapper.Application.Ports;
using IfcEnvelopeMapper.Domain.Evaluation;

namespace IfcEnvelopeMapper.Infrastructure.Persistence;

/// <summary>
/// Parses a ground-truth CSV with fixed header <c>GlobalId,IsExterior,Note</c>.
/// <c>IsExterior</c> is tri-state (<c>true</c>/<c>false</c>/<c>unknown</c>); anything
/// else throws <see cref="FormatException"/>. Blank lines are skipped.
/// The <c>Note</c> column is free-form — commas inside it are preserved because
/// parsing uses a 3-way split, not a full CSV grammar.
/// </summary>
public sealed class GroundTruthCsvReader : IGroundTruthReader
{
    private const string ExpectedHeader = "GlobalId,IsExterior,Note";

    /// <inheritdoc/>
    public IReadOnlyList<GroundTruthRecord> Read(string path)
    {
        var lines = File.ReadAllLines(path);

        if (lines.Length == 0 || lines[0].Trim() != ExpectedHeader)
        {
            var observed = lines.Length == 0 ? "<empty>" : lines[0];
            throw new FormatException(
                $"Ground truth CSV header mismatch. Expected '{ExpectedHeader}', got '{observed}'.");
        }

        var records = new List<GroundTruthRecord>(lines.Length - 1);

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // Split in max 3 parts so commas appearing in a hand-written Note do not break parsing.
            var parts = line.Split(',', 3);
            if (parts.Length < 2)
            {
                throw new FormatException(
                    $"Ground truth CSV line {i + 1}: expected at least 2 columns, got {parts.Length}. Line: '{line}'.");
            }

            bool? isExterior = parts[1].Trim().ToLowerInvariant() switch
            {
                "true"    => true,
                "false"   => false,
                "unknown" => null,
                _ => throw new FormatException(
                    $"Ground truth CSV line {i + 1}: IsExterior must be 'true', 'false', or 'unknown'. Got '{parts[1]}'."),
            };

            var rawNote = parts.Length == 3 ? parts[2].Trim() : string.Empty;
            var note    = string.IsNullOrEmpty(rawNote) ? null : rawNote;

            records.Add(new GroundTruthRecord(parts[0], isExterior, note));
        }

        return records;
    }
}
