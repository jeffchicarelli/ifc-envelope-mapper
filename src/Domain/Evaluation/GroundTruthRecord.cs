namespace IfcEnvelopeMapper.Domain.Evaluation;

/// <summary>
/// One labelled row of a ground-truth CSV. <paramref name="IsExterior"/> is tri-state: <c>true</c> = exterior, <c>false</c> = interior,
/// <c>null</c> = unknown (excluded from metrics). <paramref name="Note"/> is descriptive only — readers never inspect it.
/// </summary>
public sealed record GroundTruthRecord(string GlobalId, bool? IsExterior, string? Note);
