using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace Tagmgr
{
    public sealed partial class TagsPage : Page
    {
        // 当前排序方式
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

        public TagsPage()
        {
            this.InitializeComponent();

            // 设置默认排序
            SortComboBox.SelectedIndex = 0;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            await DataService.EnsureLoadedAsync();

            // 确保所有文件的元数据（图标 + 修改时间）已加载
            var tasks = DataService.FileItems
                .Select(item => item.LoadMetadataAsync())
                .ToList();

            await Task.WhenAll(tasks);

            // 重建标签列表
            var tags = DataService.FileItems
                .SelectMany(f => f.Tags)
                .Distinct()
                .OrderBy(t => t)
                .ToList();

            TagListView.ItemsSource = tags;

            // 清空搜索框（赋值会触发 TextChanged，但 Reason 是 ProgrammaticChange，会被忽略）
            if (SearchBox != null)
                SearchBox.Text = "";

            // 重置右侧状态
            RefreshFilteredFiles();
        }

        // 搜索框输入
        private void SearchBox_TextChanged(
            AutoSuggestBox sender,
            AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
                return;

            RefreshFilteredFiles();
        }

        // 排序方式切换
        private void SortComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            RefreshFilteredFiles();
        }

        // 标签选择变化
        private void TagListView_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            RefreshFilteredFiles();
        }

        // 根据已选标签 + 搜索文本 + 排序方式重建结果列表
        private void RefreshFilteredFiles()
        {
            var selectedTags = TagListView.SelectedItems
                .Cast<string>()
                .ToList();

            var query = SearchBox?.Text?.Trim() ?? "";

            // 没有任何筛选条件：显示空状态提示
            if (selectedTags.Count == 0 && string.IsNullOrEmpty(query))
            {
                FilteredFileListView.ItemsSource = null;
                FilteredFileListView.Visibility = Visibility.Collapsed;
                EmptyHint.Text = "请从左侧选择一个或多个标签，查看同时拥有这些标签的文件（双击可打开）";
                EmptyHint.Visibility = Visibility.Visible;
                return;
            }

            IEnumerable<FileTagItem> source = DataService.FileItems;

            // 按标签交集过滤
            if (selectedTags.Count > 0)
                source = source.Where(f => selectedTags.All(t => f.Tags.Contains(t)));

            // 按搜索文本过滤
            if (!string.IsNullOrEmpty(query))
            {
                source = source.Where(f =>
                    f.FileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    f.FilePath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    f.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)));
            }

            // 应用排序
            source = DataService.ApplySort(source, CurrentSortMode);

            var result = source.ToList();

            FilteredFileListView.ItemsSource = result;

            if (result.Count == 0)
            {
                FilteredFileListView.Visibility = Visibility.Collapsed;
                EmptyHint.Text = "没有匹配的文件。";
                EmptyHint.Visibility = Visibility.Visible;
            }
            else
            {
                FilteredFileListView.Visibility = Visibility.Visible;
                EmptyHint.Visibility = Visibility.Collapsed;
            }
        }

        // 双击文件项打开文件
        private async void FileItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not FileTagItem item) return;

            await OpenFileAsync(item);
        }

        // 打开文件，双击和右键共用
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

        // 右键点击文件项：这里的列表是只读视图，菜单只有只读操作
        private void FileItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not FileTagItem item) return;

            var menu = ContextMenuHelper.BuildReadOnlyFileMenu();

            foreach (var mfi in menu.Items.OfType<MenuFlyoutItem>())
            {
                if (mfi.Tag is string action)
                {
                    var captured = item;
                    mfi.Click += (s, args) => _ = HandleFileMenuActionAsync(action, captured);
                }
            }

            menu.ShowAt(fe, e.GetPosition(fe));
        }

        // 处理只读文件菜单的动作
        private async Task HandleFileMenuActionAsync(string action, FileTagItem item)
        {
            switch (action)
            {
                case ContextMenuHelper.ActionOpen:
                    await OpenFileAsync(item);
                    break;

                case ContextMenuHelper.ActionOpenFolder:
                    OpenContainingFolder(item);
                    break;

                case ContextMenuHelper.ActionCopyPath:
                    CopyToClipboard(item.FilePath);
                    break;

                case ContextMenuHelper.ActionCopyName:
                    CopyToClipboard(item.FileName);
                    break;
            }
        }

        // 打开所在文件夹并选中该文件
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

        private void CopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            var dp = new DataPackage();
            dp.SetText(text);
            Clipboard.SetContent(dp);
        }
        // 简单提示框
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