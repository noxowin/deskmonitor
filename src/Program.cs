using System;

namespace deskmonitor
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // The one-shot plugin poll runs before WPF and the application's elevation gate.
            if (args.Length > 0 && args[0] == PluginProbe.Argument)
                return PluginProbe.RunWorker(args);

            var app = new App();
            app.InitializeComponent();
            return app.Run();
        }
    }
}
