using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Loquacio.Tests.Packaging;

/// <summary>
/// Validates the MSIX AppxManifest for the Avalonia app (packaging/windows/msix/).
/// These checks run on Linux so manifest regressions are caught without a Windows host.
/// </summary>
public class MsixManifestTests
{
    private static readonly XNamespace Pkg = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace Uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Loquacio.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found from test bin dir");
    }

    private static XDocument LoadManifest()
    {
        var path = Path.Combine(FindRepoRoot(), "packaging", "windows", "msix", "AppxManifest.xml");
        Assert.True(File.Exists(path), $"AppxManifest.xml not found at {path}");
        return XDocument.Load(path, LoadOptions.None);
    }

    [Fact]
    public void Manifest_IsWellFormedXml() => LoadManifest();

    [Fact]
    public void Manifest_HasIdentityWithPublisherAndVersion()
    {
        var identity = LoadManifest().Root!.Element(Pkg + "Identity");
        Assert.NotNull(identity);
        Assert.Equal("Innovation.Loquacio", identity!.Attribute("Name")!.Value);
        Assert.Equal("CN=Loquacio", identity.Attribute("Publisher")!.Value);
        Assert.Matches("^\\d+\\.\\d+\\.\\d+\\.\\d+$", identity.Attribute("Version")!.Value);
    }

    [Fact]
    public void Manifest_TargetsDesktopDeviceFamily()
    {
        var families = LoadManifest().Descendants(Pkg + "TargetDeviceFamily")
            .Select(e => e.Attribute("Name")!.Value).ToList();
        Assert.Contains("Windows.Desktop", families);
    }

    [Fact]
    public void Manifest_ApplicationPointsAtAvaloniaExecutableWithFullTrust()
    {
        var app = LoadManifest().Descendants(Pkg + "Application").Single();
        Assert.Equal("loquacio-avalonia.exe", app.Attribute("Executable")!.Value);
        Assert.Equal("Windows.FullTrustApplication", app.Attribute("EntryPoint")!.Value);
    }

    [Fact]
    public void Manifest_DeclaresRunFullTrustCapability()
    {
        var caps = LoadManifest().Descendants()
            .Where(e => e.Name.LocalName == "Capability")
            .Select(e => e.Attribute("Name")!.Value).ToList();
        Assert.Contains("runFullTrust", caps);
    }

    [Fact]
    public void Manifest_DeclaresMicrophoneDeviceCapability()
    {
        var caps = LoadManifest().Descendants(Pkg + "DeviceCapability")
            .Select(e => e.Attribute("Name")!.Value).ToList();
        Assert.Contains("microphone", caps);
    }

    [Fact]
    public void Manifest_HasStartupTaskExtension()
    {
        var tasks = LoadManifest().Descendants(Uap + "StartupTask").ToList();
        Assert.Single(tasks);
        Assert.Equal("LoquacioStartup", tasks[0].Attribute("TaskId")!.Value);
    }

    [Theory]
    [InlineData("StoreLogo.png")]
    [InlineData("Square44x44Logo.png")]
    [InlineData("Square150x150Logo.png")]
    [InlineData("Wide310x150Logo.png")]
    [InlineData("SmallTile.png")]
    [InlineData("LargeTile.png")]
    [InlineData("SplashScreen.png")]
    public void Manifest_ReferencedAssets_ExistWithExpectedFormat(string fileName)
    {
        var manifestText = LoadManifest().ToString();
        Assert.Contains(fileName, manifestText);

        var assetPath = Path.Combine(FindRepoRoot(), "packaging", "windows", "msix", "assets", fileName);
        Assert.True(File.Exists(assetPath), $"Referenced asset missing: {fileName}");

        // PNG magic bytes
        var header = File.ReadAllBytes(assetPath)[..8];
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, header);
    }

    [Fact]
    public void PackageScript_ExistsAndMentionsMakeAppx()
    {
        var script = Path.Combine(FindRepoRoot(), "scripts", "package-msix.ps1");
        Assert.True(File.Exists(script), "scripts/package-msix.ps1 missing");
        var text = File.ReadAllText(script);
        Assert.Contains("MakeAppx", text);
        Assert.Contains("dotnet publish", text);
    }
}
