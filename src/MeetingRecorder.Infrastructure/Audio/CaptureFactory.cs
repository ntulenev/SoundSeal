using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;
using MeetingRecorder.Infrastructure.Wasapi;

using Microsoft.Extensions.Logging;

namespace MeetingRecorder.Infrastructure.Audio;

/// <summary>Composes the file and the pinned device streams for one session.</summary>
internal sealed class CaptureFactory : ICaptureFactory
{
    private readonly IRecordingClock _clock;
    private readonly IRecordingStore _store;
    private readonly AudioCaptureOptions _options;
    private readonly ILogger<CaptureInterval>? _logger;

    /// <summary>Combines the recording store with the shared performance clock and device policy.</summary>
    public CaptureFactory(IRecordingClock clock, IRecordingStore store, AudioCaptureOptions options,
        ILogger<CaptureInterval>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        _clock = clock;
        _store = store;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ICaptureSession> OpenAsync(RecordingSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var file = await _store.CreateAsync(settings, cancellationToken).ConfigureAwait(false);
        return new CaptureSession(settings, _clock, file,
            (id, loopback) => WasapiEndpoint.Open(id, loopback, _options), _options.QueueCapacity, _logger);
    }
}
