using System.Text;
using System.Xml.Linq;
using ConversionClassLibrary.Interfaces;

namespace ProjectsLibrary;

/// <summary>
/// Swaps the test-framework package references of a project file for the xUnit set.
/// </summary>
/// <remarks>
/// Matching is by package id only, so it works for both SDK-style and legacy project files as long as they use
/// <c>PackageReference</c>. Items the project needs for other reasons are left untouched.
/// </remarks>
public class TestPackagesRewriter : ITestPackagesRewriter
{
    /// <summary>Package ids replaced outright, compared case-insensitively.</summary>
    private static readonly string[] TestPackageIds =
    [
        "Microsoft.NET.Test.Sdk",
        "MSTest",
        "NUnit",
        "NUnit3TestAdapter",
        "xunit",
        "Net4x.MsTests",
        "Net4x.XunitTests"
    ];

    /// <summary>
    /// Namespaces of the frameworks being replaced. A project-wide <c>Using</c> item pointing at one of them
    /// would stop compiling once its package is gone.
    /// </summary>
    private static readonly string[] TestFrameworkNamespaces =
    [
        "NUnit.Framework",
        "Microsoft.VisualStudio.TestTools.UnitTesting"
    ];

    /// <summary>Package id prefixes replaced outright, compared case-insensitively.</summary>
    private static readonly string[] TestPackageIdPrefixes =
    [
        "MSTest.",
        "Microsoft.VisualStudio.TestPlatform.",
        "NUnit.",
        "xunit.",
        "coverlet."
    ];

    public File File { get; set; } = System.IO.InputOutput.Instance.File;

    public IReadOnlyList<TestPackage> XunitPackages { get; set; } =
    [
        new TestPackage("coverlet.collector", "6.0.4"),
        new TestPackage("Microsoft.NET.Test.Sdk", "17.14.1"),
        new TestPackage("xunit", "2.9.3"),
        new TestPackage("xunit.runner.visualstudio", "3.1.4")
    ];

    public bool RewritePackageReferences(string csprojPath)
    {
        var originalText = File.ReadAllText(csprojPath);
        var doc = XDocument.Parse(originalText);

        if (doc.Root == null)
        {
            return false;
        }

        var replaced = doc
            .Descendants()
            .Where(e => e.Name.LocalName == "PackageReference" && IsTestPackage(GetPackageId(e)))
            .ToList();

        // "Replace in place": the new item group takes the position of the item group that held the first match,
        // so the project file keeps the shape the author gave it.
        var anchor = replaced.FirstOrDefault()?.Parent;
        var itemGroup = BuildItemGroup(doc.Root.GetDefaultNamespace());

        if (anchor != null)
        {
            anchor.AddBeforeSelf(itemGroup);
        }
        else
        {
            doc.Root.Add(itemGroup);
        }

        foreach (var packageReference in replaced)
        {
            packageReference.Remove();
        }

        // An item group that only ever held test packages would otherwise be left behind empty.
        foreach (var emptyItemGroup in doc.Descendants()
                     .Where(e => e.Name.LocalName == "ItemGroup" && !e.Elements().Any())
                     .ToList())
        {
            emptyItemGroup.Remove();
        }

        RetargetGlobalUsings(doc);

        var newText = Serialize(doc);

        if (newText == originalText)
        {
            return false;
        }

        File.WriteAllText(csprojPath, newText);
        return true;
    }

    /// <summary>
    /// Points any project-wide <c>&lt;Using Include="NUnit.Framework" /&gt;</c> (or the MSTest equivalent) at
    /// <c>Xunit</c>, collapsing the result if the project already imported it.
    /// </summary>
    private static void RetargetGlobalUsings(XDocument doc)
    {
        var usings = doc
            .Descendants()
            .Where(e => e.Name.LocalName == "Using")
            .ToList();

        var alreadyImportsXunit = usings.Any(u =>
            string.Equals(u.Attribute("Include")?.Value, "Xunit", StringComparison.OrdinalIgnoreCase));

        foreach (var element in usings)
        {
            var include = element.Attribute("Include")?.Value;

            if (include == null || !TestFrameworkNamespaces.Contains(include, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (alreadyImportsXunit)
            {
                element.Remove();
                continue;
            }

            element.SetAttributeValue("Include", "Xunit");
            alreadyImportsXunit = true;
        }
    }

    private XElement BuildItemGroup(XNamespace ns)
    {
        return new XElement(
            ns + "ItemGroup",
            XunitPackages.Select(p => new XElement(
                ns + "PackageReference",
                new XAttribute("Include", p.Id),
                new XAttribute("Version", p.Version))));
    }

    private static string GetPackageId(XElement packageReference)
    {
        // Central package management uses Update/Remove instead of Include.
        return packageReference.Attribute("Include")?.Value
               ?? packageReference.Attribute("Update")?.Value
               ?? packageReference.Attribute("Remove")?.Value
               ?? string.Empty;
    }

    /// <summary>
    /// Whether the project references any of the test-framework packages above.
    /// </summary>
    /// <remarks>
    /// The file-level detectors are substring sniffs, so a library that merely mentions "[TestFixture]" or
    /// "NUnit.Framework" in a string literal looks exactly like a test project to them - this converter's own
    /// sources among them. Asking the csproj instead is the reliable signal, and it is what keeps a
    /// solution-wide run from rewriting ordinary libraries.
    /// </remarks>
    public bool ReferencesTestPackages(string csprojPath)
    {
        var doc = XDocument.Parse(File.ReadAllText(csprojPath));

        return doc
            .Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .Any(e => IsTestPackage(GetPackageId(e)));
    }

    private static bool IsTestPackage(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return false;
        }

        return TestPackageIds.Contains(packageId, StringComparer.OrdinalIgnoreCase)
               || TestPackageIdPrefixes.Any(prefix =>
                   packageId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string Serialize(XDocument doc)
    {
        var builder = new StringBuilder();

        if (doc.Declaration != null)
        {
            builder.Append(doc.Declaration).Append(System.Environment.NewLine);
        }

        builder.Append(doc);
        return builder.ToString();
    }
}
