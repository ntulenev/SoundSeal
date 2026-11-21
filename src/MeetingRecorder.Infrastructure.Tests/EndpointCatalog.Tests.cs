using FluentAssertions;

using MeetingRecorder.Domain;
using MeetingRecorder.Infrastructure.Wasapi;
using MeetingRecorder.TestSupport;

namespace MeetingRecorder.Infrastructure.Tests;

/// <summary>Tests Windows endpoint selection.</summary>
[Trait("Category", "Integration")]
public sealed class EndpointCatalogTests
{
    /// <summary>Passive Windows enumeration returns identifiable devices or an audio-service error.</summary>
    [Fact(DisplayName = "Passive Windows enumeration returns identifiable devices or an audio-service error")]
    public Task EnumerationShouldSupportMachinesWithoutAnActiveEndpointAsync() =>
        TestDeadline.Run(async cancellationToken =>
        {
            Devices devices;
            try
            {
                devices = await new EndpointCatalog().ReadAsync(cancellationToken);
            }
            catch (RecordingException ex)
            {
                // Windows test hosts may have the audio service disabled.
                ex.Message.Should().Contain("Windows audio devices").And.Contain("refresh devices");
                ex.InnerException.Should().NotBeNull();
                DeviceErrors.IsDeviceError(ex.InnerException).Should().BeTrue();
                return;
            }

            foreach (var endpoints in new[] { devices.Microphones, devices.Outputs })
            {
                endpoints.Select(device => device.Id).Distinct().Count()
                    .Should().Be(endpoints.Count);
                endpoints.Should().NotContain(device => device.Id.Value.Length == 0 || device.Name.Value.Length == 0);
                endpoints.Count(device => device.IsDefault).Should().BeLessThanOrEqualTo(1);
            }
        });
}
