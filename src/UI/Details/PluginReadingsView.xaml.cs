using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace deskmonitor
{
    public sealed partial class PluginReadingsView : UserControl
    {
        private PluginSection _section;
        internal event Action<string, string, bool> RowVisibilityChanged;

        public PluginReadingsView() => InitializeComponent();

        internal bool Refresh(PluginSection section)
        {
            if (ReferenceEquals(section, _section)) return false;
            _section = section;
            Readings.ItemsSource = section?.Rows.Select(row => new DisplayReading(row, section.IsShown(row)));
            return true;
        }

        private void ShowReading_Click(object sender, RoutedEventArgs e)
        {
            if (_section != null && sender is CheckBox box && box.DataContext is DisplayReading reading)
                RowVisibilityChanged?.Invoke(_section.Id, reading.Row.Id, box.IsChecked == true);
        }

        private sealed class DisplayReading
        {
            public DisplayReading(PluginRow row, bool shown) { Row = row; Shown = shown; }
            public PluginRow Row { get; }
            public bool Shown { get; }
            public bool Selectable => Row.OnTaskbar && Row.Id.Length > 0;
        }
    }
}
