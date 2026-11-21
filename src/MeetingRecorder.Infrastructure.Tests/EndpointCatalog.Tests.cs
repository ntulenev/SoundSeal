using FluentAssertions;

using MeetingRecorder.Domain;
using MeetingRecorder.Infrastructure.Wasapi;

namespace MeetingRecorder.Infrastructure.Tests;

[Trait("Category", "Integration")]
public sealed class EndpointCatalogTests
{
    [Fact(DisplayName = "Passive Windows enumeration returns identifiable devices or an audio-service error")]
    public void EnumerationShouldSupportMachinesWithoutAnActiveEndpoint()
    {
        Devices devices;
        try
        {
            devices = new EndpointCatalog().Read();
        }
        catch (RecordingException ex)
        {
            // Windows test hosts may have the audio service disabled.
            ex.Message.Should().Contain("Windows audio devices").And.Contain("refresh devices");
            ex.InnerException.Should().NotBeNull();
            DeviceErrors.IsDeviceError(ex.InnerException!).Should().BeTrue();
            return;
        }

        foreach (var endpoints in new[] { devices.Microphones, devices.Outputs })
        {
            endpoints.Select(device => device.Id).Distinct(StringComparer.Ordinal).Count()
                .Should().Be(endpoints.Count);
            endpoints.Should().NotContain(device => device.Id.Length == 0 || device.Name.Length == 0);
            endpoints.Count(device => device.IsDefault).Should().BeLessThanOrEqualTo(1);
        }
    }
}
