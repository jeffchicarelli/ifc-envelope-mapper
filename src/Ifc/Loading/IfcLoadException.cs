namespace IfcEnvelopeMapper.Ifc.Loading;

public sealed class IfcLoadException : Exception
{
    public string ModelPath { get; }

    public IfcLoadException(string modelPath, string message, Exception? inner = null)
        : base(message, inner)
    {
        ModelPath = modelPath;
    }
}
