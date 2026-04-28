using IfcEnvelopeMapper.Domain.Interfaces;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace IfcEnvelopeMapper.Infrastructure.Persistence;

/// <summary>
/// Reads IFC <c>IsExternal</c> property sets and writes a ground-truth CSV
/// for the elements returned by <see cref="XbimModelLoader"/>.
/// </summary>
public static class GroundTruthGenerator
{
    /// <summary>
    /// Opens <paramref name="ifcPath"/>, reads <c>IsExternal</c> from every
    /// <c>IIfcBuildingElement</c> property set, and writes a <c>GlobalId,IsExterior,Note</c>
    /// CSV to <paramref name="csvOutputPath"/>. Only elements in <paramref name="loadedElements"/>
    /// are emitted; <c>IsExternal == null</c> becomes <c>"unknown"</c>.
    /// Returns the number of records written.
    /// </summary>
    public static int GenerateFromIfc(
        string ifcPath,
        string csvOutputPath,
        IEnumerable<IElement> loadedElements)
    {
        var ifcTypeById = loadedElements.ToDictionary(
            e => e.GlobalId,
            e => e.IfcType,
            StringComparer.Ordinal);

        using var store = IfcStore.Open(ifcPath);
        var lines = new List<string> { "GlobalId,IsExterior,Note" };

        foreach (var entity in store.Instances.OfType<IIfcBuildingElement>())
        {
            var gid = entity.GlobalId.ToString();
            if (!ifcTypeById.TryGetValue(gid, out var ifcType))
            {
                continue;
            }

            var props = entity.IsDefinedBy
                              .Where(e => e is not null)
                              .SelectMany(r =>
                                   (r.RelatingPropertyDefinition as IIfcPropertySet)?.HasProperties
                                   ?? Enumerable.Empty<IIfcProperty>())
                              .OfType<IIfcPropertySingleValue>()
                              .Where(p => p.Name == "IsExternal")
                              .Select(p => p.NominalValue?.Value)
                              .OfType<bool>()
                              .ToList();

            // Distinguish 'pset absent' (null) from 'pset present with false' (false).
            bool? isExt = props.Count > 0 ? props[0] : null;

            var value = isExt switch
            {
                true  => "true",
                false => "false",
                null  => "unknown",
            };
            var note = isExt.HasValue ? string.Empty : $"{ifcType} (auto)";

            lines.Add($"{gid},{value},{note}");
        }

        var dir = Path.GetDirectoryName(csvOutputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllLines(csvOutputPath, lines);
        return lines.Count - 1;
    }
}
