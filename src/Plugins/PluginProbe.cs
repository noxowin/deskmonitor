using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MonitorPlugin;

namespace deskmonitor
{
    internal static class PluginProbe
    {
        public const string Argument = "--monitor-plugin-probe";
        private const int MaxResultBytes = 64 * 1024;
        private const string PipePrefix = "DeskMonitor.Plugin.";

        public static async Task<byte[]> ReadAsync(string path, string inputFile, CancellationToken cancellation)
        {
            string id = Guid.NewGuid().ToString("N");
            using (var identity = WindowsIdentity.GetCurrent())
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                var security = new PipeSecurity();
                security.AddAccessRule(new PipeAccessRule(identity.User, PipeAccessRights.FullControl, AccessControlType.Allow));
                security.AddAccessRule(new PipeAccessRule(DesktopUserProcess.GetDesktopUser(), PipeAccessRights.ReadWrite,
                    AccessControlType.Allow));
                using (var pipe = new NamedPipeServerStream(PipePrefix + id, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security))
                using (deadline.Token.Register(pipe.Dispose))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(30));
                    cancellation.ThrowIfCancellationRequested();
                    string encodedPath = EncodeArgument(path);
                    string encodedInput = EncodeArgument(inputFile);
                    await DesktopUserProcess.StartAsync(Assembly.GetExecutingAssembly().Location,
                        $"{Argument} {id} {encodedPath} {encodedInput}", deadline.Token).ConfigureAwait(false);
                    await pipe.WaitForConnectionAsync(deadline.Token).ConfigureAwait(false);
                    return await ReadBoundedAsync(pipe).ConfigureAwait(false);
                }
            }
        }

        public static int RunWorker(string[] args)
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                    if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return 2;
                if (args.Length != 4 || !Guid.TryParseExact(args[1], "N", out var id)) return 2;

                string path = DecodeArgument(args[2]);
                string input = DecodeArgument(args[3]);
                if (!Path.IsPathRooted(path) || !File.Exists(path)
                    || (!PluginCatalog.IsSingleFile(path)
                        && !string.Equals(Path.GetFileName(path), PluginCatalog.Executable, StringComparison.OrdinalIgnoreCase)))
                    return 2;
                using (var pipe = new NamedPipeClientStream(".", PipePrefix + id.ToString("N"),
                    PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification))
                {
                    pipe.Connect(3000);
                    return QueryPluginAsync(pipe, path, input).GetAwaiter().GetResult();
                }
            }
            catch (Exception) { return 1; } // Empty pipe tells the caller that the provider failed.
        }

        private static async Task<int> QueryPluginAsync(Stream pipe, string path, string input)
        {
            var start = new ProcessStartInfo
            {
                FileName = path,
                Arguments = Protocol.PollArgument,
                WorkingDirectory = Path.GetDirectoryName(path),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            start.EnvironmentVariables.Remove(Protocol.InputFileVariable);
            if (input.Length != 0) start.EnvironmentVariables[Protocol.InputFileVariable] = input;
            using (var process = Process.Start(start))
            using (var timer = new CancellationTokenSource())
            {
                try
                {
                    Task<byte[]> output = ReadBoundedAsync(process.StandardOutput.BaseStream);
                    // The parent never sends data. If it closes the pipe (switch off or app exit)
                    // bail out and clean up our child. Our own deadline also bounds a hung provider.
                    Task disconnected = pipe.ReadAsync(new byte[1], 0, 1);
                    Task timeout = Task.Delay(20000, timer.Token);
                    if (await Task.WhenAny(output, disconnected, timeout).ConfigureAwait(false) != output)
                        return 1;
                    byte[] bytes = await output.ConfigureAwait(false);
                    if (bytes == null || !process.WaitForExit(1000) || process.ExitCode != 0) return 1;
                    await pipe.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                    return 0;
                }
                finally
                {
                    timer.Cancel();
                    if (!process.HasExited)
                    {
                        try { process.Kill(); }
                        catch (InvalidOperationException) { }
                    }
                }
            }
        }

        private static string EncodeArgument(string value) =>
            "x" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));

        private static string DecodeArgument(string value) =>
            Encoding.UTF8.GetString(Convert.FromBase64String(value.Substring(1)));

        private static async Task<byte[]> ReadBoundedAsync(Stream stream)
        {
            using (var result = new MemoryStream())
            {
                var buffer = new byte[4096];
                for (int count; (count = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0;)
                {
                    if (result.Length + count > MaxResultBytes) return null;
                    result.Write(buffer, 0, count);
                }
                return result.ToArray();
            }
        }
    }
}
