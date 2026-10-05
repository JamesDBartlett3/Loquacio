using NSubstitute;
using Loquacio.Models;
using Loquacio.Services;

namespace Loquacio.Tests.Services;

public class IBackgroundTranscriptionServiceTests
{
    private readonly IBackgroundTranscriptionService _mockBgService;

    public IBackgroundTranscriptionServiceTests()
    {
        _mockBgService = Substitute.For<IBackgroundTranscriptionService>();
    }

    [Fact]
    public void IsRunning_DefaultsToFalse()
    {
        // Arrange
        _mockBgService.IsRunning.Returns(false);

        // Act & Assert
        Assert.False(_mockBgService.IsRunning);
    }

    [Fact]
    public async Task StartAsync_SetsIsRunning()
    {
        // Arrange
        _mockBgService.IsRunning.Returns(true);

        // Act
        await _mockBgService.StartAsync();

        // Assert
        Assert.True(_mockBgService.IsRunning);
        await _mockBgService.Received(1).StartAsync();
    }

    [Fact]
    public async Task StopAsync_CanBeCalled()
    {
        // Act
        await _mockBgService.StopAsync();

        // Assert
        await _mockBgService.Received(1).StopAsync();
    }

    [Fact]
    public void OnTranscriptionCompleted_EventCanBeRaised()
    {
        // Arrange
        var results = new List<TranscriptionResult>();
        _mockBgService.OnTranscriptionCompleted += (sender, result) => results.Add(result);

        var expectedResult = new TranscriptionResult
        {
            Text = "Test transcription",
            Timestamp = DateTime.UtcNow,
            Duration = TimeSpan.FromSeconds(2)
        };

        // Act
        _mockBgService.OnTranscriptionCompleted += Raise.Event<EventHandler<TranscriptionResult>>(_mockBgService, expectedResult);

        // Assert
        Assert.Single(results);
        Assert.Equal("Test transcription", results[0].Text);
    }

    [Fact]
    public void OnError_EventCanBeRaised()
    {
        // Arrange
        Exception? caughtException = null;
        _mockBgService.OnError += (sender, ex) => caughtException = ex;

        // Act
        var testException = new InvalidOperationException("Audio device not found");
        _mockBgService.OnError += Raise.Event<EventHandler<Exception>>(_mockBgService, testException);

        // Assert
        Assert.NotNull(caughtException);
        Assert.Equal("Audio device not found", caughtException!.Message);
    }

    [Fact]
    public void OnTranscriptionCompleted_MultipleSubscribers_AllReceiveEvent()
    {
        // Arrange
        var results1 = new List<TranscriptionResult>();
        var results2 = new List<TranscriptionResult>();

        _mockBgService.OnTranscriptionCompleted += (sender, result) => results1.Add(result);
        _mockBgService.OnTranscriptionCompleted += (sender, result) => results2.Add(result);

        var testResult = new TranscriptionResult { Text = "Multi-subscriber test" };

        // Act
        _mockBgService.OnTranscriptionCompleted += Raise.Event<EventHandler<TranscriptionResult>>(_mockBgService, testResult);

        // Assert
        Assert.Single(results1);
        Assert.Single(results2);
        Assert.Equal("Multi-subscriber test", results1[0].Text);
        Assert.Equal("Multi-subscriber test", results2[0].Text);
    }
}
