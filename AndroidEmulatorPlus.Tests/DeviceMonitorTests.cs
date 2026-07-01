using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class DeviceMonitorTests
{
    [Theory]
    [InlineData("emulator-5554", true)]
    [InlineData("emulator-5556", true)]
    [InlineData("RFCR12345", false)]
    [InlineData("192.168.1.42:5555", false)]
    [InlineData("", false)]
    public void Device_IsEmulator_detected_from_serial(string serial, bool expected)
    {
        var device = new Models.Device(serial, "device", "", "", serial.StartsWith("emulator-"));
        Assert.Equal(expected, device.IsEmulator);
    }

    [Fact]
    public void Device_IsOnline_requires_device_state()
    {
        var online = new Models.Device("emulator-5554", "device", "", "", true);
        var offline = new Models.Device("emulator-5554", "offline", "", "", true);
        var unauthorized = new Models.Device("RFCR12345", "unauthorized", "", "", false);

        Assert.True(online.IsOnline);
        Assert.False(offline.IsOnline);
        Assert.False(unauthorized.IsOnline);
    }

    [Fact]
    public void Device_Display_prefers_model_when_available()
    {
        var withModel = new Models.Device("emulator-5554", "device", "Pixel_7", "sdk_phone_x86_64", true);
        var noModel = new Models.Device("emulator-5554", "device", "", "", true);

        Assert.Contains("Pixel_7", withModel.Display);
        Assert.Equal("emulator-5554", noModel.Display);
    }

    [Theory]
    [InlineData("emulator-5554", "emulator")]
    [InlineData("RFCR12345", "usb")]
    [InlineData("192.168.1.42:5555", "wireless")]
    public void InferTransport_detects_transport_type(string serial, string expected)
    {
        var dev = new Models.Device(serial, "device", "", "", serial.StartsWith("emulator-"));
        Assert.Equal(expected, AdbService.InferTransport(dev));
    }
}
