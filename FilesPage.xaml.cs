using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace Tagmgr
{
    public sealed partial class FilesPage : Page
    {
        // 指向 DataService 的共享集合
        public ObservableCollection<FileTagItem> FileItems => DataService.FileItems;

        public FilesPage()
        {
            this.InitializeComponent();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            await DataService.EnsureLoadedAsync();

            // 为已有记录加载图标
            foreach (var item in FileItems)
                _ = item.LoadIconAsync();
        }

        // 选择文件
        private async void PickFile_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();

            var mainWindow = ((App)Application.Current).MainWindow!;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.FileTypeFilter.Add("*");
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            var files = await picker.PickMultipleFilesAsync();
            if (files == null) return;

            foreach (var file in files)
            {
                if (FileItems.Any(x => x.FilePath == file.Path))
                    continue;

                var item = new FileTagItem { FilePath = file.Path };
                FileItems.Add(item);

                // 异步加载图标，不阻塞界面
                _ = item.LoadIconAsync();
            }

            await DataService.SaveAsync();
        }

        // 获取当前窗口（用于 FileOpenPicker 初始化）
        private Window GetWindow()
        {
            // 在 WinUI 3 里，Page 没有直接暴露 Window，
            // 通过 App 保存的引用或 XamlRoot 拿到
            return ((App)Application.Current).MainWindow!;
        }

        // 添加标签
        private async void AddTag_Click(object sender, RoutedEventArgs e)
        {
            var selected = FileListView.SelectedItems
                .Cast<FileTagItem>()
                .ToList();

            if (selected.Count == 0)
            {
                await ShowMessageAsync("请先在左侧选中至少一个文件。");
                return;
            }

            var tag = TagInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(tag)) return;

            foreach (var item in selected)
            {
                if (!item.Tags.Contains(tag))
                    item.Tags.Add(tag);
            }

            TagInput.Text = "";
            await DataService.SaveAsync();
        }

        private async void RemoveTag_Click(object sender, RoutedEventArgs e)
        {
            var selected = FileListView.SelectedItems
                .Cast<FileTagItem>()
                .ToList();

            if (selected.Count == 0)
            {
                await ShowMessageAsync("请先在左侧选中至少一个文件。");
                return;
            }

            var tag = TagInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(tag)) return;

            foreach (var item in selected)
                item.Tags.Remove(tag);
            
            TagInput.Text = "";
            await DataService.SaveAsync();
        }
        private async void RemoveTagChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;

            var tag = btn.Tag as string;
            if (string.IsNullOrEmpty(tag)) return;

            // 找到这个 chip 所属的文件
            // 从视觉树往上找 ListViewItem
            var element = btn as FrameworkElement;
            while (element != null && element is not ListViewItem)
                element = VisualTreeHelper.GetParent(element) as FrameworkElement;

            if (element is not ListViewItem lvi) return;
            if (lvi.Content is not FileTagItem item) return;

            if (item.Tags.Remove(tag))
                await DataService.SaveAsync();
        }
        private async void DeleteFile_Click(object sender, RoutedEventArgs e)
        {
            // 先复制一份，避免遍历时修改集合
            var selected = FileListView.SelectedItems
                .Cast<FileTagItem>()
                .ToList();

            if (selected.Count == 0) return;

            foreach (var item in selected)
                FileItems.Remove(item);

            await DataService.SaveAsync();
        }

        // 简单提示框
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
    }
}