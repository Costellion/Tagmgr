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
            SortComboBox.SelectedIndex = 0;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            await DataService.EnsureLoadedAsync();

            DataService.DataChanged -= OnDataChanged;
            DataService.DataChanged += OnDataChanged;

            var tasks = DataService.FileItems
                .Select(item => item.LoadMetadataAsync())
                .ToList();

            await Task.WhenAll(tasks);

            RebuildTagList();

            if (SearchBox != null)
                SearchBox.Text = "";

            RefreshFilteredFiles();
        }

        private void OnDataChanged()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                RebuildTagList();
                RefreshFilteredFiles();
            });
        }

        // 重建左侧标签列表，保留原有选中项
        private void RebuildTagList()
        {
            var selectedTags = TagListView.SelectedItems
                .Cast<string>()
                .ToHashSet();

            var tags = DataService.FileItems
                .SelectMany(f => f.Tags)
                .Distinct()
                .OrderBy(t => t)
                .ToList();

            TagListView.ItemsSource = tags;

            foreach (var t in tags)
            {
                if (selectedTags.Contains(t))
                    TagListView.SelectedItems.Add(t);
            }
        }

        // ==================== 事件 ====================

        private void SearchBox_TextChanged(
            AutoSuggestBox sender,
            AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
                return;

            RefreshFilteredFiles();
        }

        private void SortComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            RefreshFilteredFiles();
        }

        private void TagListView_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            RefreshFilteredFiles();
        }

        private void RefreshFilteredFiles()
        {
            var selectedTags = TagListView.SelectedItems
                .Cast<string>()
                .ToList();

            var query = SearchBox?.Text?.Trim() ?? "";

            if (selectedTags.Count == 0 && string.IsNullOrEmpty(query))
            {
                FilteredFileListView.ItemsSource = null;
                FilteredFileListView.Visibility = Visibility.Collapsed;
                EmptyHint.Text = "请从左侧选择一个或多个标签，查看同时拥有这些标签的文件（双击可打开）";
                EmptyHint.Visibility = Visibility.Visible;
                return;
            }

            IEnumerable<FileTagItem> source = DataService.FileItems;

            if (selectedTags.Count > 0)
                source = source.Where(f => selectedTags.All(t => f.Tags.Contains(t)));

            if (!string.IsNullOrEmpty(query))
            {
                source = source.Where(f =>
                    f.FileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    f.FilePath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    f.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)));
            }

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

        // ==================== 打开 / 右键 ====================

        private async void FileItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not FileTagItem item) return;

            await UiService.OpenFileAsync(RootGrid.XamlRoot, item);
        }


        private void FileItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not FileTagItem item) return;

            var menu = UiService.BuildReadOnlyFileMenu();

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

        private async Task HandleFileMenuActionAsync(string action, FileTagItem item)
        {
            switch (action)
            {
                case UiService.ActionOpen:
                    await UiService.OpenFileAsync(RootGrid.XamlRoot, item);
                    break;

                case UiService.ActionOpenFolder:
                    await UiService.OpenContainingFolderAsync(RootGrid.XamlRoot, item);
                    break;

                case UiService.ActionCopyPath:
                    UiService.CopyToClipboard(item.FilePath);
                    break;

                case UiService.ActionCopyName:
                    UiService.CopyToClipboard(item.FileName);
                    break;
            }
        }
        // ==================== 快捷键 ====================

        private void OnFocusSearch(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            SearchBox.Focus(FocusState.Programmatic);
            args.Handled = true;
        }

        private void OnSelectAll(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            FilteredFileListView.SelectAll();
            args.Handled = true;
        }

        private async void OnEnterKey(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            var item = FilteredFileListView.SelectedItems
                .OfType<FileTagItem>()
                .FirstOrDefault();

            if (item == null) return;

            args.Handled = true;
            await UiService.OpenFileAsync(RootGrid.XamlRoot, item);
        }

        private async void OnUndo(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            await UndoService.UndoAsync();
        }

        private async void OnRedo(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            await UndoService.RedoAsync();
        }
    }
}