using System.Xml.Linq;
using NUnitToXunitConverter.Conversion;

namespace NUnitToXunitConverter.Projects;

internal static class ProjectScanner
{
    public static IEnumerable<string> GetCsFiles(string csprojPath)
    {
        var projectDir = Path.GetDirectoryName(csprojPath)!;
        var objDir = Path.Combine(projectDir, "obj") + Path.DirectorySeparatorChar;

        var doc = XDocument.Load(csprojPath);

        var compileItems = doc
            .Descendants()
            .Where(e => e.Name.LocalName == "Compile")
            .ToArray();

        var includedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in compileItems)
        {
            var include = item.Attribute("Include")?.Value;
            var remove = item.Attribute("Remove")?.Value;

            if (!string.IsNullOrWhiteSpace(include))
            {
                include = ExpandMsBuildProperties(include, csprojPath);

                foreach (var file in ExpandGlob(projectDir, include))
                {
                    if (IsCandidateCsFile(file, objDir))
                    {
                        includedFiles.Add(file);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(remove))
            {
                remove = ExpandMsBuildProperties(remove, csprojPath);

                foreach (var file in ExpandGlob(projectDir, remove))
                {
                    removedFiles.Add(file);
                }
            }
        }

        // SDK-style implicit include fallback
        if (includedFiles.Count == 0)
        {
            foreach (var file in Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories))
            {
                var fullPath = Path.GetFullPath(file);
                if (IsCandidateCsFile(fullPath, objDir))
                {
                    includedFiles.Add(fullPath);
                }
            }
        }

        return includedFiles
            .Except(removedFiles)
            .Where(File.Exists);
    }

    public static string[] GetNUnitCsFiles(string csprojPath)
    {
        return GetCsFiles(csprojPath).Where(NUnitTestDetector.IsNUnitTest).OrderBy(f => File.ReadAllText(f).Contains("[OneTimeSetUp]") ? "_____.cs" : f).ToArray();
    }

    private static IEnumerable<string> ExpandGlob(string baseDir, string pattern)
    {
        var rootedPattern = Path.GetFullPath(Path.Combine(baseDir, pattern));

        // No glob → single file
        if (!pattern.Contains('*'))
        {
            yield return rootedPattern;
            yield break;
        }

        // ** recursive glob
        if (pattern.Contains("**"))
        {
            var rootPart = pattern[..pattern.IndexOf("**", StringComparison.Ordinal)];
            var searchDir = Path.GetFullPath(Path.Combine(baseDir, rootPart));

            if (!Directory.Exists(searchDir))
            {
                yield break;
            }

            var searchPattern = Path.GetFileName(pattern);

            foreach (var file in Directory.GetFiles(searchDir, searchPattern, SearchOption.AllDirectories))
            {
                yield return Path.GetFullPath(file);
            }
        }
        else
        {
            var searchDir = Path.GetDirectoryName(rootedPattern)!;
            var searchPattern = Path.GetFileName(pattern);

            if (!Directory.Exists(searchDir))
            {
                yield break;
            }

            foreach (var file in Directory.GetFiles(searchDir, searchPattern, SearchOption.TopDirectoryOnly))
            {
                yield return Path.GetFullPath(file);
            }
        }
    }

    private static string ExpandMsBuildProperties(string value, string csprojPath)
    {
        var projectDir = Path.GetDirectoryName(csprojPath)!;
        var projectName = Path.GetFileName(projectDir);

        return value
            .Replace("$(MSBuildThisFileDirectory)", projectDir + Path.DirectorySeparatorChar)
            .Replace("$(ProjectDir)", projectDir + Path.DirectorySeparatorChar)
            .Replace("$(MSBuildProjectDirectory)", projectDir)
            .Replace("$(MSBuildProjectName)", projectName)
            .Replace("$(MSBuildProjectFullPath)", csprojPath);
    }

    private static bool IsCandidateCsFile(string fullPath, string objDir)
    {
        if (!fullPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (fullPath.StartsWith(objDir, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}