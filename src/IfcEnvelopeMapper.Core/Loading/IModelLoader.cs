namespace IfcEnvelopeMapper.Core.Loading;

public interface IModelLoader
{
    ModelLoadResult Load(string path);
}
