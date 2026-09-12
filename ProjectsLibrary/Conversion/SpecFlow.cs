using System.Text.RegularExpressions;

namespace ProjectsLibrary.Conversion;

/// <summary>
/// What the converter needs to know about SpecFlow.
/// </summary>
/// <remarks>
/// SpecFlow is not a unit test framework; it runs on top of one. A SpecFlow project is therefore retargeted
/// rather than converted: the provider package and the configured provider name change, while the Gherkin, the
/// <c>[Binding]</c> classes and the hooks are framework-agnostic and stay exactly as they are.
/// </remarks>
public static class SpecFlow
{
    /// <summary>The provider package every SpecFlow project ends up with.</summary>
    public const string XunitProvider = "SpecFlow.xUnit";

    /// <summary>Provider packages that are replaced by <see cref="XunitProvider"/>, keeping their version.</summary>
    public static readonly string[] ProviderPackages =
    [
        "SpecFlow.NUnit",
        "SpecFlow.NUnit.Runners",
        "SpecFlow.MsTest"
    ];

    /// <summary>
    /// Package ids that mark a project as a SpecFlow project. Used to decide whether a project is worth
    /// scanning at all - not to decide what to remove, since SpecFlow itself must survive the conversion.
    /// </summary>
    public static bool IsSpecFlowPackage(string packageId)
    {
        return packageId.Equals("SpecFlow", StringComparison.OrdinalIgnoreCase)
               || packageId.StartsWith("SpecFlow.", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The xUnit provider for a package id, or <see langword="null"/> if it is not a provider.</summary>
    public static string? RetargetedProvider(string packageId)
    {
        return ProviderPackages.Contains(packageId, StringComparer.OrdinalIgnoreCase)
            ? XunitProvider
            : null;
    }

    /// <summary>
    /// Whether a file is SpecFlow's generated code-behind for a feature.
    /// </summary>
    /// <remarks>
    /// Generated from the <c>.feature</c> file on every build, so rewriting it is pointless - the next build
    /// discards the change - and leaving a stale one shaped for the old provider is what actually breaks the
    /// build after a retarget.
    /// </remarks>
    public static bool IsGeneratedFeatureCode(string path)
    {
        return path.EndsWith(".feature.cs", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Points a <c>specflow.json</c> at the xUnit provider, or returns <see langword="null"/> when there is
    /// nothing to change.
    /// </summary>
    /// <remarks>
    /// Edited as text rather than reparsed as JSON so the file keeps its formatting, its key order and any
    /// comments. Only the provider name is touched; every other setting is left exactly where it was.
    /// </remarks>
    public static string? RetargetJson(string json)
    {
        var match = ProviderName.Match(json);

        if (!match.Success)
            return null;

        var name = match.Groups["name"];

        if (name.Value.Equals("xunit", StringComparison.OrdinalIgnoreCase))
            return null;

        return json[..name.Index] + "xunit" + json[(name.Index + name.Length)..];
    }

    /// <summary>The <c>"name"</c> of the <c>unitTestProvider</c> object, wherever it sits in the file.</summary>
    private static readonly Regex ProviderName = new(
        @"""unitTestProvider""\s*:\s*\{[^}]*?""name""\s*:\s*""(?<name>[^""]*)""",
        RegexOptions.Compiled | RegexOptions.Singleline);
}
