using System.Collections.Frozen;
using System.Globalization;

using Microsoft.Extensions.Configuration;

namespace MeetingRecorder.Infrastructure.Audio;

/// <summary>Validated capture limits shared by device negotiation and the packet queue.</summary>
internal sealed class AudioCaptureOptions
{
    internal const int DEFAULT_QUEUE_CAPACITY = 512;
    internal const int CHANNEL_MASK_BITS = sizeof(uint) * 8;

    private AudioCaptureOptions(int minimum, int maximum, int channels, int capacity, FrozenSet<SampleEncoding> encodings)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimum);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, minimum);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channels, CHANNEL_MASK_BITS);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        AllowedEncodings = encodings;
        MinimumSampleRate = minimum;
        MaximumSampleRate = maximum;
        MaximumChannels = channels;
        QueueCapacity = capacity;
    }

    /// <summary>Defaults used when the host supplies no overrides.</summary>
    internal static AudioCaptureOptions Default { get; } = new(8_000, 384_000, CHANNEL_MASK_BITS, DEFAULT_QUEUE_CAPACITY,
        Enum.GetValues<SampleEncoding>().ToFrozenSet());

    /// <summary>Sample encodings accepted during device negotiation.</summary>
    internal FrozenSet<SampleEncoding> AllowedEncodings { get; }

    /// <summary>Lowest accepted device rate in frames per second.</summary>
    internal int MinimumSampleRate { get; }

    /// <summary>Highest accepted device rate in frames per second.</summary>
    internal int MaximumSampleRate { get; }

    /// <summary>Largest accepted native source channel count.</summary>
    internal int MaximumChannels { get; }

    /// <summary>Maximum number of queued capture batches before an overload is reported.</summary>
    internal int QueueCapacity { get; }

    /// <summary>Reads and validates the AudioCapture configuration section at startup.</summary>
    internal static AudioCaptureOptions Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection("AudioCapture");
        return new(ReadInt(nameof(MinimumSampleRate), Default.MinimumSampleRate),
            ReadInt(nameof(MaximumSampleRate), Default.MaximumSampleRate),
            ReadInt(nameof(MaximumChannels), Default.MaximumChannels),
            ReadInt(nameof(QueueCapacity), Default.QueueCapacity),
            section[nameof(AllowedEncodings)] is { } encodings ? ParseEncodings(encodings) : Default.AllowedEncodings);

        int ReadInt(string key, int fallback)
        {
            if (section[key] is not { } value)
            {
                return fallback;
            }

            return int.TryParse(value, CultureInfo.InvariantCulture, out var number)
                ? number : throw new FormatException($"AudioCapture:{key} must be an integer.");
        }
    }

    private static FrozenSet<SampleEncoding> ParseEncodings(string value)
    {
        var encodings = new HashSet<SampleEncoding>();
        foreach (var name in value.Split(',', StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<SampleEncoding>(name, true, out var encoding)
                || !Enum.IsDefined(encoding)
                || !string.Equals(name, encoding.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException($"Unsupported AudioCapture:AllowedEncodings entry '{name}'.");
            }

            _ = encodings.Add(encoding);
        }

        return encodings.ToFrozenSet();
    }
}
