namespace IfcEnvelopeMapper.Infrastructure.Ifc.Loading;

/// <summary>Thrown when an IFC file cannot be opened or parsed by the xBIM stack.</summary>
public sealed class IfcLoadException : Exception
{
    /// <summary>Absolute path to the IFC file that triggered the failure.</summary>
    public string ModelPath { get; }

    /// <summary>Creates an instance with the failing path, a descriptive message, and an optional inner exception.</summary>
    public IfcLoadException(string modelPath, string message, Exception? inner = null)
        : base(message, inner)
    {
        ModelPath = modelPath;
    }
}
