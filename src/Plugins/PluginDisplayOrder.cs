using System;
using System.Collections.Generic;
using System.Linq;

namespace deskmonitor
{
    // Runtime slots stay stable while DeskMonitor is open. Settings persist stable
    // plugin IDs so reinstalling or discovering a plugin in a different order is harmless.
    internal sealed class PluginDisplayOrder
    {
        private static readonly string[] BuiltinKeys = { "cpu", "ram", "disk", "gpu", "network" };
        private readonly Dictionary<string, int> _slotById = new Dictionary<string, int>(StringComparer.Ordinal);
        private IReadOnlyDictionary<int, InstalledPlugin> _installed = new Dictionary<int, InstalledPlugin>();
        private int _nextSlot = TaskbarWindow.FirstPluginSlot;
        private int[] _defaults = Enumerable.Range(0, BuiltinKeys.Length).ToArray();

        public IReadOnlyList<int> Defaults => _defaults;
        public IReadOnlyDictionary<int, string> Names { get; private set; } = new Dictionary<int, string>();

        public int SlotFor(InstalledPlugin plugin) => _slotById[plugin.Manifest.Id];

        public void Update(IReadOnlyList<InstalledPlugin> plugins)
        {
            var installed = new Dictionary<int, InstalledPlugin>();
            var defaults = new List<int>(Enumerable.Range(0, BuiltinKeys.Length));
            foreach (var plugin in plugins)
            {
                if (!_slotById.TryGetValue(plugin.Manifest.Id, out int slot))
                {
                    slot = _nextSlot++;
                    _slotById.Add(plugin.Manifest.Id, slot);
                }
                installed.Add(slot, plugin);
                defaults.Add(slot);
            }
            _installed = installed;
            _defaults = defaults.ToArray();
            Names = installed.ToDictionary(pair => pair.Key, pair => pair.Value.Name);
        }

        public string KeyFor(int slot)
        {
            if (slot >= 0 && slot < BuiltinKeys.Length) return BuiltinKeys[slot];
            return _installed.TryGetValue(slot, out var plugin) ? "plugin:" + plugin.Manifest.Id : null;
        }

        public int[] Resolve(IReadOnlyList<string> saved, IReadOnlyList<int> legacy)
        {
            var order = new List<int>();
            if (saved != null)
            {
                var slots = _defaults.ToDictionary(KeyFor, slot => slot, StringComparer.Ordinal);
                foreach (string key in saved)
                    if (key != null && slots.TryGetValue(key, out int slot)) order.Add(slot);
            }
            else if (legacy != null)
            {
                foreach (int slot in legacy)
                {
                    if (slot >= 0 && slot < BuiltinKeys.Length) order.Add(slot);
                    else if (slot == BuiltinKeys.Length) order.AddRange(_defaults.Skip(BuiltinKeys.Length));
                }
            }
            return order.Concat(_defaults).Distinct().ToArray();
        }
    }
}
