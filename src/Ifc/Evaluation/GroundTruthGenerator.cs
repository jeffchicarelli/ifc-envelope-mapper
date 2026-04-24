using IfcEnvelopeMapper.Core.Domain.Element;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace IfcEnvelopeMapper.Ifc.Evaluation;

public static class GroundTruthGenerator
{
    // Extract IsExternal psets from an IFC file and emit a ground-truth CSV.
    // Only elements present in `loadedElements` are emitted (matches the filter
    // applied by XbimModelLoader); IsExternal=null becomes "unknown".
    // Returns the number of records written.
    public static int GenerateFromIfc(
        string ifcPath,
        string csvOutputPath,
        IEnumerable<BuildingElement> loadedElements)
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
            if (!ifcTypeById.TryGetValue(gid, out var ifcType)) continue;

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
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllLines(csvOutputPath, lines);
        return lines.Count - 1;
    }
}
