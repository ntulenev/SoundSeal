using MeetingRecorder.Domain;

using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MeetingRecorder.Infrastructure.Wasapi;

/// <summary>Routes the reference tone and captured mix to the intended Windows render endpoint.</summary>
internal static class SoundCheckPlayback
{
    internal static Task ToneAsync(EndpointId output, CancellationToken cancellationToken)
    {
        var tone = new SignalGenerator(RecordingFormat.SampleRate, 1)
        {
            Type = SignalGeneratorType.Sin,
            Frequency = 440,
            Gain = 0.15
        };
        return PlayAsync(output, tone.Take(TimeSpan.FromMilliseconds(500)).ToWaveProvider(), cancellationToken);
    }

    internal static async Task SampleAsync(EndpointId? output, byte[] sample, CancellationToken cancellationToken)
    {
        using var wave = new RawSourceWaveStream(sample, 0, sample.Length, new WaveFormat(RecordingFormat.SampleRate, 16, 1));
        await PlayAsync(output, wave, cancellationToken).ConfigureAwait(false);
    }

    private static Task PlayAsync(EndpointId? output, IWaveProvider source, CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            try
            {
                using var devices = new MMDeviceEnumerator();
                using var device = output is null ? devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                    : devices.GetDevice(output.Value);
                using var player = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
                var stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
                player.PlaybackStopped += (sender, e) => stopped.TrySetResult(e.Exception);
                player.Init(source);
                cancellationToken.ThrowIfCancellationRequested();
                player.Play();
                try
                {
                    var error = await stopped.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    if (error is not null)
                    {
                        throw new RecordingException("Playback failed. Check the output device.", error);
                    }
                }
                finally
                {
                    player.Stop();
                }
            }
            catch (Exception ex) when (ex is not RecordingException && DeviceErrors.IsDeviceError(ex))
            {
                throw DeviceErrors.Translate(true, ex);
            }
        }, cancellationToken);
}
