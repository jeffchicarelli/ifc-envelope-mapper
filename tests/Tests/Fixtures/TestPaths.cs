namespace IfcEnvelopeMapper.Tests.Fixtures;

// Locates project-relative test artefacts (IFC models, ground-truth CSVs)
// without depending on the test runner's working directory or hard-coded
// absolute paths. The repo layout under search:
//
//     <repo>/data/models/<file>.ifc
//     <repo>/data/ground-truth/<file>.csv
//
// FindUpward walks parents until either the file is found or the filesystem
// root is reached. Test bin folders typically sit a few levels deep
// (tests/Tests/bin/Debug/net8.0/...) so the search terminates quickly.
internal static class TestPaths
{
    public static string FindModel(string fileName)
        => FindUpward(Path.Combine("data", "models", fileName))
           ?? throw new FileNotFoundException(
               $"{fileName} not found in any parent directory of " + Directory.GetCurrentDirectory());

    // Returns the existing GT path if found, otherwise the canonical write
    // location next to data/models so the GroundTruthGenerator can create it.
    public static string GroundTruthPath(string fileName)
    {
        var found = FindUpward(Path.Combine("data", "ground-truth", fileName));
        if (found is not null) return found;

        // Anchor on data/models — already present — so we land at data/ground-truth.
        var modelsDir = FindUpward("data") ?? throw new DirectoryNotFoundException(
            "data directory not found upward from " + Directory.GetCurrentDirectory());
        return Path.Combine(modelsDir, "ground-truth", fileName);
    }

    // Canonical destination for committed test artefacts (e.g. the strategy
    // comparison table). The folder may not exist yet — caller creates it.
    public static string ResultsPath(string fileName)
    {
        var dataDir = FindUpward("data") ?? throw new DirectoryNotFoundException(
            "data directory not found upward from " + Directory.GetCurrentDirectory());
        return Path.Combine(dataDir, "results", fileName);
    }

    private static string? FindUpward(string relative)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
