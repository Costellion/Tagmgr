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
            RefreshTags();
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

        // 重命名：把所有文件中的旧标签替换为新标签
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

        // 删除按钮
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

        // 真正执行重命名
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

            int changed = 0;
            foreach (var file in DataService.FileItems)
            {
                if (file.Tags.Contains(oldTag))
                {
                    file.Tags.Remove(oldTag);
                    if (!file.Tags.Contains(newName))
                        file.Tags.Add(newName);
                    changed++;
                }
            }

            // 颜色迁移
            var oldColor = DataService.GetTagColor(oldTag);
            if (!string.IsNullOrEmpty(oldColor) &&
                string.IsNullOrEmpty(DataService.GetTagColor(newName)))
            {
                DataService.SetTagColor(newName, oldColor);
            }
            DataService.SetTagColor(oldTag, null);

            if (changed > 0 || !string.IsNullOrEmpty(oldColor))
            {
                await DataService.SaveAsync();
                RefreshTags();
            }
            else
            {
                await ShowMessageAsync("没有文件使用该标签。");
            }
        }

        // 真正执行删除
        private async Task DeleteTagsAsync(IReadOnlyCollection<string> names)
        {
            if (names.Count == 0) return;

            var namesText = string.Join("、", names);
            var ok = await ShowConfirmAsync(
                $"确定要从所有文件中删除以下标签吗？\n\n{namesText}\n\n此操作不可撤销。");
            if (!ok) return;

            var namesSet = names.ToHashSet();

            foreach (var file in DataService.FileItems)
            {
                foreach (var name in namesSet)
                    file.Tags.Remove(name);
            }

            // 同时清除颜色
            foreach (var name in namesSet)
                DataService.SetTagColor(name, null);

            await DataService.SaveAsync();
            RefreshTags();
        }

        // 合并：把选中的多个标签统一替换为一个目标标签
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

            foreach (var file in DataService.FileItems)
            {
                bool changed = false;
                foreach (var oldName in oldNames)
                {
                    if (file.Tags.Remove(oldName))
                        changed = true;
                }

                if (changed && !file.Tags.Contains(target))
                    file.Tags.Add(target);
            }

            await DataService.SaveAsync();
            RefreshTags();
        }
        // 颜色块按钮
        private async void ChangeColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string tagName)
                return;

            await ShowColorPickerForTagAsync(tagName);
        }

        // 抽取出来的颜色选择逻辑，按钮和右键菜单共用
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
                DataService.SetTagColor(tagName, TagColorHelper.ToHex(picker.Color));
                await DataService.SaveTagColorsAsync();
                RefreshTags();
            }
            else if (result == ContentDialogResult.Secondary)
            {
                DataService.SetTagColor(tagName, null);
                await DataService.SaveTagColorsAsync();
                RefreshTags();
            }
        }

        // 右键点击标签项
        private void TagItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not TagInfo info) return;

            // 本次菜单的操作目标：优先用已有的多选集合；
            // 若被右键的项不在其中，则只用它自己
            var selectedNames = TagListView.SelectedItems
                .OfType<TagInfo>()
                .Select(t => t.Name)
                .ToHashSet();

            if (!selectedNames.Contains(info.Name))
                selectedNames = new HashSet<string> { info.Name };

            var menu = new MenuFlyout();

            // 设置颜色（只对右键的那一个）
            var colorItem = new MenuFlyoutItem { Text = "设置颜色..." };
            colorItem.Icon = new FontIcon { Glyph = "\uE790" };
            colorItem.Click += async (s, args) => await ShowColorPickerForTagAsync(info.Name);

            // 重命名（只对右键的那一个）
            var renameItem = new MenuFlyoutItem { Text = "重命名..." };
            renameItem.Icon = new FontIcon { Glyph = "\uE8AC" };
            renameItem.Click += async (s, args) => await RenameTagAsync(info.Name);

            // 删除（作用于整个目标集合）
            var deleteItem = new MenuFlyoutItem { Text = "删除" };
            deleteItem.Icon = new FontIcon { Glyph = "\uE74D" };
            deleteItem.Click += async (s, args) => await DeleteTagsAsync(selectedNames.ToList());

            menu.Items.Add(colorItem);
            menu.Items.Add(renameItem);
            menu.Items.Add(deleteItem);

            // 合并到... 子菜单
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
                        await MergeTagsIntoAsync(selectedNames, targetName);
                    mergeSub.Items.Add(mi);
                }

                menu.Items.Add(mergeSub);
            }

            menu.ShowAt(fe, e.GetPosition(fe));
            e.Handled = true;   // 阻止 ListView 的默认右键行为覆盖菜单
        }

        // 把当前选中的所有标签合并到指定目标
        private async Task MergeTagsIntoAsync(
    IReadOnlyCollection<string> sourceNames,
    string targetTag)
        {
            if (sourceNames.Count == 0) return;
            if (sourceNames.Contains(targetTag)) return;

            // 颜色迁移：目标没颜色时，用源标签里第一个有颜色的
            var targetColor = DataService.GetTagColor(targetTag);
            string? sourceColor = null;

            if (string.IsNullOrEmpty(targetColor))
            {
                sourceColor = sourceNames
                    .Select(n => DataService.GetTagColor(n))
                    .FirstOrDefault(c => !string.IsNullOrEmpty(c));
            }

            // 合并标签
            foreach (var file in DataService.FileItems)
            {
                bool changed = false;
                foreach (var oldName in sourceNames)
                {
                    if (file.Tags.Remove(oldName))
                        changed = true;
                }

                if (changed && !file.Tags.Contains(targetTag))
                    file.Tags.Add(targetTag);
            }

            // 颜色迁移
            if (string.IsNullOrEmpty(targetColor) && !string.IsNullOrEmpty(sourceColor))
                DataService.SetTagColor(targetTag, sourceColor);

            foreach (var oldName in sourceNames)
                DataService.SetTagColor(oldName, null);

            await DataService.SaveTagColorsAsync();
            RefreshTags();
        }

        // ---------- 对话框 ----------

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