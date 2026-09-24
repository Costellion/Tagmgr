using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
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

            // 数据路径（废弃）
            //DataPathText.Text = DataService.DataFile;

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
        private async void ExportData_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"tagmgr-backup-{DateTime.Now:yyyyMMdd-HHmm}"
            };
            picker.FileTypeChoices.Add("Tagmgr 备份", new List<string> { ".db" });

            var mainWindow = ((App)Application.Current).MainWindow!;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            StorageFile? file;
            try
            {
                file = await picker.PickSaveFileAsync();
            }
            catch (Exception ex)
            {
                await ShowMessageAsync($"无法打开保存对话框：{ex.Message}");
                return;
            }

            if (file == null) return;

            var ok = await DataService.ExportAsync(file.Path);

            if (ok)
                await ShowMessageAsync($"已导出到：\n{file.Path}");
            else
                await ShowMessageAsync("导出失败。请确认目标位置可写，或换一个位置重试。");
        }

        // 导入数据
        private async void ImportData_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeFilter.Add(".db");

            var mainWindow = ((App)Application.Current).MainWindow!;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            StorageFile? file;
            try
            {
                file = await picker.PickSingleFileAsync();
            }
            catch (Exception ex)
            {
                await ShowMessageAsync($"无法打开文件对话框：{ex.Message}");
                return;
            }

            if (file == null) return;

            // 二次确认：导入会覆盖当前数据
            var confirm = new ContentDialog
            {
                Title = "确认导入",
                Content = $"导入会用所选文件覆盖当前数据，且无法撤销。\n\n所选文件：\n{file.Path}\n\n是否继续？",
                PrimaryButtonText = "导入并覆盖",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
                return;

            var ok = await DataService.ImportAsync(file.Path);

            if (ok)
            {
                RefreshAll();
                await ShowMessageAsync("导入成功，数据已更新。");
            }
            else
            {
                await ShowMessageAsync(
                    "导入失败。请确认所选文件是有效的 Tagmgr 数据库备份。");
            }
        }
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