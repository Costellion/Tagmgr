using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace Tagmgr
{
    public sealed partial class SettingsPage : Page
    {
        // 页面初始化期间，抑制 ComboBox 的 SelectionChanged
        private bool _isInitializing;

        public SettingsPage()
        {
            this.InitializeComponent();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;

            await DataService.EnsureLoadedAsync();
            RefreshAll();

            _isInitializing = false;
        }

        // 刷新所有界面状态
        private void RefreshAll()
        {
            // 主题下拉框
            var theme = AppSettings.Theme;
            ThemeComboBox.SelectedIndex = theme switch
            {
                ElementTheme.Light => 1,
                ElementTheme.Dark => 2,
                _ => 0
            };

            // 数据路径
            DataPathText.Text = DataService.DataFile;

            // 统计
            FileCountText.Text = $"文件记录：{DataService.FileItems.Count} 条";

            var tagCount = DataService.FileItems
                .SelectMany(f => f.Tags)
                .Distinct()
                .Count();

            TagCountText.Text = $"标签数量：{tagCount} 个";
        }

        // 主题切换
        private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (ThemeComboBox.SelectedItem is not ComboBoxItem item) return;
            if (item.Tag is not string tag) return;

            AppSettings.Theme = tag switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }

        // 修改数据存储位置
        private async void ChangeDataFolder_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            var mainWindow = ((App)Application.Current).MainWindow!;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder == null) return;

            var ok = await DataService.ChangeDataFolderAsync(folder.Path);

            if (ok)
            {
                RefreshAll();
                await ShowMessageAsync(
                    $"数据位置已更新，原有数据已复制到新位置。\n\n当前数据文件：\n{DataService.DataFile}");
            }
            else
            {
                await ShowMessageAsync("修改数据位置失败，请检查路径是否有效、是否有写入权限。");
            }
        }

        // 恢复默认位置
        private async void ResetDataFolder_Click(object sender, RoutedEventArgs e)
        {
            var defaultFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tagmgr");

            if (string.Equals(DataService.DataFolder, defaultFolder, StringComparison.OrdinalIgnoreCase))
            {
                await ShowMessageAsync("当前已经在使用默认位置。");
                return;
            }

            var ok = await DataService.ChangeDataFolderAsync(defaultFolder);

            if (ok)
            {
                RefreshAll();
                await ShowMessageAsync(
                    $"已恢复到默认位置。\n\n当前数据文件：\n{DataService.DataFile}");
            }
            else
            {
                await ShowMessageAsync("恢复默认位置失败。");
            }
        }

        // 打开数据文件夹
        private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folder = DataService.DataFolder;
                Directory.CreateDirectory(folder);

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = folder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _ = ShowMessageAsync($"无法打开文件夹：{ex.Message}");
            }
        }

        private async Task ShowMessageAsync(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "提示",
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };

            await dialog.ShowAsync();
        }
    }
}