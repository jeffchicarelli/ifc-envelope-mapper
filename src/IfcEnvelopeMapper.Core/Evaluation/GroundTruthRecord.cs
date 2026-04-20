namespace IfcEnvelopeMapper.Core.Evaluation;

// IsExterior is tri-state:
//   true    = labeled exterior (from IFC pset or manual curation)
//   false   = labeled interior
//   null    = unknown — excluded from metrics
// Note is descriptive only; MetricsCalculator never inspects it.
public sealed record GroundTruthRecord(string GlobalId, bool? IsExterior, string? Note);
