using FluentAssertions;

using MeetingRecorder.Domain;
using MeetingRecorder.Infrastructure.Audio;
using MeetingRecorder.Infrastructure.Wasapi;

using NAudio.Wave;

namespace MeetingRecorder.Infrastructure.Tests;

[Trait("Category", "Unit")]
public sealed class InputFormatTests
{
    [Theory(DisplayName = "Signed and unsigned PCM decode negative full scale with headroom")]
    [InlineData(8, "00")]
    [InlineData(16, "0080")]
    [InlineData(24, "000080")]
    [InlineData(32, "00000080")]
    public void PcmShouldPreserveSign(int bits, string sample)
    {
        var format = WasapiFormatMapper.FromMixFormat(new WaveFormat(44100, bits, 1));
        format.Rate.Should().Be(44100);
        format.Stride.Should().Be(bits / 8);
        format.Decode(Convert.FromHexString(sample)).Should().BeApproximately(-0.48f, 0.00001f);
    }

    [Theory(DisplayName = "Floating-point devices suppress nonfinite values and bound peaks")]
    [InlineData(32, double.NaN, 0)]
    [InlineData(64, double.PositiveInfinity, 0)]
    [InlineData(64, -2, -0.48)]
    [InlineData(32, 2, 0.48)]
    public void FloatShouldHandleOutOfRangeSamples(int bits, double value, float expected)
    {
        var wave = WaveFormat.CreateCustomFormat(WaveFormatEncoding.IeeeFloat, 48000,
            1, 48000 * (bits / 8), bits / 8, bits);
        var format = WasapiFormatMapper.FromMixFormat(wave);
        var data = bits == 32 ? BitConverter.GetBytes((float)value) : BitConverter.GetBytes(value);
        format.Decode(data).Should().BeApproximately(expected, 0.00001f);
    }

    [Theory(DisplayName = "WAVEFORMATEXTENSIBLE negotiates PCM and float and honors the LFE mask")]
    [InlineData(16)]
    [InlineData(32)]
    public void ExtensibleFormatShouldHonorChannelRole(int bits)
    {
        var wave = new WaveFormatExtensible(48000, bits, 2, 12);
        var format = WasapiFormatMapper.FromMixFormat(wave);
        var frame = bits == 16 ? Convert.FromHexString("FF7FFF7F")
            : Convert.FromHexString("0000803F0000803F");
        format.Decode(frame).Should().BeApproximately(0.48f, 0.0001f);
        Array.Clear(frame, 0, bits / 8);
        format.Decode(frame).Should().Be(0);
    }

    [Theory(DisplayName = "Unsupported layouts fail before capture starts")]
    [InlineData(7000, 1, 16, false)]
    [InlineData(48000, 33, 16, false)]
    [InlineData(48000, 2, 20, false)]
    [InlineData(48000, 1, 16, true)]
    public void UnsupportedLayoutShouldFail(int rate, int channels, int bits, bool floating)
    {
        var open = () => new InputFormat(rate, channels, bits, floating, 0);
        open.Should().Throw<RecordingException>().WithMessage("*unsupported*");
    }

    [Fact(DisplayName = "Compressed input and inconsistent block alignment are rejected")]
    public void MalformedMixFormatShouldFail()
    {
        var compressed = () => WasapiFormatMapper.FromMixFormat(WaveFormat.CreateALawFormat(8000, 1));
        var misaligned = () => WasapiFormatMapper.FromMixFormat(
            WaveFormat.CreateCustomFormat(WaveFormatEncoding.Pcm, 48000, 2, 192000, 2, 16));
        compressed.Should().Throw<RecordingException>();
        misaligned.Should().Throw<RecordingException>();
    }
}
