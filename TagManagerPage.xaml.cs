using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

            var oldTag = selected[0].Name;
            var newName = await ShowInputAsync(
                $"将标签“{oldTag}”重命名为：", oldTag);

            if (string.IsNullOrWhiteSpace(newName)) return;
            newName = newName.Trim();
            if (newName == oldTag) return;

            // 新名字已存在：提示用户是否合并过去
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

            if (changed > 0)
            {
                await DataService.SaveAsync();
                RefreshTags();
            }
            else
            {
                await ShowMessageAsync("没有文件使用该标签。");
            }
        }

        // 删除：从所有文件中移除选中标签
        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            var selected = TagListView.SelectedItems.OfType<TagInfo>().ToList();

            if (selected.Count == 0)
            {
                await ShowMessageAsync("请先选中至少一个标签。");
                return;
            }

            var names = string.Join("、", selected.Select(t => t.Name));
            var ok = await ShowConfirmAsync(
                $"确定要从所有文件中删除以下标签吗？\n\n{names}\n\n此操作不可撤销。");
            if (!ok) return;

            var namesSet = selected.Select(t => t.Name).ToHashSet();

            foreach (var file in DataService.FileItems)
            {
                foreach (var name in namesSet)
                    file.Tags.Remove(name);
            }

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