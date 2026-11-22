using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;
using MeetingRecorder.Infrastructure.Wasapi;

namespace MeetingRecorder.Infrastructure.Audio;

/// <summary>Runs the normal capture pipeline against a bounded in-memory destination.</summary>
internal sealed class SoundCheck : ISoundCheck
{
    private readonly IRecordingClock _clock;
    private readonly Func<EndpointId, bool, IInputEndpoint> _open;
    private readonly Func<EndpointId, CancellationToken, Task> _tone;
    private readonly int _capacity;

    public SoundCheck(IRecordingClock clock, AudioCaptureOptions options)
        : this(clock, (id, loopback) => WasapiEndpoint.Open(id, loopback, options), SoundCheckPlayback.ToneAsync,
            options.QueueCapacity)
    { }

    internal SoundCheck(IRecordingClock clock, Func<EndpointId, bool, IInputEndpoint> open,
        Func<EndpointId, CancellationToken, Task> tone, int capacity)
    {
        _clock = clock;
        _open = open;
        _tone = tone;
        _capacity = capacity;
    }

    public async Task<byte[]> CaptureAsync(EndpointId? microphone, EndpointId? output, IProgress<Levels> peaks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(peaks);
        if (microphone is null && output is null)
        {
            throw new ArgumentException("Choose an audio source first.", nameof(microphone));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var sample = new SampleBuffer();
        var interrupted = new TaskCompletionSource<RecordingProblem>(TaskCreationOptions.RunContinuationsAsynchronously);
        var maximum = new Levels();
        void Report(Levels levels)
        {
            var next = new Levels(Math.Max(levels.Microphone.Value, maximum.Microphone.Value),
                Math.Max(levels.Computer.Value, maximum.Computer.Value));
            if (next != maximum)
            {
                maximum = next;
                peaks.Report(next);
            }
        }

        // StopAsync owns shutdown and drains the mixer even when the dialog cancels startup.
        var interval = new CaptureInterval(microphone, output, _clock, _open, sample, Report,
            problem => interrupted.TrySetResult(problem), _capacity, CancellationToken.None);
        try
        {
            _ = await interval.Started.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (output is not null)
            {
                await _tone(output, cancellationToken).ConfigureAwait(false);
            }
            _ = await Task.WhenAny(sample.Filled.Task, interrupted.Task).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await interval.StopAsync(_clock.Now, CancellationToken.None).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (interrupted.Task.IsCompletedSuccessfully)
        {
            var problem = await interrupted.Task.ConfigureAwait(false);
            throw new RecordingException(problem.Message);
        }
        return sample.Bytes;
    }

    public Task PlayAsync(EndpointId? output, byte[] sample, CancellationToken cancellationToken) =>
        SoundCheckPlayback.SampleAsync(output, sample, cancellationToken);

    private sealed class SampleBuffer : IPcmSink
    {
        private int _length;
        internal byte[] Bytes { get; } = new byte[5 * RecordingFormat.SampleRate * sizeof(short)];
        internal TaskCompletionSource Filled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Append(Pcm16Block block)
        {
            var count = Math.Min(block.Bytes.Length, Bytes.Length - _length);
            block.Bytes[..count].CopyTo(Bytes.AsSpan(_length));
            _length += count;
            if (_length == Bytes.Length)
            {
                _ = Filled.TrySetResult();
            }
        }
    }
}
