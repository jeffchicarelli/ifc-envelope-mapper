namespace IfcEnvelopeMapper.Application.Ports;

/// <summary>Loads an IFC file and returns a <see cref="ModelLoadResult"/> that owns the underlying store lifetime. Callers must dispose the result.</summary>
public interface IModelLoader
{
    /// <summary>Opens the IFC file at <paramref name="ifcPath"/> and returns a live result. The caller owns the lifetime — dispose when done.</summary>
    ModelLoadResult Load(string ifcPath);
}
