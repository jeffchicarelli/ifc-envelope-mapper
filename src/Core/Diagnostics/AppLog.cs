using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IfcEnvelopeMapper.Core.Diagnostics;

/// <summary>
/// Single ambient <see cref="ILogger"/> for the whole project. Domain, pipeline,
/// and strategy classes log via <c>AppLog.Log.LogInformation(...)</c> directly —
/// no constructor injection, no per-class field, no <c>&lt;T&gt;</c> at the call
/// site. The category is fixed as <c>"IfcEnvelopeMapper"</c>, which keeps log
/// records separable from xBIM's (categorized under <c>"Xbim.*"</c>) and lets
/// the CLI filter both pipelines through one shared <see cref="ILoggerFactory"/>.
/// Until <see cref="Configure"/> runs, <see cref="Log"/> returns
/// <see cref="NullLogger.Instance"/> (silent, zero-allocation).
/// </summary>
public static class AppLog
{
    private const string CATEGORY = "IfcEnvelopeMapper";

    /// <summary>
    /// Project-wide ambient logger. Read-only to consumers; reassigned only by
    /// <see cref="Configure"/>.
    /// </summary>
    public static ILogger Log { get; private set; } = NullLogger.Instance;

    /// <summary>
    /// Installs <paramref name="factory"/> as the source of <see cref="Log"/>.
    /// Call once at process start (CLI <c>Program.cs</c>, test fixtures) before
    /// any code path that logs.
    /// </summary>
    public static void Configure(ILoggerFactory factory)
        => Log = factory.CreateLogger(CATEGORY);
}
