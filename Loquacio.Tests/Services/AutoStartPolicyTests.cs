using Loquacio.Services;

namespace Loquacio.Tests.Services;

public class AutoStartPolicyTests
{
    [Theory]
    [InlineData(true, true, null)]
    [InlineData(false, false, null)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    public void ComputeTransition_MapsStates(bool desired, bool current, bool? expected)
    {
        Assert.Equal(expected, AutoStartPolicy.ComputeTransition(desired, current));
    }

    [Fact]
    public void BuildRunValue_QuotesPlainPath()
    {
        Assert.Equal("\"C:\\Apps\\Loquacio.exe\"", AutoStartPolicy.BuildRunValue("C:\\Apps\\Loquacio.exe"));
    }

    [Fact]
    public void BuildRunValue_StripsSurroundingQuotesAndWhitespace()
    {
        Assert.Equal("\"C:\\Apps\\Loquacio.exe\"", AutoStartPolicy.BuildRunValue("  \"C:\\Apps\\Loquacio.exe\"  "));
    }

    [Fact]
    public void BuildRunValue_EmptyPath_IsStillQuotedEmpty()
    {
        Assert.Equal("\"\"", AutoStartPolicy.BuildRunValue(""));
        Assert.Equal("\"\"", AutoStartPolicy.BuildRunValue(null!));
    }

    [Fact]
    public void Constants_UseExpectedRunKeyAndValueName()
    {
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Run", AutoStartPolicy.RunKeyPath);
        Assert.Equal("Loquacio", AutoStartPolicy.ValueName);
    }

    /// <summary>On Windows the registry is really touched, so only run off-Windows.</summary>
    [Fact]
    public void RegistryAutoStartService_IsNoOpOffWindows()
    {
        if (OperatingSystem.IsWindows()) return; // covered by manual Windows verification
        var svc = new RegistryAutoStartService();
        Assert.False(svc.IsEnabled);
        Assert.False(svc.Enable());
        Assert.False(svc.Disable());
        Assert.False(svc.Toggle());
    }
}
