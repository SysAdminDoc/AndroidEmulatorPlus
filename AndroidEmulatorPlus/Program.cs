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

        if (args.Any(a => a.Equals("--headless", StringComparison.OrdinalIgnoreCase)))
        {
            var code = HeadlessRunner.RunAsync(args).GetAwaiter().GetResult();
            Environment.Exit(code);
            return;
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
