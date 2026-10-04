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
            RefreshLanguageCombo();
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
            // 数据路径（废弃）
            //DataPathText.Text = DataService.DataFile;

            // 统计
            var CountTxt1_1 =loader.GetString("SettingsCountText/Text1");
            var CountTxt2_1 = loader.GetString("SettingsCountText/Text2");
            //量词被删了 var CountTxt1_2 =loader.GetString("SettingsCountText/Text2");
            FileCountText.Text = $"{CountTxt1_1}：{DataService.FileItems.Count}";

            var tagCount = DataService.FileItems
                .SelectMany(f => f.Tags)
                .Distinct()
                .Count();

            TagCountText.Text = $"{CountTxt2_1}：{tagCount}";
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
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
            var ExportTxt1 = loader.GetString("SettingsExportNotice/Text1");
            var ExportTxt2 = loader.GetString("SettingsExportNotice/Text2");
            var ExportTxt3 = loader.GetString("SettingsExportNotice/Text3");
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"tagmgr-backup-{DateTime.Now:yyyyMMdd-HHmm}"
            };
            var backupType = loader.GetString("SettingsBackupFileType/Text");
            picker.FileTypeChoices.Add(backupType, new List<string> { ".db" });
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
                await UiService.ShowMessageAsync(this.XamlRoot, $"{ExportTxt3}：{ex.Message}");
                return;
            }

            if (file == null) return;

            var ok = await DataService.ExportAsync(file.Path);

            if (ok)
                await UiService.ShowMessageAsync(this.XamlRoot, $"{ExportTxt1}：\n{file.Path}");
            else
                await UiService.ShowMessageAsync(this.XamlRoot, $"{ExportTxt2}");
        }

        // 导入数据
        private async void ImportData_Click(object sender, RoutedEventArgs e)
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
            var ImportTxt1 = loader.GetString("SettingsImportNotice/Text1");
            var ImportTxt2 = loader.GetString("SettingsImportNotice/Text2");
            var ImportTxt3 = loader.GetString("SettingsImportNotice/Text3");
            var ImportTxt4 = loader.GetString("SettingsImportNotice/Text4");
            var ImportTxt5 = loader.GetString("SettingsImportNotice/Text5");
            var ImportTxt6 = loader.GetString("SettingsImportNotice/Text6");
            var ImportTxt7 = loader.GetString("SettingsImportNotice/Text7");
            var ImportTxt8 = loader.GetString("SettingsImportNotice/Text8");
            var cancelTxt = loader.GetString("GlobalNotice/Cancel");
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
                await UiService.ShowMessageAsync(this.XamlRoot, $"{ImportTxt7}{ex.Message}");
                return;
            }

            if (file == null) return;

            // 二次确认：导入会覆盖当前数据
            var confirm = new ContentDialog
            {
                Title = ImportTxt1,
                Content = $"{ImportTxt3}\n\n{ImportTxt4}：\n{file.Path}\n\n{ImportTxt5}",
                PrimaryButtonText = ImportTxt6,
                CloseButtonText = cancelTxt,
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
                return;

            var ok = await DataService.ImportAsync(file.Path);

            if (ok)
            {
                RefreshAll();
                await UiService.ShowMessageAsync(this.XamlRoot, ImportTxt2);
            }
            else
            {
                await UiService.ShowMessageAsync(this.XamlRoot, ImportTxt8);
            }
        }
        /* 由于 UWP 沙箱限制，无法直接打开应用数据文件夹，因此暂时禁用此功能
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
                _ = UiService.ShowMessageAsync(this.XamlRoot, $"无法打开文件夹：{ex.Message}");
            }
        }*/
        private async void CheckFiles_Click(object sender, RoutedEventArgs e)
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader();

            var NoFileTxt = loader.GetString("SettingsMaintenanceTxt/Nofile");
            var AllValidTxt = loader.GetString("SettingsMaintenanceTxt/AllValid");
            var MoreItemsTxt = loader.GetString("SettingsMaintenanceTxt/MoreItems");
            var MissTitleTxt = loader.GetString("SettingsMaintenanceTxt/MissingTitle");
            var MissBodyTxt = loader.GetString("SettingsMaintenanceTxt/MissingContent");
            var CleanTxt = loader.GetString("SettingsMaintenanceTxt/Clean");
            var KeepTxt = loader.GetString("SettingsMaintenanceTxt/Keep");

            if (CheckFilesButton.IsEnabled == false) return;

            var total = DataService.FileItems.Count;
            if (total == 0)
            {
                await UiService.ShowMessageAsync(this.XamlRoot, NoFileTxt);
                return;
            }

            CheckFilesButton.IsEnabled = false;

            try
            {
                var missing = new List<FileTagItem>();
                var sync = new object();

                // 限制并发数，避免一次性同时打开过多文件句柄
                using var throttle = new System.Threading.SemaphoreSlim(16);

                var tasks = DataService.FileItems
                    .Select(async item =>
                    {
                        await throttle.WaitAsync();
                        try
                        {
                            var ok = await item.CheckExistsAsync();
                            if (!ok)
                            {
                                lock (sync)
                                {
                                    missing.Add(item);
                                }
                            }
                        }
                        finally
                        {
                            throttle.Release();
                        }
                    })
                    .ToList();

                await Task.WhenAll(tasks);

                if (missing.Count == 0)
                {
                    await UiService.ShowMessageAsync(this.XamlRoot, string.Format(AllValidTxt, total));
                    return;
                }

                // 预览前 10 个失效文件
                var preview = string.Join("\n", missing.Take(10).Select(f => "· " + f.FileName));
                if (missing.Count > 10)
                    preview += "\n" + string.Format(MoreItemsTxt, missing.Count - 10);

                var dialog = new ContentDialog
                {
                    Title = MissTitleTxt,
                    Content = string.Format(MissBodyTxt, total, missing.Count, preview),
                    PrimaryButtonText = string.Format(CleanTxt, missing.Count),
                    CloseButtonText = KeepTxt,
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    // 走撤销服务，用户可以用 Ctrl+Z 恢复
                    await UndoService.ExecuteAsync(new DeleteFilesCommand(missing));
                    RefreshAll();
                }
            }
            finally
            {
                CheckFilesButton.IsEnabled = true;
            }
        }
        // 根据保存的设置，选中对应的下拉项
        private void RefreshLanguageCombo()
        {
            var lang = AppSettings.Language;

            if (string.IsNullOrEmpty(lang))
            {
                LanguageComboBox.SelectedIndex = 0;   // 跟随系统
            }
            else if (lang.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                LanguageComboBox.SelectedIndex = 1;   // 简体中文
            }
            else if (lang.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                LanguageComboBox.SelectedIndex = 2;   // English
            }
            else
            {
                LanguageComboBox.SelectedIndex = 0;
            }
        }

        // 语言切换
        private async void Language_SelectionChanged(
            object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (LanguageComboBox.SelectedItem is not ComboBoxItem item) return;
            if (item.Tag is not string tag) return;

            // "System" 转成空字符串，表示跟随系统
            var newLang = tag == "System" ? "" : tag;

            // 没变化就不处理
            if (newLang == AppSettings.Language) return;
            AppSettings.Language = newLang;
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
            await UiService.ShowMessageAsync(XamlRoot, loader.GetString("SettingsLanguageSwitch/Text"));
        }
    }
}