using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

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
    }
}