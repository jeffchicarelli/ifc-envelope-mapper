using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IfcEnvelopeMapper.Infrastructure.Diagnostics;

/// <summary>
/// Single ambient <see cref="ILogger"/>. The log category is <c>"IfcEnvelopeMapper"</c>, separable from xBIM's output (<c>"Xbim.*"</c>) via
/// standard <see cref="ILoggerFactory"/> filters. Before <see cref="Configure"/> is called, <see cref="Log"/> returns
/// <see cref="NullLogger.Instance"/> (silent, zero-allocation).
/// </summary>
public static class AppLog
{
    private const string CATEGORY = "IfcEnvelopeMapper";

    /// <summary>Ambient logger. Read-only to consumers; reassigned only by <see cref="Configure"/>.</summary>
    public static ILogger Log { get; private set; } = NullLogger.Instance;

    /// <summary>Wires <paramref name="factory"/> as the source of <see cref="Log"/>. Must be called before any code path that logs.</summary>
    public static void Configure(ILoggerFactory factory)
    {
        Log = factory.CreateLogger(CATEGORY);
    }
}
