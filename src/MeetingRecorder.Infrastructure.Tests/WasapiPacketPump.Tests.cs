using System.Runtime.InteropServices;

using FluentAssertions;

using MeetingRecorder.Domain;
using MeetingRecorder.Infrastructure.Audio;
using MeetingRecorder.Infrastructure.Wasapi;

using NAudio.CoreAudioApi;

namespace MeetingRecorder.Infrastructure.Tests;

[Trait("Category", "Unit")]
public sealed class WasapiPacketPumpTests
{
    private static readonly InputFormat FORMAT = new(48000, 1, 16, false, 0);

    [Theory(DisplayName = "WASAPI packets preserve source, independent storage and timing flags")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PumpShouldCopyBeforeRelease(bool loopback, bool timestampError)
    {
        var flags = timestampError ? AudioClientBufferFlags.TimestampError
            | AudioClientBufferFlags.DataDiscontinuity : AudioClientBufferFlags.None;
        using var buffer = new NativeBuffer(2, flags, [1, 2, 3, 4]);
        var pump = new WasapiPacketPump(buffer, FORMAT, loopback);
        var packets = new List<AudioPacket>();

        pump.Drain(packet =>
        {
            buffer.Released.Should().Be(2);
            packets.Add(packet);
        });

        var result = packets.Should().ContainSingle().Subject;
        result.Data.Should().Equal(1, 2, 3, 4);
        result.Loopback.Should().Be(loopback);
        result.Position.Should().Be(480);
        result.Frames.Should().Be(2);
        result.Qpc.Should().Be(timestampError ? null : 100000L);
        result.Discontinuity.Should().Be(timestampError);
    }

    [Fact(DisplayName = "Silent WASAPI buffers may contain no native sample pointer")]
    public void SilentPacketShouldReleaseFramesWithoutReadingMemory()
    {
        using var buffer = new NativeBuffer(12, AudioClientBufferFlags.Silent, []);
        var pump = new WasapiPacketPump(buffer, FORMAT, true);
        var packets = new List<AudioPacket>();

        pump.Drain(packets.Add);

        packets.Single().Data.Should().BeEmpty();
        packets.Single().Frames.Should().Be(12);
        buffer.Released.Should().Be(12);
    }

    [Fact(DisplayName = "An empty acquire ends polling without releasing unacquired frames")]
    public void EmptyPacketShouldEndDrain()
    {
        using var buffer = new NativeBuffer(0, AudioClientBufferFlags.None, []);
        var pump = new WasapiPacketPump(buffer, FORMAT, false);
        var packets = new List<AudioPacket>();
        pump.Drain(packets.Add);
        packets.Should().BeEmpty();
        buffer.Released.Should().BeNull();
    }

    [Theory(DisplayName = "Invalid native buffers are released before an interruption propagates")]
    [InlineData(3, false, false)]
    [InlineData(int.MaxValue, true, false)]
    [InlineData(-1, true, false)]
    [InlineData(int.MaxValue, false, true)]
    public void InvalidBufferShouldAlwaysRelease(int frames, bool hasPointer, bool silent)
    {
        using var buffer = new NativeBuffer(frames, silent
            ? AudioClientBufferFlags.Silent : AudioClientBufferFlags.None,
            hasPointer ? [0, 0] : []);
        var pump = new WasapiPacketPump(buffer, FORMAT, false);
        var packets = new List<AudioPacket>();
        var drain = () => pump.Drain(packets.Add);

        drain.Should().Throw<RecordingException>().WithMessage("*buffer*");
        packets.Should().BeEmpty();
        buffer.Released.Should().Be(frames);
    }

    private sealed class NativeBuffer : IWasapiBuffer, IDisposable
    {
        private readonly int _frames;
        private readonly AudioClientBufferFlags _flags;
        private readonly nint _pointer;
        private bool _available = true;

        internal NativeBuffer(int frames, AudioClientBufferFlags flags, byte[] data)
        {
            _frames = frames;
            _flags = flags;
            if (data.Length > 0)
            {
                _pointer = Marshal.AllocHGlobal(data.Length);
                Marshal.Copy(data, 0, _pointer, data.Length);
            }
        }

        internal int? Released { get; private set; }
        public int GetNextPacketSize() => _available ? 1 : 0;

        public nint Acquire(out int frames, out AudioClientBufferFlags flags,
            out long position, out long qpc)
        {
            _available = false;
            frames = _frames;
            flags = _flags;
            position = 480;
            qpc = 100000;
            return _pointer;
        }

        public void Release(int frames)
        {
            Released.Should().BeNull();
            Released = frames;
            if (_pointer != 0)
            {
                Marshal.WriteByte(_pointer, 0, 0);
            }
        }

        public void Dispose() => Marshal.FreeHGlobal(_pointer);
    }
}
