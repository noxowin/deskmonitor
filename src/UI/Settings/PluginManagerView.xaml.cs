using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace deskmonitor
{
    public sealed partial class PluginManagerView : UserControl
    {
        private readonly PluginCatalog _catalog;
        private readonly List<PluginPreference> _preferences;
        private readonly Action _save;
        private PluginSnapshot _snapshot = PluginSnapshot.Empty;
        private bool _updating;

        internal PluginManagerView(PluginCatalog catalog, List<PluginPreference> preferences, Action save,
            Action<string, string, bool> changeRowVisibility)
        {
            _catalog = catalog;
            _preferences = preferences;
            _save = save;
            InitializeComponent();
            PluginReadings.RowVisibilityChanged += changeRowVisibility;
            Reload();
        }

        private InstalledPlugin Selected => PluginList?.SelectedItem as InstalledPlugin;

        internal void ReloadCatalog() => Reload(Selected?.Manifest.Id);

        private bool IsPluginEnabled(InstalledPlugin plugin) => Preference(plugin)?.Enabled ?? plugin.SingleFile;

        private PluginPreference Preference(InstalledPlugin plugin, bool create = false)
        {
            var saved = _preferences.Find(item => item?.Id == plugin.Manifest.Id);
            if (saved == null && create)
            {
                saved = new PluginPreference { Id = plugin.Manifest.Id };
                _preferences.Add(saved);
            }
            return saved;
        }

        private void Reload(string selected = null)
        {
            try
            {
                var plugins = _catalog.List();
                PluginList.ItemsSource = plugins;
                EmptyText.Visibility = plugins.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                InstalledPlugins.Visibility = plugins.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
                PluginList.SelectedItem = plugins.FirstOrDefault(plugin => plugin.Manifest.Id == selected)
                    ?? plugins.FirstOrDefault();
                ShowSelection();
            }
            catch (Exception ex) { SetOperation(ex.Message); }
        }

        private void PluginList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowSelection();

        private void ShowSelection()
        {
            var plugin = Selected;
            PluginInfo.Visibility = plugin == null ? Visibility.Collapsed : Visibility.Visible;
            if (plugin == null)
            {
                PluginReadings.Refresh(null);
                return;
            }
            var saved = Preference(plugin);
            NameText.Text = plugin.Name;
            DescriptionText.Text = plugin.Manifest.Description ?? "";
            _updating = true;
            EnabledSwitch.IsOn = IsPluginEnabled(plugin);
            _updating = false;
            InputPanel.Visibility = plugin.Manifest.InputFileName == null ? Visibility.Collapsed : Visibility.Visible;
            InputLabel.Text = plugin.Manifest.InputFileLabel ?? "输入文件（可选）";
            InputPathText.Text = saved?.InputFile ?? "尚未选择";
            ClearInputButton.IsEnabled = saved?.InputFile != null;
            RefreshStatus(_snapshot);
        }

        internal void RefreshStatus(PluginSnapshot snapshot)
        {
            _snapshot = snapshot;
            var plugin = Selected;
            if (plugin == null) return;
            bool enabled = IsPluginEnabled(plugin);
            var section = enabled ? snapshot.Sections.FirstOrDefault(item => item.Id == plugin.Manifest.Id) : null;
            string status = !enabled ? "已停用"
                : section == null ? "等待读取…"
                : section.Status.Length > 0 ? section.Status : $"已读取 {section.Rows.Count} 项";
            if (StatusText.Text != status) StatusText.Text = status;
            PluginReadings.Refresh(section);
            PluginReadings.Visibility = section?.Rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            var picker = new OpenFileDialog
            {
                Filter = "DeskMonitor 插件 (*.plugin;*.zip)|*.plugin;*.zip", CheckFileExists = true,
            };
            if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
            InstallButton.IsEnabled = false;
            try
            {
                var plugin = await Task.Run(() => _catalog.Install(picker.FileName));
                Preference(plugin, true).Enabled = true;
                _save();
                Reload(plugin.Manifest.Id);
                SetOperation($"已安装 {plugin.Name}。");
            }
            catch (Exception ex) { SetOperation("安装失败：" + ex.Message); }
            finally { InstallButton.IsEnabled = true; }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _catalog.EnsureRoot();
                Process.Start(new ProcessStartInfo(_catalog.Root)
                {
                    UseShellExecute = true,
                })?.Dispose();
            }
            catch (Exception ex) { SetOperation("无法打开插件目录：" + ex.Message); }
        }

        private void Enabled_Toggled(object sender, RoutedEventArgs e)
        {
            if (_updating || Selected == null) return;
            Preference(Selected, true).Enabled = EnabledSwitch.IsOn;
            _save();
            RefreshStatus(_snapshot);
        }

        private void ChooseInput_Click(object sender, RoutedEventArgs e)
        {
            var plugin = Selected;
            string name = plugin?.Manifest.InputFileName;
            if (name == null) return;
            var picker = new OpenFileDialog { FileName = name, Filter = $"{name}|{name}", CheckFileExists = true };
            if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
            if (!string.Equals(Path.GetFileName(picker.FileName), name, StringComparison.OrdinalIgnoreCase))
            {
                SetOperation($"请选择 {name}。");
                return;
            }
            Preference(plugin, true).InputFile = picker.FileName;
            _save();
            SetOperation("");
            ShowSelection();
        }

        private void ClearInput_Click(object sender, RoutedEventArgs e)
        {
            if (Selected == null) return;
            Preference(Selected, true).InputFile = null;
            _save();
            ShowSelection();
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            var plugin = Selected;
            if (plugin == null || MessageBox.Show(Window.GetWindow(this), $"要移除「{plugin.Name}」吗？", "移除插件",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            try
            {
                _catalog.Remove(plugin);
                _preferences.RemoveAll(item => item?.Id == plugin.Manifest.Id);
                if (_catalog.List().Any(installed => installed.Manifest.Id == plugin.Manifest.Id))
                    Preference(plugin, true).Enabled = false;
                _save();
                Reload();
                SetOperation($"已移除 {plugin.Name}。");
            }
            catch (Exception ex) { SetOperation("移除失败：" + ex.Message); }
        }

        private void SetOperation(string text)
        {
            OperationText.Text = text;
            OperationText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
