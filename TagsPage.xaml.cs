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

            // 确保所有文件的图标都已加载（幂等）
            foreach (var item in DataService.FileItems)
                _ = item.LoadIconAsync();

            RefreshTagList();
        }

        private void RefreshTagList()
        {
            var tags = DataService.FileItems
                .SelectMany(f => f.Tags)
                .Distinct()
                .OrderBy(t => t)
                .ToList();

            TagListView.ItemsSource = tags;

            FilteredFileListView.ItemsSource = null;
            FilteredFileListView.Visibility = Visibility.Collapsed;
            EmptyHint.Visibility = Visibility.Visible;
        }

        // 多标签筛选：文件必须同时包含所有选中标签
        private void TagListView_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            var selectedTags = TagListView.SelectedItems
                .Cast<string>()
                .ToList();

            if (selectedTags.Count == 0)
            {
                FilteredFileListView.Visibility = Visibility.Collapsed;
                EmptyHint.Visibility = Visibility.Visible;
                return;
            }

            var filtered = DataService.FileItems
                .Where(f => selectedTags.All(t => f.Tags.Contains(t)))
                .ToList();

            FilteredFileListView.ItemsSource = filtered;
            FilteredFileListView.Visibility = Visibility.Visible;
            EmptyHint.Visibility = Visibility.Collapsed;
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