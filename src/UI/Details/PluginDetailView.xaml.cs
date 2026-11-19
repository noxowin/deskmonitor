using System;
using System.Windows;
using System.Windows.Controls;

namespace deskmonitor
{
    public sealed partial class PluginDetailView : UserControl, IDetailView
    {
        private readonly int _slot;

        internal PluginDetailView(int slot, Action<string, string, bool> changeVisibility)
        {
            _slot = slot;
            InitializeComponent();
            Readings.RowVisibilityChanged += changeVisibility;
        }
        public ContentControl PinSlot => PinSlotHost;
        public void ApplyTheme(bool dark) { } // Brushes follow the live theme resources.

        void IDetailView.Refresh(SystemSnapshot snapshot)
        {
            var section = snapshot.Plugins.SectionFor(_slot);
            if (!Readings.Refresh(section)) return;
            if (section != null) TitleText.Text = section.Name;
            string status = section?.Status;
            if (string.IsNullOrEmpty(status) && (section?.Rows.Count ?? 0) == 0) status = "暂无数据";
            StatusText.Text = status;
            StatusText.Visibility = string.IsNullOrEmpty(status) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
