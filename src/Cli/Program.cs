using System.CommandLine;
using IfcEnvelopeMapper.Cli.Commands;
using IfcEnvelopeMapper.Infrastructure.Diagnostics;
using IfcEnvelopeMapper.Infrastructure.Visualization.Api;
using Microsoft.Extensions.Logging;
using Xbim.Common.Configuration;

// CLI is the production runner: no viewer helper, no GLB output. The debug
// emission path is reserved for xunit tests (which keep the default
// Enabled=true and produce their own per-test disagreement GLBs).
GeometryDebug.Enabled = false;

using var loggerFactory = LoggerFactory.Create(b => b
    .AddConsole()
    .SetMinimumLevel(LogLevel.Warning)
    .AddFilter("IfcEnvelopeMapper", LogLevel.Information));

AppLog.Configure(loggerFactory);

XbimServices.Current.ConfigureServices(s => s
    .AddXbimToolkit(c => c.AddLoggerFactory(loggerFactory)));

var root = new RootCommand("ifcenvmapper — IFC building envelope mapper")
{
    DetectCommand.Build(),
};

return await root.InvokeAsync(args);
