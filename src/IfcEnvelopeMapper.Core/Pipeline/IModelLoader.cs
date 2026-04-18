namespace IfcEnvelopeMapper.Core.Pipeline;

public interface IModelLoader
{
    ModelLoadResult Load(string path);
}
