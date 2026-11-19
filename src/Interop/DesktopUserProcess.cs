using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace deskmonitor
{
    internal static class DesktopUserProcess
    {
        public static SecurityIdentifier GetDesktopUser()
        {
            IntPtr shell = GetShellWindow();
            if (shell == IntPtr.Zero || WindowInterop.GetWindowThreadProcessId(shell, out uint pid) == 0)
                throw new InvalidOperationException("找不到 Windows 桌面进程");

            const uint queryLimitedInformation = 0x1000;
            IntPtr process = SystemInfo.OpenProcess(queryLimitedInformation, false, pid);
            if (process == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 Windows 桌面进程");
            try
            {
                const uint queryToken = 0x0008;
                if (!OpenProcessToken(process, queryToken, out var token))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 Windows 桌面用户");
                using (token)
                {
                    const int tokenElevation = 20;
                    if (!GetTokenInformation(token, tokenElevation, out int elevated, sizeof(int), out _))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 Windows 桌面权限");
                    if (elevated != 0)
                        throw new InvalidOperationException("Windows 桌面正在使用管理员权限");
                    using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
                        return identity.User;
                }
            }
            finally { SystemInfo.CloseHandle(process); }
        }

        public static Task StartAsync(string file, string arguments, CancellationToken cancellation)
        {
            var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    StartFromExplorer(file, arguments, cancellation);
                    result.SetResult(true);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    result.SetCanceled();
                }
                catch (Exception ex)
                {
                    result.SetException(new InvalidOperationException("Windows Explorer 无法启动插件进程", ex));
                }
            }) { IsBackground = true, Name = "MonitorPluginLaunch" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return result.Task;
        }

        private static void StartFromExplorer(string file, string arguments, CancellationToken cancellation)
        {
            // Use the existing desktop folder's shell automation object so Explorer
            // creates the worker in the signed-in user's desktop context.
            // https://learn.microsoft.com/windows/win32/shell/samples-execinexplorer
            cancellation.ThrowIfCancellationRequested();
            const int desktop = 8; // SWC_DESKTOP
            const int needDispatch = 1; // SWFO_NEEDDISPATCH
            const uint viewBackground = 0; // SVGIO_BACKGROUND
            const int hide = 0; // SW_HIDE
            var windows = (IShellWindows)new ShellWindows();
            object desktopLocation = 0; // CSIDL_DESKTOP
            object root = null; // VT_EMPTY
            object desktopWindow = null, browser = null, view = null, background = null, shell = null;
            try
            {
                desktopWindow = windows.FindWindowSW(ref desktopLocation, ref root, desktop, out _, needDispatch)
                    ?? throw new InvalidOperationException("Explorer 未注册桌面窗口");
                var topLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
                var shellBrowserId = typeof(IShellBrowser).GUID;
                browser = ((IShellServiceProvider)desktopWindow).QueryService(ref topLevelBrowser, ref shellBrowserId);
                view = ((IShellBrowser)browser).QueryActiveShellView();
                var dispatchId = new Guid("00020400-0000-0000-C000-000000000046"); // IDispatch
                background = ((IShellView)view).GetItemObject(viewBackground, ref dispatchId);
                shell = ((IShellFolderViewDual)background).Application;
                cancellation.ThrowIfCancellationRequested();
                ((IShellDispatch2)shell).ShellExecute(file, arguments, Path.GetDirectoryName(file), "open", hide);
            }
            finally
            {
                Release(shell);
                Release(background);
                Release(view);
                Release(browser);
                Release(desktopWindow);
                Release(windows);
            }
        }

        private static void Release(object instance)
        {
            if (instance != null && Marshal.IsComObject(instance)) Marshal.ReleaseComObject(instance);
        }

        [ComImport, Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")]
        private class ShellWindows { }

        [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
        [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
        private interface IShellWindows
        {
            [return: MarshalAs(UnmanagedType.IDispatch)]
            object FindWindowSW([In, MarshalAs(UnmanagedType.Struct)] ref object location,
                [In, MarshalAs(UnmanagedType.Struct)] ref object root, int windowClass, out int hwnd, int options);
        }

        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellServiceProvider
        {
            [return: MarshalAs(UnmanagedType.Interface)]
            object QueryService(ref Guid service, ref Guid iid);
        }

        [ComImport, Guid("000214E2-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellBrowser
        {
            void _VtblGap1_12(); // IOleWindow and the first ten IShellBrowser methods
            IShellView QueryActiveShellView();
        }

        [ComImport, Guid("000214E3-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellView
        {
            void _VtblGap1_12(); // IOleWindow and the first ten IShellView methods
            [return: MarshalAs(UnmanagedType.Interface)]
            object GetItemObject(uint item, ref Guid iid);
        }

        [ComImport, Guid("E7A1AF80-4D96-11CF-960C-0080C7F4EE85")]
        [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
        private interface IShellFolderViewDual
        {
            object Application { [return: MarshalAs(UnmanagedType.IDispatch)] get; }
        }

        [ComImport, Guid("A4C6892C-3BA9-11D2-9DEA-00C04FB16162")]
        [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
        private interface IShellDispatch2
        {
            void ShellExecute([MarshalAs(UnmanagedType.BStr)] string file,
                [MarshalAs(UnmanagedType.Struct)] object arguments, [MarshalAs(UnmanagedType.Struct)] object directory,
                [MarshalAs(UnmanagedType.Struct)] object operation, [MarshalAs(UnmanagedType.Struct)] object show);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass,
            out int information, int length, out int requiredLength);
    }
}
