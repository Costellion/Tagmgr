using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;

namespace Tagmgr
{
    public sealed partial class TagsPage : Page
    {
        public TagsPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            await DataService.EnsureLoadedAsync();

            // 确保所有文件图标已加载
            foreach (var item in DataService.FileItems)
                _ = item.LoadMetadataAsync();

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

        // 标签选择变化
        private void TagListView_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            RefreshFilteredFiles();
        }

        // 根据已选标签 + 搜索文本重建结果列表
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

            var result = source.ToList();

            FilteredFileListView.ItemsSource = result;

            // 有结果才显示列表；无结果显示空状态
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