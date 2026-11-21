using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;
using MeetingRecorder.Infrastructure.Wasapi;

namespace MeetingRecorder.Infrastructure.Audio;

/// <summary>Composes the file and the pinned device streams for one session.</summary>
internal sealed class CaptureFactory : ICaptureFactory
{
    private readonly IRecordingClock _clock;
    private readonly IRecordingStore _store;
    private readonly AudioCaptureOptions _options;

    /// <summary>Combines the recording store with the shared performance clock and device policy.</summary>
    public CaptureFactory(IRecordingClock clock, IRecordingStore store, AudioCaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        _clock = clock;
        _store = store;
        _options = options;
    }

    /// <inheritdoc/>
    public async Task<ICaptureSession> OpenAsync(RecordingSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var file = await _store.CreateAsync(settings, cancellationToken).ConfigureAwait(false);
        return new CaptureSession(settings, _clock, file,
            (id, loopback) => WasapiEndpoint.Open(id, loopback, _options), _options.QueueCapacity);
    }
}
