using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage.Pickers;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Tagmgr
{
    public sealed partial class MainWindow : Window
    {
        // 界面上显示的所有文件记录
        public ObservableCollection<FileTagItem> FileItems { get; } = new();

        // 数据保存位置
        private readonly string _dataFolder;
        private readonly string _dataFile;

        public MainWindow()
        {
            this.InitializeComponent();

            // 保存到：C:\Users\你的用户名\AppData\Local\Tagmgr\tags.json
            _dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tagmgr");

            Directory.CreateDirectory(_dataFolder);
            _dataFile = Path.Combine(_dataFolder, "tags.json");
        }

        // 页面加载完成后，读取本地保存的标签数据
        private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadDataAsync();
        }

        // 选择文件
        private async void PickFile_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();

            // WinUI 3 桌面程序必须初始化窗口句柄，否则 FileOpenPicker 会报错
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.FileTypeFilter.Add("*");
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            var files = await picker.PickMultipleFilesAsync();
            if (files == null) return;

            foreach (var file in files)
            {
                // 如果这个文件已经添加过，就跳过
                if (FileItems.Any(x => x.FilePath == file.Path))
                    continue;

                FileItems.Add(new FileTagItem
                {
                    FilePath = file.Path
                });
            }

            await SaveDataAsync();
        }

        // 给当前选中的文件添加标签
        private async void AddTag_Click(object sender, RoutedEventArgs e)
        {
            if (FileListView.SelectedItem is not FileTagItem item)
            {
                await ShowMessageAsync("请先在左侧选中一个文件。");
                return;
            }

            var tag = TagInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(tag))
                return;

            if (!item.Tags.Contains(tag))
            {
                item.Tags.Add(tag);
            }

            TagInput.Text = "";
            await SaveDataAsync();
        }

        // 从当前选中的文件移除标签
        private async void RemoveTag_Click(object sender, RoutedEventArgs e)
        {
            if (FileListView.SelectedItem is not FileTagItem item)
            {
                await ShowMessageAsync("请先在左侧选中一个文件。");
                return;
            }

            var tag = TagInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(tag))
                return;

            if (item.Tags.Contains(tag))
            {
                item.Tags.Remove(tag);
                await SaveDataAsync();
            }
        }

        // 删除当前选中的文件记录
        private async void DeleteFile_Click(object sender, RoutedEventArgs e)
        {
            if (FileListView.SelectedItem is FileTagItem item)
            {
                FileItems.Remove(item);
                await SaveDataAsync();
            }
        }

        // 从本地 JSON 文件读取数据
        private async Task LoadDataAsync()
        {
            if (!File.Exists(_dataFile))
                return;

            try
            {
                var json = await File.ReadAllTextAsync(_dataFile);
                var list = JsonSerializer.Deserialize<List<FileTagItem>>(json);

                if (list == null)
                    return;

                FileItems.Clear();

                foreach (var item in list)
                {
                    FileItems.Add(item);
                }
            }
            catch (Exception ex)
            {
                await ShowMessageAsync($"读取数据失败：{ex.Message}");
            }
        }

        // 把数据保存到本地 JSON 文件
        private async Task SaveDataAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(
                    FileItems,
                    new JsonSerializerOptions { WriteIndented = true });

                await File.WriteAllTextAsync(_dataFile, json);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync($"保存数据失败：{ex.Message}");
            }
        }

        // 显示简单提示框
        private async Task ShowMessageAsync(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "提示",
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = RootGrid.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "关于",
                Content = "Made by Costellion \nBuild 0.0.1 (2026/9/19)",
                CloseButtonText = "确定",
                XamlRoot = RootGrid.XamlRoot
            };

            _ = dialog.ShowAsync();
            return;
        }
    }
}