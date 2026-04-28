using IfcEnvelopeMapper.Domain.Detection;

namespace IfcEnvelopeMapper.Application.Reports;

/// <summary>
/// JSON-shaped report describing the outcome of one detection run. Produced by
/// <see cref="JsonReportBuilder"/>, serialised by <c>IJsonReportWriter</c>.
/// <para>
/// The schema is intentionally minimal at v1 — input identification, strategy
/// + its tuning, classification counts, the per-element list, and run
/// metadata. Future LoD outputs (Biljecki/van der Vaart framework) extend the
/// schema by adding new sections; the version field gates that evolution.
/// </para>
/// </summary>
public sealed record DetectionReport(
    string                       SchemaVersion,
    string                       Input,
    string                       Strategy,
    StrategyConfig               Config,
    int                          ExteriorCount,
    int                          InteriorCount,
    IReadOnlyList<ElementReport> Elements,
    DateTimeOffset               GeneratedAt,
    double                       DurationSeconds);

/// <summary>
/// Per-element classification row. Sorted by <see cref="GlobalId"/> in the
/// report so two runs over the same input produce byte-identical JSON.
/// </summary>
public sealed record ElementReport(
    string GlobalId,
    string IfcType,
    bool   IsExterior);
