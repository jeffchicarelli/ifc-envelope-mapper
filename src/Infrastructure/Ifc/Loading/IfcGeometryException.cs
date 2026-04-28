namespace IfcEnvelopeMapper.Infrastructure.Ifc.Loading;

/// <summary>Thrown when the xBIM geometry context fails to tessellate the model after a successful file open.</summary>
public sealed class IfcGeometryException : Exception
{
    /// <summary>Absolute path to the IFC file whose tessellation failed.</summary>
    public string ModelPath { get; }

    /// <summary>Creates an instance with the failing path, a descriptive message, and an optional inner exception.</summary>
    public IfcGeometryException(string modelPath, string message, Exception? inner = null)
        : base(message, inner)
    {
        ModelPath = modelPath;
    }
}
