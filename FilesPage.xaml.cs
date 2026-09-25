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

        // ==================== 刷新 ====================

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

            var selected = FileListView.SelectedItems
                .OfType<FileTagItem>()
                .ToHashSet();

            DisplayItems.Clear();
            foreach (var item in result)
                DisplayItems.Add(item);

            foreach (var item in result)
            {
                if (selected.Contains(item))
                    FileListView.SelectedItems.Add(item);
            }
            UpdateStatusBar();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            await DataService.EnsureLoadedAsync();

            // 订阅共享集合与数据变化事件
            DataService.FileItems.CollectionChanged -= FileItems_CollectionChanged;
            DataService.FileItems.CollectionChanged += FileItems_CollectionChanged;

            DataService.DataChanged -= OnDataChanged;
            DataService.DataChanged += OnDataChanged;

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

        private void OnDataChanged()
        {
            DispatcherQueue.TryEnqueue(RefreshDisplay);
        }

        // ==================== 选择文件 ====================

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

                newItems.Add(new FileTagItem { FilePath = file.Path });
            }

            if (newItems.Count == 0) return;

            await Task.WhenAll(newItems.Select(i => i.LoadMetadataAsync()));

            await UndoService.ExecuteAsync(new AddFilesCommand(newItems));
        }

        // ==================== 搜索 / 排序 ====================

        private void SortComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            RefreshDisplay();
        }

        private void SearchBox_TextChanged(
            AutoSuggestBox sender,
            AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
                return;

            RefreshDisplay();
        }

        // ==================== 标签操作 ====================

        private async void AddTag_Click(object sender, RoutedEventArgs e)
        {
            await AddTagAsync(TagInput.Text);
        }

        private async Task AddTagAsync(string? rawTag)
        {
            var tag = (rawTag ?? "").Trim();
            if (string.IsNullOrWhiteSpace(tag)) return;

            var selected = FileListView.SelectedItems
                .OfType<FileTagItem>()
                .ToList();

            if (selected.Count == 0)
            {
                await UiService.ShowMessageAsync(RootGrid.XamlRoot,"请先在左侧选中至少一个文件。");
                return;
            }

            // 检查是否真的有变化
            if (!selected.Any(f => !f.Tags.Contains(tag)))
            {
                TagInput.Text = "";
                return;
            }

            await UndoService.ExecuteAsync(new AddTagCommand(selected, tag));
            TagInput.Text = "";
        }

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

        private void TagInput_SuggestionChosen(
            AutoSuggestBox sender,
            AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is string tag)
                sender.Text = tag;
        }

        private async void TagInput_QuerySubmitted(
            AutoSuggestBox sender,
            AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            var tag = string.IsNullOrWhiteSpace(args.QueryText)
                ? sender.Text
                : args.QueryText;

            await AddTagAsync(tag);
        }

        private async void RemoveTag_Click(object sender, RoutedEventArgs e)
        {
            await RemoveTagAsync(TagInput.Text);
        }

        private async Task RemoveTagAsync(string? rawTag)
        {
            var tag = (rawTag ?? "").Trim();
            if (string.IsNullOrWhiteSpace(tag)) return;

            var selected = FileListView.SelectedItems
                .OfType<FileTagItem>()
                .ToList();

            if (selected.Count == 0)
            {
                await UiService.ShowMessageAsync(RootGrid.XamlRoot,"请先在左侧选中至少一个文件。");
                return;
            }

            if (!selected.Any(f => f.Tags.Contains(tag)))
            {
                TagInput.Text = "";
                return;
            }

            await UndoService.ExecuteAsync(new RemoveTagCommand(selected, tag));
            TagInput.Text = "";
        }

        private async void RemoveTagChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;

            var tag = btn.Tag as string;
            if (string.IsNullOrEmpty(tag)) return;

            var element = btn as FrameworkElement;
            while (element != null && element is not ListViewItem)
                element = VisualTreeHelper.GetParent(element) as FrameworkElement;

            if (element is not ListViewItem lvi) return;
            if (lvi.Content is not FileTagItem item) return;

            if (!item.Tags.Contains(tag)) return;

            await UndoService.ExecuteAsync(
                new RemoveTagCommand(new[] { item }, tag));
        }

        // ==================== 删除文件记录 ====================

        private async void DeleteFile_Click(object sender, RoutedEventArgs e)
        {
            var selected = FileListView.SelectedItems
                .OfType<FileTagItem>()
                .ToList();

            await DeleteSelectedAsync(selected);
        }

        private async Task DeleteSelectedAsync(List<FileTagItem> selected)
        {
            if (selected.Count == 0) return;
            await UndoService.ExecuteAsync(new DeleteFilesCommand(selected));
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

            if (!FileListView.SelectedItems.Contains(item))
            {
                FileListView.SelectedItems.Clear();
                FileListView.SelectedItems.Add(item);
            }

            var menu = UiService.BuildFileMenu();

            foreach (var mfi in menu.Items.OfType<MenuFlyoutItem>())
            {
                if (mfi.Tag is string action)
                    mfi.Click += (s, args) => _ = HandleFileMenuActionAsync(action);
            }

            menu.ShowAt(fe, e.GetPosition(fe));
        }

        private async Task HandleFileMenuActionAsync(string action)
        {
            var selected = FileListView.SelectedItems
                .OfType<FileTagItem>()
                .ToList();

            if (selected.Count == 0) return;

            switch (action)
            {
                case UiService.ActionOpen:
                    await UiService.OpenFileAsync(RootGrid.XamlRoot, selected[0]);
                    break;

                case UiService.ActionOpenFolder:
                    await UiService.OpenContainingFolderAsync(RootGrid.XamlRoot, selected[0]);
                    break;

                case UiService.ActionCopyPath:
                    UiService.CopyToClipboard(selected.Select(f => f.FilePath));
                    break;

                case UiService.ActionCopyName:
                    UiService.CopyToClipboard(selected.Select(f => f.FileName));
                    break;

                case UiService.ActionDelete:
                    await DeleteSelectedAsync(selected);
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
            FileListView.SelectAll();
            args.Handled = true;
        }

        private async void OnDeleteKey(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;

            var selected = FileListView.SelectedItems
                .OfType<FileTagItem>()
                .ToList();

            await DeleteSelectedAsync(selected);
        }

        private async void OnEnterKey(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            var item = FileListView.SelectedItems
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
        // ==================== 状态栏 ====================

        private void FileListView_SelectionChanged(
            object sender, SelectionChangedEventArgs e)
        {
            UpdateStatusBar();
        }

        private void UpdateStatusBar()
        {
            var total = DataService.FileItems.Count;
            var shown = DisplayItems.Count;
            var selected = FileListView.SelectedItems.Count;

            StatusBarText.Text =
                $"共 {total} 个文件，当前显示 {shown} 个，选中 {selected} 个";
        }
        // ==================== 拖拽添加 ====================

        private void RootGrid_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                if (e.DragUIOverride != null)
                    e.DragUIOverride.Caption = "添加文件";
            }
        }

        private async void RootGrid_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
                return;

            var storageItems = await e.DataView.GetStorageItemsAsync();
            var newItems = new List<FileTagItem>();

            foreach (var item in storageItems)
            {
                var file = item as StorageFile;
                if (file == null) continue;

                // 已存在的不重复添加
                if (DataService.FileItems.Any(x => x.FilePath == file.Path))
                    continue;

                newItems.Add(new FileTagItem { FilePath = file.Path });
            }

            if (newItems.Count == 0) return;

            await Task.WhenAll(newItems.Select(i => i.LoadMetadataAsync()));

            await UndoService.ExecuteAsync(new AddFilesCommand(newItems));
        }

    }
}