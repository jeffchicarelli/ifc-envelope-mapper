using IfcEnvelopeMapper.Domain.Detection;

namespace IfcEnvelopeMapper.Application.Reports;

/// <summary>
/// Builds a <see cref="DetectionReport"/> from a <see cref="DetectionResult"/>
/// plus the run metadata the strategy itself doesn't carry (input path,
/// strategy name, configuration, elapsed time).
/// </summary>
public static class JsonReportBuilder
{
    public const string SCHEMA_VERSION = "1";

    public static DetectionReport Build(
        string           ifcPath,
        string           strategy,
        StrategyConfig   config,
        DetectionResult  result,
        TimeSpan         duration)
    {
        var exterior = result.Classifications.Count(c => c.IsExterior);
        var interior = result.Classifications.Count(c => !c.IsExterior);

        var elements = result.Classifications
            .Select(c => new ElementReport(c.Element.GlobalId, c.Element.IfcType, c.IsExterior))
            .OrderBy(e => e.GlobalId, StringComparer.Ordinal)
            .ToList();

        return new DetectionReport(
            SchemaVersion:   SCHEMA_VERSION,
            Input:           ifcPath,
            Strategy:        strategy,
            Config:          config,
            ExteriorCount:   exterior,
            InteriorCount:   interior,
            Elements:        elements,
            GeneratedAt:     DateTimeOffset.UtcNow,
            DurationSeconds: duration.TotalSeconds);
    }
}
