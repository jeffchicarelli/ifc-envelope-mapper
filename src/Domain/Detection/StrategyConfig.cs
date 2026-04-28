namespace IfcEnvelopeMapper.Domain.Detection;

/// <summary>
/// Strategy-specific tuning captured for reproducibility. Each field is nullable so a single record can describe either strategy: voxel
/// populates <see cref="VoxelSize"/> only; raycast populates the three ray fields.
/// </summary>
public sealed record StrategyConfig(double? VoxelSize, int? NumRays, double? JitterDeg, double? HitRatio);
