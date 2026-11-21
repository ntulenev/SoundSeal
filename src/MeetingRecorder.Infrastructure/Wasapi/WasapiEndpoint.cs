using MeetingRecorder.Domain;
using MeetingRecorder.Infrastructure.Audio;

using NAudio.CoreAudioApi;

namespace MeetingRecorder.Infrastructure.Wasapi;

internal sealed class WasapiEndpoint : IInputEndpoint
{
    private readonly AudioClient _client;
    private readonly WasapiPacketPump _packets;
    private readonly bool _loopback;
    private bool _started;

    private WasapiEndpoint(AudioClient client, InputFormat format, bool loopback,
        TimeSpan enginePeriod)
    {
        _client = client;
        _packets = new WasapiPacketPump(new WasapiBuffer(client.AudioCaptureClient), format,
            loopback);
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
                var format = WasapiFormatMapper.FromMixFormat(wave);
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
            _packets.Drain(accept);
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
