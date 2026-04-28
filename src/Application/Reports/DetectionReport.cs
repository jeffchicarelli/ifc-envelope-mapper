using IfcEnvelopeMapper.Domain.Detection;

namespace IfcEnvelopeMapper.Application.Reports;

/// <summary>
/// Outcome of one detection run, serialised to JSON by <c>IJsonReportWriter</c>. Schema v1 covers input identification, strategy tuning,
/// classification counts, the per-element list, and run metadata; <c>SchemaVersion</c> gates forward evolution.
/// </summary>
public sealed record DetectionReport
(
    string SchemaVersion,
    string Input,
    string Strategy,
    StrategyConfig Config,
    int ExteriorCount,
    int InteriorCount,
    IReadOnlyList<ElementReport> Elements,
    DateTimeOffset GeneratedAt,
    double DurationSeconds
);

/// <summary>Per-element classification row. Sorted by <see cref="GlobalId"/> in the report so two runs over the same input produce byte-identical JSON.</summary>
public sealed record ElementReport(string GlobalId, string IfcType, bool IsExterior);
