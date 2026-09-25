using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Tagmgr
{
    public sealed partial class TagManagerPage : Page
    {
        public TagManagerPage()
        {
            this.InitializeComponent();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            await DataService.EnsureLoadedAsync();

            DataService.DataChanged -= OnDataChanged;
            DataService.DataChanged += OnDataChanged;

            RefreshTags();
        }

        private void OnDataChanged()
        {
            DispatcherQueue.TryEnqueue(RefreshTags);
        }

        // 重新统计所有标签
        private void RefreshTags()
        {
            var query = SearchBox?.Text?.Trim() ?? "";

            var tagInfos = DataService.FileItems
                .SelectMany(f => f.Tags)
                .GroupBy(t => t)
                .Select(g => new TagInfo { Name = g.Key, Count = g.Count() })
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!string.IsNullOrEmpty(query))
            {
                tagInfos = tagInfos
                    .Where(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            TagListView.ItemsSource = tagInfos;
        }

        private void SearchBox_TextChanged(
            AutoSuggestBox sender,
            AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
                return;

            RefreshTags();
        }

        // ==================== 按钮 ====================

        private async void Rename_Click(object sender, RoutedEventArgs e)
        {
            var selected = TagListView.SelectedItems.OfType<TagInfo>().ToList();

            if (selected.Count == 0)
            {
                await ShowMessageAsync("请先选中一个标签。");
                return;
            }

            if (selected.Count > 1)
            {
                await ShowMessageAsync("重命名一次只能操作一个标签。若要处理多个标签，请使用“合并”。");
                return;
            }

            await RenameTagAsync(selected[0].Name);
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            var selected = TagListView.SelectedItems.OfType<TagInfo>().ToList();

            if (selected.Count == 0)
            {
                await ShowMessageAsync("请先选中至少一个标签。");
                return;
            }

            await DeleteTagsAsync(selected.Select(t => t.Name).ToList());
        }

        private async void Merge_Click(object sender, RoutedEventArgs e)
        {
            var selected = TagListView.SelectedItems.OfType<TagInfo>().ToList();

            if (selected.Count < 2)
            {
                await ShowMessageAsync("请至少选中两个标签进行合并。");
                return;
            }

            var names = string.Join("、", selected.Select(t => t.Name));
            var target = await ShowInputAsync(
                $"将以下标签合并为一个新标签：\n\n{names}\n\n请输入目标标签名：",
                selected[0].Name);

            if (string.IsNullOrWhiteSpace(target)) return;
            target = target.Trim();

            var oldNames = selected.Select(t => t.Name).ToList();
            await UndoService.ExecuteAsync(new MergeTagsCommand(oldNames, target));
        }

        private async void ChangeColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string tagName)
                return;

            await ShowColorPickerForTagAsync(tagName);
        }

        // ==================== 核心操作 ====================

        private async Task RenameTagAsync(string oldTag)
        {
            var newName = await ShowInputAsync(
                $"将标签“{oldTag}”重命名为：", oldTag);

            if (string.IsNullOrWhiteSpace(newName)) return;
            newName = newName.Trim();
            if (newName == oldTag) return;

            bool targetExists = DataService.FileItems
                .Any(f => f.Tags.Contains(newName));

            if (targetExists)
            {
                var ok = await ShowConfirmAsync(
                    $"标签“{newName}”已存在，重命名会把“{oldTag}”合并到该标签中。是否继续？");
                if (!ok) return;
            }

            await UndoService.ExecuteAsync(new RenameTagCommand(oldTag, newName));
        }

        private async Task DeleteTagsAsync(IReadOnlyCollection<string> names)
        {
            if (names.Count == 0) return;

            var namesText = string.Join("、", names);
            var ok = await ShowConfirmAsync(
                $"确定要从所有文件中删除以下标签吗？\n\n{namesText}\n\n此操作可撤销。");
            if (!ok) return;

            await UndoService.ExecuteAsync(new DeleteTagsCommand(names));
        }

        private async Task ShowColorPickerForTagAsync(string tagName)
        {
            var currentHex = DataService.GetTagColor(tagName) ?? "#E5E5E5";

            var picker = new ColorPicker
            {
                Color = TagColorHelper.ParseHex(currentHex),
                IsAlphaEnabled = false,
                IsColorChannelTextInputVisible = true,
                IsHexInputVisible = true,
                IsMoreButtonVisible = false,
                IsColorSliderVisible = true,
                IsColorSpectrumVisible = true
            };

            var dialog = new ContentDialog
            {
                Title = $"设置标签颜色：{tagName}",
                Content = picker,
                PrimaryButtonText = "确定",
                SecondaryButtonText = "清除颜色",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                await UndoService.ExecuteAsync(
                    new ChangeTagColorCommand(tagName, TagColorHelper.ToHex(picker.Color)));
            }
            else if (result == ContentDialogResult.Secondary)
            {
                await UndoService.ExecuteAsync(
                    new ChangeTagColorCommand(tagName, null));
            }
        }

        // ==================== 右键 ====================

        private void TagItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not TagInfo info) return;

            var selectedNames = TagListView.SelectedItems
                .OfType<TagInfo>()
                .Select(t => t.Name)
                .ToHashSet();

            if (!selectedNames.Contains(info.Name))
                selectedNames = new HashSet<string> { info.Name };

            var menu = new MenuFlyout();

            var colorItem = new MenuFlyoutItem { Text = "设置颜色..." };
            colorItem.Icon = new FontIcon { Glyph = "\uE790" };
            colorItem.Click += async (s, args) => await ShowColorPickerForTagAsync(info.Name);

            var renameItem = new MenuFlyoutItem { Text = "重命名..." };
            renameItem.Icon = new FontIcon { Glyph = "\uE8AC" };
            renameItem.Click += async (s, args) => await RenameTagAsync(info.Name);

            var deleteItem = new MenuFlyoutItem { Text = "删除" };
            deleteItem.Icon = new FontIcon { Glyph = "\uE74D" };
            deleteItem.Click += async (s, args) => await DeleteTagsAsync(selectedNames.ToList());

            menu.Items.Add(colorItem);
            menu.Items.Add(renameItem);
            menu.Items.Add(deleteItem);

            var others = DataService.FileItems
                .SelectMany(f => f.Tags)
                .Distinct()
                .Where(t => !selectedNames.Contains(t))
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (others.Count > 0)
            {
                menu.Items.Add(new MenuFlyoutSeparator());

                var mergeSub = new MenuFlyoutSubItem { Text = "合并到..." };
                mergeSub.Icon = new FontIcon { Glyph = "\uE8C4" };

                foreach (var target in others)
                {
                    var targetName = target;
                    var mi = new MenuFlyoutItem { Text = targetName };
                    mi.Click += async (s, args) =>
                    {
                        await UndoService.ExecuteAsync(
                            new MergeTagsCommand(selectedNames, targetName));
                    };
                    mergeSub.Items.Add(mi);
                }

                menu.Items.Add(mergeSub);
            }

            menu.ShowAt(fe, e.GetPosition(fe));
            e.Handled = true;
        }

        // ==================== 快捷键 ====================

        private void OnFocusSearch(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            SearchBox.Focus(FocusState.Programmatic);
            args.Handled = true;
        }

        private void OnSelectAll(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            TagListView.SelectAll();
            args.Handled = true;
        }

        private async void OnDeleteKey(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;

            var selected = TagListView.SelectedItems.OfType<TagInfo>().ToList();
            if (selected.Count == 0) return;

            await DeleteTagsAsync(selected.Select(t => t.Name).ToList());
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

        // ==================== 对话框 ====================

        private async Task<string?> ShowInputAsync(string prompt, string defaultText)
        {
            var input = new TextBox
            {
                Text = defaultText,
                SelectionStart = 0,
                SelectionLength = defaultText.Length
            };

            var dialog = new ContentDialog
            {
                Title = "输入",
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap },
                        input
                    }
                },
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? input.Text : null;
        }

        private async Task<bool> ShowConfirmAsync(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "确认",
                Content = message,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

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