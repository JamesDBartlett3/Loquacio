using System.Xml.Linq;
using Xunit;

namespace Loquacio.Tests.Packaging;

/// <summary>
/// Phase 6.5 — validates Package.appxmanifest structure on any OS (no Windows SDK needed).
/// Guards against regressions in the MSIX packaging setup until a Windows host can
/// produce a real .msix (see build-msix.ps1 / packaging/README.md).
/// </summary>
public class AppxManifestTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Loquacio.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (Loquacio.slnx) not found above test output dir");
    }

    private static XDocument LoadManifest()
    {
        var path = Path.Combine(RepoRoot, "Package.appxmanifest");
        return XDocument.Load(path); // throws if missing or malformed
    }

    [Fact]
    public void Manifest_IsWellFormedXml()
    {
        var doc = LoadManifest();
        Assert.Equal("Package", doc.Root?.Name.LocalName);
    }

    [Fact]
    public void Identity_HasNamePublisherAndValidVersion()
    {
        var identity = LoadManifest().Root!.Elements()
            .First(e => e.Name.LocalName == "Identity");
        Assert.Equal("Innovation.Loquacio", (string?)identity.Attribute("Name"));
        Assert.StartsWith("CN=", (string?)identity.Attribute("Publisher"));
        var version = (string?)identity.Attribute("Version");
        Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$", version!);
    }

    [Fact]
    public void Application_DeclaresStartupTaskExtension()
    {
        var doc = LoadManifest();
        var ns = doc.Root!.GetDefaultNamespace();
        var app = doc.Descendants(ns + "Application").Single();
        var ext = app.Descendants().FirstOrDefault(e =>
            e.Name.LocalName == "Extension" && (string?)e.Attribute("Category") == "windows.startupTask");
        Assert.NotNull(ext);
        var uapNs = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
        Assert.Equal("LoquacioStartup", (string?)ext?.Element(XName.Get("StartupTask", uapNs))?.Attribute("TaskId"));
    }

    [Fact]
    public void Capabilities_DeclareOnlyExpectedSet()
    {
        var doc = LoadManifest();
        var caps = doc.Descendants().Where(e => e.Name.LocalName == "Capability")
            .Select(e => (string?)e.Attribute("Name")).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "backgroundMediaRecording", "internetClient" }, caps);
    }

    [Fact]
    public void AllReferencedPngAssets_ExistOnDisk()
    {
        // Collect every *.png path referenced anywhere in the manifest and verify it exists.
        var doc = LoadManifest();
        var refs = doc.Descendants().Attributes()
            .Where(a => a.Name.LocalName != "IgnorableNamespaces"
                        && a.Value.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Value).Distinct().ToList();
        Assert.NotEmpty(refs);
        foreach (var rel in refs)
        {
            var path = Path.Combine(RepoRoot, rel.Replace('\\', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "Manifest references missing asset: " + rel);
        }
    }
}
