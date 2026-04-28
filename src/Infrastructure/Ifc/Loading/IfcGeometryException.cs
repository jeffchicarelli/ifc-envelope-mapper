namespace IfcEnvelopeMapper.Infrastructure.Ifc.Loading;

public sealed class IfcGeometryException : Exception
{
    public string ModelPath { get; }

    public IfcGeometryException(string modelPath, string message, Exception? inner = null)
        : base(message, inner)
    {
        ModelPath = modelPath;
    }
}
