using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using MeetingRecorder.Domain;

using Microsoft.Extensions.Logging;

using NAudio.MediaFoundation;
using NAudio.Wave;

namespace MeetingRecorder.Storage;

/// <summary>Streams the mix to the Windows Media Foundation mono MP3 encoder.</summary>
[SupportedOSPlatform("windows")]
internal sealed class Mp3Destination : FileDestination
{
    private const int BIT_RATE = 48_000;
    private const int SAMPLES_PER_MP3_FRAME = 576; // MPEG-2 Layer III at 16 kHz.
    private readonly byte[] _pcm = new byte[RecordingFormat.SampleRate * sizeof(short) / 4];
    private readonly int _streamIndex;
    private readonly string _path;
    private IMFSinkWriter? _writer;
    private int _buffered;
    private long _timestampTicks;
    private bool _encodingFailed;

    internal Mp3Destination(RecordingFile temporary, RecordingFile final, ILogger? logger = null)
        : base(temporary, final, logger)
    {
        _path = temporary.Value;
        try
        {
            MediaFoundationApi.Startup();
            _writer = OpenWriter(temporary.Value);
            _streamIndex = Configure(_writer);
            _writer.BeginWriting();
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or TypeInitializationException)
        {
            CloseFile();
            throw new RecordingException("Cannot start the Windows MP3 encoder. Check the recording folder and Windows media features, or select WAV.", ex);
        }
        catch
        {
            CloseFile();
            throw;
        }
    }

    protected override void WriteBlock(Pcm16Block block)
    {
        var remaining = block.Bytes;
        while (!remaining.IsEmpty)
        {
            var count = Math.Min(remaining.Length, _pcm.Length - _buffered);
            remaining[..count].CopyTo(_pcm.AsSpan(_buffered));
            _buffered += count;
            remaining = remaining[count..];
            if (_buffered == _pcm.Length)
            {
                SubmitBuffer();
            }
        }
    }

    protected override void FinishFile()
    {
        if (_encodingFailed)
        {
            throw new IOException("The encoder stopped before it could complete this MP3.");
        }
        if (_buffered > 0)
        {
            SubmitBuffer();
        }

        // Align to an MPEG frame, then drain the encoder delay. Four silent frames also
        // let Windows recognize very short clips. Padding does not count as captured time.
        var alignment = (SAMPLES_PER_MP3_FRAME - (Frames.Value % SAMPLES_PER_MP3_FRAME)) % SAMPLES_PER_MP3_FRAME;
        var padding = alignment + (4 * SAMPLES_PER_MP3_FRAME);
        _buffered = (int)padding * sizeof(short);
        Array.Clear(_pcm, 0, _buffered);
        SubmitBuffer();
        _writer!.DoFinalize();
        CloseFile();
        using var file = File.OpenHandle(_path, FileMode.Open, FileAccess.Write);
        RandomAccess.FlushToDisk(file);
    }

    protected override void CloseFile()
    {
        if (_writer is { } writer)
        {
            _writer = null;
            _ = Marshal.ReleaseComObject(writer);
        }
    }

    private static IMFSinkWriter OpenWriter(string path)
    {
        var attributes = MediaFoundationApi.CreateAttributes(1);
        try
        {
            // The reservation ends in .partial, so specify the container explicitly.
            attributes.SetGUID(MediaFoundationAttributes.MF_TRANSCODE_CONTAINERTYPE,
                TranscodeContainerTypes.MFTranscodeContainerType_MP3);
            MediaFoundationInterop.MFCreateSinkWriterFromURL(path, null, attributes, out var writer);
            return writer;
        }
        finally
        {
            _ = Marshal.ReleaseComObject(attributes);
        }
    }

    private static int Configure(IMFSinkWriter writer)
    {
        var pcm = new WaveFormat(RecordingFormat.SampleRate, 16, 1);
        var output = MediaFoundationEncoder.SelectMediaType(AudioSubtypes.MFAudioFormat_MP3, pcm, BIT_RATE)
            ?? throw new RecordingException("The Windows MP3 encoder is unavailable. Select WAV or install the Windows media features.");
        try
        {
            if (output.AverageBytesPerSecond * 8 != BIT_RATE)
            {
                throw new RecordingException("The Windows MP3 encoder does not support 48 kbit/s. Select WAV.");
            }

            writer.AddStream(output.MediaFoundationObject, out var index);
            var input = MediaFoundationApi.CreateMediaTypeFromWaveFormat(pcm);
            try
            {
                writer.SetInputMediaType(index, input, null);
            }
            finally
            {
                _ = Marshal.ReleaseComObject(input);
            }
            return index;
        }
        finally
        {
            _ = Marshal.ReleaseComObject(output.MediaFoundationObject);
        }
    }

    private void SubmitBuffer()
    {
        _encodingFailed = true;
        var buffer = MediaFoundationApi.CreateMemoryBuffer(_buffered);
        try
        {
            buffer.Lock(out var pointer, out _, out _);
            try
            {
                Marshal.Copy(_pcm, 0, pointer, _buffered);
            }
            finally
            {
                buffer.Unlock();
            }
            buffer.SetCurrentLength(_buffered);
            var sample = MediaFoundationApi.CreateSample();
            try
            {
                sample.AddBuffer(buffer);
                var duration = _buffered / sizeof(short) * RecordingFormat.TicksPerFrame;
                sample.SetSampleTime(_timestampTicks);
                sample.SetSampleDuration(duration);
                _writer!.WriteSample(_streamIndex, sample);
                _timestampTicks += duration;
                _buffered = 0;
                _encodingFailed = false;
            }
            finally
            {
                _ = Marshal.ReleaseComObject(sample);
            }
        }
        finally
        {
            _ = Marshal.ReleaseComObject(buffer);
        }
    }
}
