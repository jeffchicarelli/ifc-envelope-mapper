namespace IfcEnvelopeMapper.Application.Ports;

/// <summary>
/// Loads an IFC file and returns a <see cref="ModelLoadResult"/> that owns
/// the underlying store lifetime. Callers must dispose the result.
/// </summary>
public interface IModelLoader
{
    ModelLoadResult Load(string ifcPath);
}
