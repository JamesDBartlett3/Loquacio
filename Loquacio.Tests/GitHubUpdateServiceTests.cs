using System.Security.Cryptography;
using System.Text;
using Loquacio.Engine.Services;
using static Loquacio.Engine.Services.GitHubUpdateService;

namespace Loquacio.Tests;

public class GitHubUpdateServiceTests
{
    [Fact]
    public void IsNewerVersion_Version10_GreaterThan_Version09()
    {
        // Arrange
        var latestVersion = "0.10.0";
        var currentVersion = "0.9.0";

        // Act
        var result = GitHubUpdateService.IsNewerVersion(latestVersion, currentVersion);

        // Assert
        Assert.True(result, "0.10.0 should be greater than 0.9.0");
    }

    [Fact]
    public void IsNewerVersion_Version09_NotGreaterThan_Version10()
    {
        // Arrange
        var latestVersion = "0.9.0";
        var currentVersion = "0.10.0";

        // Act
        var result = GitHubUpdateService.IsNewerVersion(latestVersion, currentVersion);

        // Assert
        Assert.False(result, "0.9.0 should not be greater than 0.10.0");
    }

    [Fact]
    public void IsNewerVersion_WithVPrefix_WorksCorrectly()
    {
        // Arrange
        var latestVersion = "v1.0.0";
        var currentVersion = "v0.9.0";

        // Act
        var result = GitHubUpdateService.IsNewerVersion(latestVersion, currentVersion);

        // Assert
        Assert.True(result, "v1.0.0 should be greater than v0.9.0");
    }

    [Fact]
    public void IsNewerVersion_SameVersion_ReturnsFalse()
    {
        // Arrange
        var latestVersion = "1.0.0";
        var currentVersion = "1.0.0";

        // Act
        var result = GitHubUpdateService.IsNewerVersion(latestVersion, currentVersion);

        // Assert
        Assert.False(result, "Same versions should return false");
    }

    [Fact]
    public void IsNewerVersion_LowerVersion_ReturnsFalse()
    {
        // Arrange
        var latestVersion = "0.8.0";
        var currentVersion = "0.9.0";

        // Act
        var result = GitHubUpdateService.IsNewerVersion(latestVersion, currentVersion);

        // Assert
        Assert.False(result, "Lower version should return false");
    }

    [Fact]
    public void IsNewerVersion_InvalidVersion_FallsBackToStringComparison()
    {
        // Arrange
        var latestVersion = "invalid";
        var currentVersion = "0.9.0";

        // Act
        var result = GitHubUpdateService.IsNewerVersion(latestVersion, currentVersion);

        // Assert - should not throw, fallback to string comparison
        // "invalid" > "0.9.0" lexicographically, so this is expected behavior
        Assert.True(result);
    }

    [Theory]
    [InlineData("a1b2c3d4e5f6789012345678901234567890abcdef1234567890abcdef123456  myfile.zip", "a1b2c3d4e5f6789012345678901234567890abcdef1234567890abcdef123456")]
    [InlineData("a1b2c3d4e5f6789012345678901234567890abcdef1234567890abcdef123456\tmyfile.zip", "a1b2c3d4e5f6789012345678901234567890abcdef1234567890abcdef123456")]
    [InlineData("  a1b2c3d4e5f6789012345678901234567890abcdef1234567890abcdef123456  myfile.zip  ", "a1b2c3d4e5f6789012345678901234567890abcdef1234567890abcdef123456")]
    [InlineData("a1b2c3d4e5f6789012345678901234567890abcdef1234567890abcdef123456\nmyfile.zip", "a1b2c3d4e5f6789012345678901234567890abcdef1234567890abcdef123456")]
    public void ParseChecksumFromContent_ValidFormats_ReturnsHash(string content, string expectedHash)
    {
        // Act
        var result = GitHubUpdateService.ParseChecksumFromContent(content, "myfile.zip");

        // Assert
        Assert.Equal(expectedHash, result);
    }

    [Fact]
    public void ParseChecksumFromContent_MultipleLines_ReturnsFirstValidHash()
    {
        // Arrange
        var content = @"a1b2c3d4e5f6789012345678901234567890abcdef1234567890abcdef123456  file1.zip
d4e5f6a1b2c3789012345678901234567890abcdef1234567890abcdef123456  file2.zip
7890abcdef123456a1b2c3d4e5f6789012345678901234567890abcdef123456  file3.zip";

        // Act
        var result = GitHubUpdateService.ParseChecksumFromContent(content, "file1.zip");

        // Assert
        Assert.Equal("a1b2c3d4e5f6789012345678901234567890abcdef1234567890abcdef123456", result);
    }

    [Fact]
    public void ParseChecksumFromContent_InvalidHash_ReturnsNull()
    {
        // Arrange
        var content = "not-a-valid-hash  file.zip";

        // Act
        var result = GitHubUpdateService.ParseChecksumFromContent(content, "file.zip");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ParseChecksumFromContent_EmptyContent_ReturnsNull()
    {
        // Act
        var result = GitHubUpdateService.ParseChecksumFromContent("", "file.zip");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ComputeFileHashAsync_ValidFile_ReturnsCorrectSha256()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            var content = Encoding.UTF8.GetBytes("Hello, World!");
            await File.WriteAllBytesAsync(tempFile, content);

            // Compute expected hash
            using var sha256 = SHA256.Create();
            var expectedHash = Convert.ToHexString(sha256.ComputeHash(content)).ToLowerInvariant();

            // Act
            var result = await GitHubUpdateService.ComputeFileHashAsync(tempFile, CancellationToken.None);

            // Assert
            Assert.Equal(expectedHash, result);
            Assert.Equal(64, result.Length); // SHA256 produces 64 hex characters
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task ComputeFileHashAsync_SameContent_SameHash()
    {
        // Arrange
        var tempFile1 = Path.GetTempFileName();
        var tempFile2 = Path.GetTempFileName();
        try
        {
            var content = Encoding.UTF8.GetBytes("Test content for hash comparison");
            await File.WriteAllBytesAsync(tempFile1, content);
            await File.WriteAllBytesAsync(tempFile2, content);

            // Act
            var hash1 = await GitHubUpdateService.ComputeFileHashAsync(tempFile1, CancellationToken.None);
            var hash2 = await GitHubUpdateService.ComputeFileHashAsync(tempFile2, CancellationToken.None);

            // Assert
            Assert.Equal(hash1, hash2);
        }
        finally
        {
            if (File.Exists(tempFile1)) File.Delete(tempFile1);
            if (File.Exists(tempFile2)) File.Delete(tempFile2);
        }
    }

    [Fact]
    public async Task ComputeFileHashAsync_DifferentContent_DifferentHash()
    {
        // Arrange
        var tempFile1 = Path.GetTempFileName();
        var tempFile2 = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile1, "Content A");
            await File.WriteAllTextAsync(tempFile2, "Content B");

            // Act
            var hash1 = await GitHubUpdateService.ComputeFileHashAsync(tempFile1, CancellationToken.None);
            var hash2 = await GitHubUpdateService.ComputeFileHashAsync(tempFile2, CancellationToken.None);

            // Assert
            Assert.NotEqual(hash1, hash2);
        }
        finally
        {
            if (File.Exists(tempFile1)) File.Delete(tempFile1);
            if (File.Exists(tempFile2)) File.Delete(tempFile2);
        }
    }

    [Fact]
    public void FindChecksumAsset_MatchingSha256File_ReturnsAsset()
    {
        // Arrange
        var targetAsset = new GitHubAsset("app.zip", "https://example.com/app.zip", 1000);
        var checksumAsset = new GitHubAsset("app.zip.sha256", "https://example.com/app.zip.sha256", 64);
        var assets = new List<GitHubAsset> { targetAsset, checksumAsset };

        // Act
        var result = GitHubUpdateService.FindChecksumAsset(assets, targetAsset);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("app.zip.sha256", result.Name);
    }

    [Fact]
    public void FindChecksumAsset_NoMatchingChecksum_ReturnsNull()
    {
        // Arrange
        var targetAsset = new GitHubAsset("app.zip", "https://example.com/app.zip", 1000);
        var otherAsset = new GitHubAsset("completely-different.txt.sha256", "https://example.com/completely-different.txt.sha256", 64);
        var assets = new List<GitHubAsset> { targetAsset, otherAsset };

        // Act
        var result = GitHubUpdateService.FindChecksumAsset(assets, targetAsset);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FindChecksumAsset_CaseInsensitiveMatching_Works()
    {
        // Arrange
        var targetAsset = new GitHubAsset("APP.ZIP", "https://example.com/app.zip", 1000);
        var checksumAsset = new GitHubAsset("app.zip.sha256", "https://example.com/app.zip.sha256", 64);
        var assets = new List<GitHubAsset> { targetAsset, checksumAsset };

        // Act
        var result = GitHubUpdateService.FindChecksumAsset(assets, targetAsset);

        // Assert
        Assert.NotNull(result);
    }
}
