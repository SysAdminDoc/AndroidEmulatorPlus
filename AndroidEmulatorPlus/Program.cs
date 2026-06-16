using System.Windows;
using Velopack;

namespace AndroidEmulatorPlus;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            .SetArgs(args)
            .SetAppUserModelId("SysAdminDoc.AndroidEmulatorPlus")
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
