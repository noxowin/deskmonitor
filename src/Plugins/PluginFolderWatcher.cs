using System;
using System.IO;
using System.Threading;
using System.Windows.Threading;

namespace deskmonitor
{
    internal sealed class PluginFolderWatcher : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly DispatcherTimer _debounce;
        private readonly Dispatcher _dispatcher;
        private int _queued;
        private volatile bool _disposed;

        public PluginFolderWatcher(PluginCatalog catalog, Dispatcher dispatcher, Action rescan)
        {
            catalog.EnsureRoot();
            _dispatcher = dispatcher;
            _debounce = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(450),
            };
            _debounce.Tick += (s, e) => { _debounce.Stop(); rescan(); };
            _watcher = new FileSystemWatcher(catalog.Root)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            _watcher.Changed += OnChange;
            _watcher.Created += OnChange;
            _watcher.Deleted += OnChange;
            _watcher.Renamed += (s, e) =>
            {
                if (IsCandidate(e.Name) || IsCandidate(e.OldName)) RequestRescan();
            };
            _watcher.Error += (s, e) => RequestRescan();
            _watcher.EnableRaisingEvents = true;
        }

        private static bool IsCandidate(string name) => name != null
            && (PluginCatalog.IsSingleFile(name) || name.StartsWith("plugin-", StringComparison.OrdinalIgnoreCase));

        private void OnChange(object sender, FileSystemEventArgs args)
        {
            if (IsCandidate(args.Name)) RequestRescan();
        }

        private void RequestRescan()
        {
            if (_disposed || _dispatcher.HasShutdownStarted || Interlocked.Exchange(ref _queued, 1) != 0) return;
            _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                Interlocked.Exchange(ref _queued, 0);
                if (_disposed) return;
                _debounce.Stop();
                _debounce.Start();
            }));
        }

        public void Dispose()
        {
            _disposed = true;
            _watcher.Dispose();
            _debounce.Stop();
        }
    }
}
