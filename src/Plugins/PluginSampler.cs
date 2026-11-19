using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MonitorPlugin;

namespace deskmonitor
{
    public sealed class PluginPreference
    {
        public string Id { get; set; }
        public bool Enabled { get; set; }
        public string InputFile { get; set; }
        public List<string> HiddenRows { get; set; }
    }

    internal sealed class PluginActivation
    {
        private readonly HashSet<string> _hiddenRows;

        public PluginActivation(InstalledPlugin plugin, string inputFile, int slot, IEnumerable<string> hiddenRows = null)
        {
            Plugin = plugin;
            InputFile = inputFile;
            Slot = slot;
            _hiddenRows = new HashSet<string>(hiddenRows ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        }
        public InstalledPlugin Plugin { get; }
        public string InputFile { get; }
        public int Slot { get; }
        public bool IsHidden(string id) => id.Length > 0 && _hiddenRows.Contains(id);
    }

    internal sealed class PluginRow
    {
        public const string FontFamily = "Segoe MDL2 Assets";
        public const string DefaultIcon = "\uE772";

        public PluginRow(Reading reading)
        {
            Label = SingleLine(reading.Label, 120);
            Id = SingleLine(reading.Id, 240);
            Value = SingleLine(reading.Value, 120);
            TaskbarValue = SingleLine(reading.Value, 5);
            Detail = SingleLine(reading.Detail, 240);
            OnTaskbar = reading.ShowOnTaskbar != false && TaskbarValue.Length > 0;
            Glyph = reading.Icon switch
            {
                "battery" => "\uE855", "keyboard" => "\uE765", "mouse" => "\uE962",
                "headphones" => "\uE7F6", "gamepad" => "\uE7FC", "time" => "\uE823",
                "power" => "\uE83E", _ => DefaultIcon,
            };
        }
        public string Label { get; }
        public string Id { get; }
        public string Value { get; }
        public string TaskbarValue { get; }
        public string Detail { get; }
        public string Glyph { get; }
        public bool OnTaskbar { get; }

        internal static string SingleLine(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            value = value.Trim();
            return new string(value.Select(ch => char.IsControl(ch) ? ' ' : ch).Take(max).ToArray());
        }
    }

    internal sealed class PluginSection
    {
        private readonly PluginActivation _activation;

        public PluginSection(PluginActivation activation, string status, IReadOnlyList<PluginRow> rows = null)
        {
            _activation = activation;
            Id = activation.Plugin.Manifest.Id;
            Name = activation.Plugin.Name;
            Slot = activation.Slot;
            Status = status ?? "";
            Rows = rows ?? Array.Empty<PluginRow>();
            TaskbarRows = Array.AsReadOnly(Rows.Where(IsShown).ToArray());
        }
        public string Id { get; }
        public int Slot { get; }
        public string Name { get; }
        public string Status { get; }
        public IReadOnlyList<PluginRow> Rows { get; }
        public IReadOnlyList<PluginRow> TaskbarRows { get; }
        public int OverlayRows => Math.Min(TaskbarRows.Count, PluginSnapshot.MaxRowsPerPlugin);
        public bool IsShown(PluginRow row) => row.OnTaskbar && !_activation.IsHidden(row.Id);

        public PluginSection WithVisibility(PluginActivation activation) =>
            ReferenceEquals(_activation, activation) ? this : new PluginSection(activation, Status, Rows);
    }

    internal sealed class PluginSnapshot
    {
        public const int MaxRowsPerPlugin = 4;
        public static readonly PluginSnapshot Empty = new PluginSnapshot(Array.Empty<PluginSection>());
        private readonly Dictionary<int, PluginSection> _sectionsBySlot;

        public PluginSnapshot(PluginSection[] sections)
        {
            Sections = Array.AsReadOnly(sections);
            _sectionsBySlot = sections.ToDictionary(section => section.Slot);
            OverlayRows = sections.Sum(section => section.OverlayRows);
        }
        public IReadOnlyList<PluginSection> Sections { get; }
        public int OverlayRows { get; }

        public PluginSection SectionFor(int slot) => _sectionsBySlot.TryGetValue(slot, out var section) ? section : null;
        public int RowCount(int slot) => SectionFor(slot)?.OverlayRows ?? 0;

        public bool HasSameGeometry(PluginSnapshot other) => OverlayRows == other.OverlayRows
            && Sections.All(section => section.OverlayRows == other.RowCount(section.Slot));
    }

    internal sealed class PluginSampler
    {
        private static readonly SemaphoreSlim Workers = new SemaphoreSlim(3);
        private PluginActivation[] _configuration;
        private readonly List<Poll> _polls = new List<Poll>();
        private PluginSnapshot _snapshot = PluginSnapshot.Empty;

        public PluginSnapshot Sample(PluginActivation[] configuration)
        {
            bool changed = _configuration != configuration;
            if (changed) Reconcile(configuration);
            long now = Stopwatch.GetTimestamp();
            foreach (var poll in _polls)
            {
                if (poll.Pending != null && poll.Pending.IsCompleted)
                {
                    poll.Section = poll.Pending.GetAwaiter().GetResult().WithVisibility(poll.Activation);
                    poll.Pending = null;
                    changed = true;
                }
                if (poll.Pending == null && now >= poll.Next)
                {
                    poll.Next = now + Stopwatch.Frequency * poll.Activation.Plugin.Manifest.IntervalSeconds;
                    var activation = poll.Activation;
                    var cancellation = poll.Cancellation.Token;
                    poll.Pending = Task.Run(() => ReadAsync(activation, cancellation));
                }
            }
            if (changed)
                _snapshot = _polls.Count == 0 ? PluginSnapshot.Empty
                    : new PluginSnapshot(_polls.Select(poll => poll.Section).ToArray());
            return _snapshot;
        }

        private void Reconcile(PluginActivation[] configuration)
        {
            var retained = new List<Poll>();
            foreach (var activation in configuration)
            {
                int found = _polls.FindIndex(poll =>
                    poll.Activation.Slot == activation.Slot && poll.Activation.Plugin.SameFile(activation.Plugin)
                    && poll.Activation.InputFile == activation.InputFile);
                if (found < 0) retained.Add(new Poll(activation));
                else
                {
                    var poll = _polls[found];
                    poll.Activation = activation;
                    poll.Section = poll.Section.WithVisibility(activation);
                    retained.Add(poll);
                    _polls.RemoveAt(found);
                }
            }
            foreach (var poll in _polls) poll.Stop();
            _polls.Clear();
            _polls.AddRange(retained);
            _configuration = configuration;
        }

        public void Shutdown()
        {
            if (_configuration == null) return;
            foreach (var poll in _polls) poll.Stop();
            _polls.Clear();
            _configuration = null;
            _snapshot = PluginSnapshot.Empty;
        }

        private static async Task<PluginSection> ReadAsync(PluginActivation activation, CancellationToken cancellation)
        {
            var plugin = activation.Plugin;
            try
            {
                await Workers.WaitAsync(cancellation).ConfigureAwait(false);
                byte[] result;
                try
                {
                    result = await PluginProbe.ReadAsync(plugin.Exe, activation.InputFile, cancellation).ConfigureAwait(false);
                }
                finally { Workers.Release(); }

                if (result == null || result.Length == 0) return new PluginSection(activation, "插件未返回数据");
                Report report;
                using (var stream = new MemoryStream(result, writable: false)) report = Protocol.Read<Report>(stream);
                if (report?.Readings == null || report.Readings.Length > 64)
                    throw new InvalidDataException("插件返回的数据不符合协议");

                var rows = report.Readings.Where(row => !string.IsNullOrWhiteSpace(row?.Label))
                    .Select(row => new PluginRow(row)).ToArray();
                return new PluginSection(activation, PluginRow.SingleLine(report.Message, 240), Array.AsReadOnly(rows));
            }
            catch (Exception ex)
            {
                if (!cancellation.IsCancellationRequested)
                    Logger.WarnOnce("plugin-" + plugin.Manifest.Id + "-" + ex.GetType().Name,
                        $"插件 {plugin.Manifest.Id} 采集失败", ex);
                return new PluginSection(activation, cancellation.IsCancellationRequested ? "" : "采集失败，请查看日志");
            }
        }

        private sealed class Poll
        {
            public Poll(PluginActivation activation)
            {
                Activation = activation;
                Section = new PluginSection(activation, "等待读取…");
            }
            public PluginActivation Activation;
            public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            public long Next;
            public PluginSection Section;
            public Task<PluginSection> Pending;

            public void Stop()
            {
                Cancellation.Cancel();
                if (Pending == null || Pending.IsCompleted) Cancellation.Dispose();
                else Pending.ContinueWith(_ => Cancellation.Dispose(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }
}
