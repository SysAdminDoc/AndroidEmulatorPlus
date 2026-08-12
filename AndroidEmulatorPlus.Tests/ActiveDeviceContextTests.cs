using AndroidEmulatorPlus.Models;
using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public sealed class ActiveDeviceContextTests
{
    [Fact]
    public void Single_device_is_safe_default()
    {
        var context = new ActiveDeviceContext();
        var emulator = Device("emulator-5554", isEmulator: true);

        Assert.Equal(emulator, context.ResolveOnlineEmulator(new[] { emulator }));
    }

    [Fact]
    public void Multiple_devices_require_explicit_selection()
    {
        var context = new ActiveDeviceContext();
        var first = Device("emulator-5554", isEmulator: true);
        var second = Device("emulator-5556", isEmulator: true);

        Assert.Null(context.ResolveOnlineEmulator(new[] { first, second }));

        context.SelectEmulator(second);

        Assert.Equal(second, context.ResolveOnlineEmulator(new[] { first, second }));
        Assert.Equal("emulator-5556", context.SelectedEmulatorSerial);
    }

    [Fact]
    public void Disconnected_selection_does_not_fall_back_to_another_device()
    {
        var context = new ActiveDeviceContext();
        var selected = Device("emulator-5554", isEmulator: true);
        var replacement = Device("emulator-5556", isEmulator: true);
        context.SelectEmulator(selected);

        var current = new[] { replacement };

        Assert.Null(context.ResolveOnlineEmulator(current));
    }

    [Fact]
    public void Phone_and_emulator_selections_are_independent()
    {
        var context = new ActiveDeviceContext();
        var phone = Device("phone", isEmulator: false);
        var emulator = Device("emulator-5554", isEmulator: true);
        context.SelectPhone(phone);
        context.SelectEmulator(emulator);

        var current = new[] { phone, emulator };

        Assert.Equal(phone, context.ResolveOnlinePhone(current));
        Assert.Equal(emulator, context.ResolveOnlineEmulator(current));
    }

    private static Device Device(string serial, bool isEmulator)
        => new(serial, "device", "", "", isEmulator);
}
