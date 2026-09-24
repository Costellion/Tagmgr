using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace Tagmgr
{
    public sealed partial class FilesPage : Page
    {
        // 指向 DataService 的共享集合
        public ObservableCollection<FileTagItem> FileItems => DataService.FileItems;
        public ObservableCollection<FileTagItem> DisplayItems { get; } = new();
        private FileSortMode CurrentSortMode
        {
            get
            {
                if (SortComboBox?.SelectedItem is ComboBoxItem item &&
                    item.Tag is string tag &&
                    Enum.TryParse<FileSortMode>(tag, out var mode))
                    return mode;

                return FileSortMode.NameAsc;
            }
        }
        public FilesPage()
        {
            this.InitializeComponent();
            SortComboBox.SelectedIndex = 0;
        }
        // 按搜索文本+ 排序方式重建 DisplayItems
        private void RefreshDisplay()
        {
            var query = SearchBox?.Text?.Trim() ?? "";

            IEnumerable<FileTagItem> source = DataService.FileItems;

            if (!string.IsNullOrEmpty(query))
            {
                source = source.Where(f =>
                    f.FileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    f.FilePath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    f.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)));
            }
            source = DataService.ApplySort(source, CurrentSortMode);
            var result = source.ToList();

            // 保留原有选中项
            var selected = FileListView.SelectedItems
                .OfType<FileTagItem>()
                .ToHashSet();

            DisplayItems.Clear();
            foreach (var item in result)
                DisplayItems.Add(item);

            // 恢复选中
            foreach (var item in result)
            {
                if (selected.Contains(item))
                    FileListView.SelectedItems.Add(item);
            }
        }
        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            await DataService.EnsureLoadedAsync();

            // 订阅共享集合变化，增删时同步刷新显示
            DataService.FileItems.CollectionChanged -= FileItems_CollectionChanged;
            DataService.FileItems.CollectionChanged += FileItems_CollectionChanged;

            // 为已有记录加载图标
            var tasks = FileItems
                 .Select(item => item.LoadMetadataAsync())
                 .ToList();

            await Task.WhenAll(tasks);

            RefreshDisplay();
        }

        private void FileItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshDisplay();
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

            var newItems = new List<FileTagItem>();

            foreach (var file in files)
            {
                if (FileItems.Any(x => x.FilePath == file.Path))
                    continue;

                var item = new FileTagItem { FilePath = file.Path };
                FileItems.Add(item);
                newItems.Add(item);
            }

            // 加载新文件的元数据，加载完后再刷新一次排序
            await Task.WhenAll(newItems.Select(i => i.LoadMetadataAsync()));

            RefreshDisplay();
            await DataService.SaveAsync();
        }
        // 排序方式切换
        private void SortComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            RefreshDisplay();
        }
        // 搜索框内容变化
        private void SearchBox_TextChanged(
            AutoSuggestBox sender,
            AutoSuggestBoxTextChangedEventArgs args)
        {
            // 只处理用户输入，忽略程序赋值
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
                return;

            RefreshDisplay();
        }
        // 获取当前窗口（用于 FileOpenPicker 初始化）
        private Window GetWindow()
        {
            // 在 WinUI 3 里，Page 没有直接暴露 Window，
            // 通过 App 保存的引用或 XamlRoot 拿到
            return ((App)Application.Current).MainWindow!;
        }

        // 添加标签
        // 点击“添加标签”按钮
        private async void AddTag_Click(object sender, RoutedEventArgs e)
        {
            await AddTagAsync(TagInput.Text);
        }

        // 供按钮和自动补全共用
        private async Task AddTagAsync(string? rawTag)
        {
            var tag = (rawTag ?? "").Trim();
            if (string.IsNullOrWhiteSpace(tag)) return;

            var selected = FileListView.SelectedItems
                .OfType<FileTagItem>()
                .ToList();

            if (selected.Count == 0)
            {
                await ShowMessageAsync("请先在左侧选中至少一个文件。");
                return;
            }

            foreach (var item in selected)
            {
                if (!item.Tags.Contains(tag))
                    item.Tags.Add(tag);
            }

            TagInput.Text = "";
            await DataService.SaveAsync();
            RefreshDisplay();
        }

        // 自动补全：输入时提示已有标签
        private void TagInput_TextChanged(
            AutoSuggestBox sender,
            AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
                return;

            var query = (sender.Text ?? "").Trim();

            var all = DataService.FileItems
                .SelectMany(f => f.Tags)
                .Where(t => !string.IsNullOrEmpty(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!string.IsNullOrEmpty(query))
            {
                all = all
                    .Where(t => t.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            all = all
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();

            sender.ItemsSource = all;
        }

        // 用户从下拉里选了一个标签
        private void TagInput_SuggestionChosen(
            AutoSuggestBox sender,
            AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is string tag)
                sender.Text = tag;
        }

        // 用户按回车，或点击下拉里的项并回车
        private async void TagInput_QuerySubmitted(
            AutoSuggestBox sender,
            AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            var tag = string.IsNullOrWhiteSpace(args.QueryText)
                ? sender.Text
                : args.QueryText;

            await AddTagAsync(tag);
        }

        //移除标签
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
            RefreshDisplay();
        }
        //移除单个标签
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
            RefreshDisplay();
        }
        //移除选中文件
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
            //RefreshDisplay();
        }
        // 双击文件项打开文件
        private async void FileItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not FileTagItem item) return;

            await OpenFileAsync(item);
        }

        // 打开文件的实际逻辑，双击和右键菜单共用
        private async Task OpenFileAsync(FileTagItem item)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                var ok = await Launcher.LaunchFileAsync(file);

                if (!ok)
                    await ShowMessageAsync("系统没有可用来打开该文件的程序。");
            }
            catch (Exception ex)
            {
                await ShowMessageAsync(
                    $"无法打开文件：{ex.Message}\n路径：{item.FilePath}");
            }
        }
        private void FileItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not FileTagItem item) return;

            // 被右键的项如果不在选中列表里，先把选中切到它
            if (!FileListView.SelectedItems.Contains(item))
            {
                FileListView.SelectedItems.Clear();
                FileListView.SelectedItems.Add(item);
            }

            var menu = ContextMenuHelper.BuildFileMenu();

            // 给每个菜单项挂统一的动作处理器
            foreach (var mfi in menu.Items.OfType<MenuFlyoutItem>())
            {
                if (mfi.Tag is string action)
                    mfi.Click += (s, args) => _ = HandleFileMenuActionAsync(action);
            }

            menu.ShowAt(fe, e.GetPosition(fe));
        }

        // 统一处理右键菜单的动作
        private async Task HandleFileMenuActionAsync(string action)
        {
            var selected = FileListView.SelectedItems
                .OfType<FileTagItem>()
                .ToList();

            if (selected.Count == 0) return;

            switch (action)
            {
                case ContextMenuHelper.ActionOpen:
                    // “打开文件”只作用于被右键的那一个
                    await OpenFileAsync(selected[0]);
                    break;

                case ContextMenuHelper.ActionOpenFolder:
                    OpenContainingFolder(selected[0]);
                    break;

                case ContextMenuHelper.ActionCopyPath:
                    CopyToClipboard(selected.Select(f => f.FilePath));
                    break;

                case ContextMenuHelper.ActionCopyName:
                    CopyToClipboard(selected.Select(f => f.FileName));
                    break;

                case ContextMenuHelper.ActionDelete:
                    await DeleteSelectedAsync(selected);
                    break;
            }
        }

        // 打开所在文件夹并选中文件
        private void OpenContainingFolder(FileTagItem item)
        {
            try
            {
                var folder = Path.GetDirectoryName(item.FilePath);
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                {
                    _ = ShowMessageAsync($"文件夹不存在：\n{folder}");
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{item.FilePath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _ = ShowMessageAsync($"无法打开文件夹：{ex.Message}");
            }
        }

        // 把若干行文本放进剪贴板
        private void CopyToClipboard(IEnumerable<string> lines)
        {
            var text = string.Join(Environment.NewLine, lines);
            if (string.IsNullOrEmpty(text)) return;

            var dp = new DataPackage();
            dp.SetText(text);
            Clipboard.SetContent(dp);
        }

        // 删除记录（不删除磁盘文件）
        private async Task DeleteSelectedAsync(List<FileTagItem> selected)
        {
            if (selected.Count == 0) return;

            foreach (var item in selected)
                FileItems.Remove(item);

            await DataService.SaveAsync();
            // CollectionChanged 会自动触发 RefreshDisplay
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