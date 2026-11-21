using System.Runtime.InteropServices;

using MeetingRecorder.Domain;
using MeetingRecorder.Infrastructure.Audio;

using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetingRecorder.Infrastructure.Wasapi;

/// <summary>One shared-mode WASAPI stream. All operations run on its owning capture thread.</summary>
internal interface IInputEndpoint : IDisposable
{
    TimeSpan EnginePeriod { get; }
    void Start();
    void Drain(Action<AudioPacket> accept);
}

internal sealed class WasapiEndpoint : IInputEndpoint
{
    private readonly AudioClient _client;
    private readonly InputFormat _format;
    private readonly bool _loopback;
    private bool _started;

    private WasapiEndpoint(AudioClient client, InputFormat format, bool loopback,
        TimeSpan enginePeriod)
    {
        _client = client;
        _format = format;
        _loopback = loopback;
        EnginePeriod = enginePeriod;
    }

    public TimeSpan EnginePeriod { get; }

    internal static IInputEndpoint Open(string id, bool loopback)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(id);
            var client = device.AudioClient;
            try
            {
                var wave = client.MixFormat;
                var format = Describe(wave);
                client.Initialize(AudioClientShareMode.Shared, loopback
                    ? AudioClientStreamFlags.Loopback : AudioClientStreamFlags.None,
                    TimeSpan.TicksPerMillisecond * 100, 0, wave, Guid.Empty);
                return new WasapiEndpoint(client, format, loopback,
                    TimeSpan.FromTicks(client.DefaultDevicePeriod));
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
        catch (RecordingException ex)
        {
            throw new RecordingException((loopback ? "Computer audio: " : "Microphone: ")
                + ex.Message, ex);
        }
        catch (Exception ex) when (DeviceErrors.IsDeviceError(ex))
        {
            throw DeviceErrors.Translate(loopback, ex);
        }
    }

    private static InputFormat Describe(WaveFormat wave)
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

    public void Start()
    {
        try
        {
            _client.Start();
            _started = true;
        }
        catch (Exception ex) when (DeviceErrors.IsDeviceError(ex))
        {
            throw DeviceErrors.Translate(_loopback, ex);
        }
    }

    public void Drain(Action<AudioPacket> accept)
    {
        try
        {
            var capture = _client.AudioCaptureClient;
            while (capture.GetNextPacketSize() > 0)
            {
                var pointer = capture.GetBuffer(out var frames, out var flags,
                    out var position, out var qpc);
                if (frames == 0)
                {
                    break;
                }

                byte[] bytes;
                try
                {
                    bytes = flags.HasFlag(AudioClientBufferFlags.Silent)
                        ? [] : new byte[checked(frames * _format.Stride)];
                    if (bytes.Length != 0)
                    {
                        Marshal.Copy(pointer, bytes, 0, bytes.Length);
                    }
                }
                finally
                {
                    capture.ReleaseBuffer(frames);
                }

                accept(new(_loopback, _format, position,
                    flags.HasFlag(AudioClientBufferFlags.TimestampError) ? null : qpc,
                    frames, bytes, flags.HasFlag(AudioClientBufferFlags.DataDiscontinuity)));
            }
        }
        catch (Exception ex) when (DeviceErrors.IsDeviceError(ex))
        {
            throw DeviceErrors.Translate(_loopback, ex);
        }
    }

    public void Dispose()
    {
        try
        {
            if (_started)
            {
                _client.Stop();
            }
        }
        catch (Exception ex) when (DeviceErrors.IsDeviceError(ex))
        {
            // An invalidated endpoint is already stopped.
        }
        finally
        {
            _client.Dispose();
        }
    }
}
