using MeetingRecorder.Domain;
using MeetingRecorder.Infrastructure.Audio;

using Microsoft.Extensions.Configuration;

namespace MeetingRecorder.Infrastructure.Tests;

/// <summary>Tests audio device configuration.</summary>
[Trait("Category", "Unit")]
public sealed class AudioCaptureOptionsTests
{
    /// <summary>Invalid capture settings are rejected during composition.</summary>
    [Theory(DisplayName = "Invalid capture settings are rejected during composition")]
    [InlineData("MinimumSampleRate", "0")]
    [InlineData("MaximumSampleRate", "7999")]
    [InlineData("MaximumChannels", "-1")]
    [InlineData("MaximumChannels", "33")]
    [InlineData("QueueCapacity", "0")]
    public void LimitsMustBePositiveAndOrdered(string key, string value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Read(key, value));

    /// <summary>A malformed value identifies its configuration key.</summary>
    [Theory(DisplayName = "A malformed value identifies its configuration key")]
    [InlineData("QueueCapacity", "many")]
    [InlineData("AllowedEncodings", "Pcm20")]
    [InlineData("AllowedEncodings", "99")]
    [InlineData("AllowedEncodings", "1")]
    [InlineData("AllowedEncodings", "")]
    public void MalformedSettingsIdentifyTheKey(string key, string value)
    {
        var error = Assert.Throws<FormatException>(() => Read(key, value));
        Assert.Contains(key, error.Message, StringComparison.Ordinal);
    }

    /// <summary>Configured limits narrow the layouts accepted by the actual decoder.</summary>
    [Fact(DisplayName = "Configured limits narrow the layouts accepted by the actual decoder")]
    public void NegotiationHonorsConfiguredEncodingsAndRates()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AudioCapture:MinimumSampleRate"] = "16000",
            ["AudioCapture:MaximumSampleRate"] = "48000",
            ["AudioCapture:MaximumChannels"] = "2",
            ["AudioCapture:QueueCapacity"] = "8",
            ["AudioCapture:AllowedEncodings"] = "pcm16, Float32"
        }).Build();
        var options = AudioCaptureOptions.Read(config);
        Assert.Equal(8, options.QueueCapacity);
        Assert.Equal(4, new InputFormat(16000, 2, 16, false, 0, options).Stride);
        Assert.Equal(4, new InputFormat(48000, 1, 32, true, 0, options).Stride);
        Assert.Throws<RecordingException>(() => new InputFormat(8000, 1, 16, false, 0, options));
        Assert.Throws<RecordingException>(() => new InputFormat(96000, 1, 16, false, 0, options));
        Assert.Throws<RecordingException>(() => new InputFormat(44100, 3, 16, false, 0, options));
        Assert.Throws<RecordingException>(() => new InputFormat(44100, 2, 64, true, 0, options));
    }

    private static AudioCaptureOptions Read(string key, string value) =>
        AudioCaptureOptions.Read(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["AudioCapture:" + key] = value }).Build());
}
