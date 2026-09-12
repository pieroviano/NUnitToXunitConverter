using System.Diagnostics;
using System.Xml.Linq;
using ProjectsLibrary.Conversion;

namespace ProjectsLibrary;

/// <summary>
/// Points a SpecFlow project's configuration at the xUnit provider and clears out the generated feature
/// code-behind so the next build regenerates it against that provider.
/// </summary>
/// <remarks>
/// The provider package itself is swapped by <see cref="TestPackagesRewriter"/>, which already owns the
/// csproj. This handles everything beside it: the configuration file, which names the provider independently
/// of the package, and the generated code, which is shaped for whichever provider generated it.
/// </remarks>
public class SpecFlowRetargetService
{
    public File File { get; set; } = System.IO.InputOutput.Instance.File;
    public Path Path { get; set; } = System.IO.InputOutput.Instance.Path;
    public Directory Directory { get; set; } = System.IO.InputOutput.Instance.Directory;

    /// <summary>Configuration file names SpecFlow reads the provider from, newest first.</summary>
    private static readonly string[] JsonConfigurationNames = ["specflow.json"];

    private static readonly string[] XmlConfigurationNames = ["App.config", "app.config"];

    /// <summary>
    /// Retargets the project. Returns the paths that were changed or removed, which is what the caller
    /// reports; an empty result means the project had nothing SpecFlow-shaped in it.
    /// </summary>
    public IReadOnlyList<string> Retarget(string csprojPath)
    {
        var projectDir = Path.GetDirectoryName(Path.GetFullPath(csprojPath))!;
        var changed = new List<string>();

        changed.AddRange(RetargetConfiguration(projectDir));
        changed.AddRange(DeleteGeneratedFeatureCode(projectDir));

        return changed;
    }

    private IEnumerable<string> RetargetConfiguration(string projectDir)
    {
        foreach (var name in JsonConfigurationNames)
        {
            var path = Path.Combine(projectDir, name);

            if (!File.Exists(path))
                continue;

            var retargeted = SpecFlow.RetargetJson(File.ReadAllText(path));

            if (retargeted == null)
                continue;

            File.WriteAllText(path, retargeted);

            yield return path;
        }

        foreach (var name in XmlConfigurationNames)
        {
            var path = Path.Combine(projectDir, name);

            if (!File.Exists(path))
                continue;

            if (RetargetXml(path))
                yield return path;
        }
    }

    /// <summary><c>&lt;specFlow&gt;&lt;unitTestProvider name="nunit" /&gt;&lt;/specFlow&gt;</c>.</summary>
    private bool RetargetXml(string path)
    {
        XDocument document;

        try
        {
            document = XDocument.Parse(File.ReadAllText(path));
        }
        catch (Exception exception)
        {
            // A malformed App.config is not something to fail a conversion over, and it is not this
            // service's business to repair one.
            LoggerFactoryContainer.Instance.LoggerFactory.Warn(
                $"Could not read {path}, leaving its SpecFlow provider alone: {exception.Message}");

            return false;
        }

        var provider = document
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "unitTestProvider");

        var name = provider?.Attribute("name");

        if (name == null || name.Value.Equals("xunit", StringComparison.OrdinalIgnoreCase))
            return false;

        name.Value = "xunit";
        File.WriteAllText(path, document.ToString());

        return true;
    }

    private IEnumerable<string> DeleteGeneratedFeatureCode(string projectDir)
    {
        // The MSBuild generator writes into obj/, which is rebuilt anyway; only the legacy generator's
        // output, sitting beside the .feature file, needs removing.
        foreach (var file in Directory
                     .GetFiles(projectDir, "*.feature.cs", SearchOption.AllDirectories)
                     .Where(SpecFlow.IsGeneratedFeatureCode)
                     .ToList())
        {
            File.Delete(file);

            yield return file;
        }
    }
}
