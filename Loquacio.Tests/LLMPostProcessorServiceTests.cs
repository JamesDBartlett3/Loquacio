using Loquacio.Services;

namespace Loquacio.Tests;

/// <summary>
/// Tests for the LLMPostProcessorService filler word removal and text cleanup logic.
/// These test the pure (non-LLM) text processing methods that don't require network access.
/// </summary>
public class LLMPostProcessorServiceTests
{
    [Theory]
    [InlineData("hello world", "hello world")]
    [InlineData("", "")]
    [InlineData(null!, "")]
    [InlineData("um hello world", "hello world")]
    [InlineData("hello um world", "hello world")]
    [InlineData("hello world um", "hello world")]
    [InlineData("um uh hello um uh world", "hello world")]
    [InlineData("Hello, um, world", "Hello, world")]
    [InlineData("uh um ehm ah er hmm", "")]
    public void RemoveFillerWords_BasicCases(string? input, string expected)
    {
        if (input is null)
        {
            Assert.Null(LLMPostProcessorService.RemoveFillerWords(input!));
            return;
        }
        var result = LLMPostProcessorService.RemoveFillerWords(input);
        Assert.Equal(expected.Trim(), result);
    }

    [Theory]
    [InlineData("UM HELLO", "HELLO")]
    [InlineData("Um, hello Uh world", "hello world")]
    [InlineData("HELLO um WORLD", "HELLO WORLD")]
    public void RemoveFillerWords_CaseInsensitive(string input, string expected)
    {
        var result = LLMPostProcessorService.RemoveFillerWords(input);
        Assert.Equal(expected.Trim(), result);
    }

    [Fact]
    public void RemoveFillerWords_MultiWordPhrases()
    {
        var result = LLMPostProcessorService.RemoveFillerWords("hello you know world");
        // "you know" should be removed
        Assert.DoesNotContain("you know", result);
        Assert.Contains("hello", result);
        Assert.Contains("world", result);
    }

    [Fact]
    public void RemoveFillerWords_PreservesWordBoundaries()
    {
        var input = "um the quick brown fox jumps over the lazy dog uh";
        var result = LLMPostProcessorService.RemoveFillerWords(input);

        Assert.Contains("quick", result);
        Assert.Contains("brown", result);
        Assert.Contains("fox", result);
        Assert.Contains("jumps", result);
        // Standalone filler words should be gone, but substrings inside real words remain
        Assert.DoesNotContain(" um ", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" uh ", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemoveFillerWords_HandlesConsecutiveFillers()
    {
        var input = "um uh er hello um ah";
        var result = LLMPostProcessorService.RemoveFillerWords(input);

        Assert.Contains("hello", result);
        Assert.DoesNotContain(" um ", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" uh ", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemoveFillerWords_CleansWhitespaceAfterRemoval()
    {
        var input = "hello um     world";
        var result = LLMPostProcessorService.RemoveFillerWords(input);

        Assert.DoesNotContain("  ", result);
    }

    [Fact]
    public void RemoveFillerWords_DoesNotCapitalizeAfterPeriod()
    {
        // RemoveFillerWords should NOT change capitalization — that's a post-processing concern
        var input = "hello world. um this is a test";
        var result = LLMPostProcessorService.RemoveFillerWords(input);

        Assert.Contains("this is a test", result);
        Assert.DoesNotContain("This", result);
    }

    [Fact]
    public void RemoveFillerWords_DoesNotCapitalizeFirstLetter()
    {
        // RemoveFillerWords should NOT change capitalization — that's a post-processing concern
        var input = "um hello world";
        var result = LLMPostProcessorService.RemoveFillerWords(input);

        Assert.StartsWith("hello", result);
    }

    [Fact]
    public void RemoveFillerWords_FixesSpaceBeforePunctuation()
    {
        var input = "hello , world . test";
        var result = LLMPostProcessorService.RemoveFillerWords(input);

        Assert.Contains("hello, world. test", result);
    }

    [Fact]
    public void RemoveFillerWords_DoesNotRemoveSubstringMatches()
    {
        var input = "humble bumble umble";
        var result = LLMPostProcessorService.RemoveFillerWords(input);

        // "um" inside words should not be removed
        Assert.Contains("humble", result);
        Assert.Contains("bumble", result);
        Assert.Contains("umble", result);
    }

    [Fact]
    public void RemoveFillerWords_ComplexTranscription_CheckFillerRemoval()
    {
        var input = "um so I was thinking uh that we should, you know, um maybe like sort of try the new approach";
        var result = LLMPostProcessorService.RemoveFillerWords(input);

        // Should remove fillers but preserve meaning
        Assert.Contains("thinking", result);
        Assert.Contains("approach", result);
        // Check standalone fillers are gone (not substrings)
        Assert.DoesNotContain(" um ", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" uh ", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemoveFillerWords_AllFillerWords()
    {
        var input = "um uh ehm erm ah er hmm mm mhm";
        var result = LLMPostProcessorService.RemoveFillerWords(input);

        Assert.True(string.IsNullOrWhiteSpace(result));
    }

    [Theory]
    [InlineData("like that was good")]
    public void RemoveFillerWords_RemovesLikeAsFiller(string input)
    {
        var result = LLMPostProcessorService.RemoveFillerWords(input);
        // "like" is in the filler list
        Assert.DoesNotContain("like", result, StringComparison.OrdinalIgnoreCase);
    }
}
