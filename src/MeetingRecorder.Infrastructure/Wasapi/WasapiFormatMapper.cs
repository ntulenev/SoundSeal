using System.Runtime.InteropServices;

using MeetingRecorder.Domain;
using MeetingRecorder.Infrastructure.Audio;

using NAudio.Wave;

namespace MeetingRecorder.Infrastructure.Wasapi;

/// <summary>Validates the WASAPI mix format before either endpoint enters capture.</summary>
internal static class WasapiFormatMapper
{
    internal static InputFormat FromMixFormat(WaveFormat wave)
    {
        var encoding = wave.Encoding;
        var mask = 0;
        if (wave is WaveFormatExtensible extended)
        {
            // WAVEFORMATEXTENSIBLE.dwChannelMask follows WAVEFORMATEX and valid bits.
            var pointer = WaveFormat.MarshalToPtr(extended);
            try
            {
                mask = Marshal.ReadInt32(pointer, 20);
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }

            encoding = extended.SubFormat.ToString() switch
            {
                "00000001-0000-0010-8000-00aa00389b71" => WaveFormatEncoding.Pcm,
                "00000003-0000-0010-8000-00aa00389b71" => WaveFormatEncoding.IeeeFloat,
                _ => WaveFormatEncoding.Unknown
            };
        }

        if (encoding is not (WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat)
            || wave.BlockAlign != wave.Channels * (wave.BitsPerSample / 8))
        {
            throw new RecordingException("The selected device uses an unsupported audio format.");
        }

        return new(wave.SampleRate, wave.Channels, wave.BitsPerSample,
            encoding == WaveFormatEncoding.IeeeFloat, mask);
    }
}
