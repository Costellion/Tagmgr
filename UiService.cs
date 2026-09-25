using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using Windows.UI;

namespace Tagmgr
{
    /// <summary>
    /// 界面层通用助手：对话框、打开文件、剪贴板、标签颜色、右键菜单。
    /// 把原来分散在 TagColorHelper、ContextMenuHelper 和三个页面里的重复逻辑收在一起。
    /// </summary>
    public static class UiService
    {
        // ============================================================
        //  对话框
        // ============================================================

        /// <summary>显示一条提示信息。</summary>
        public static async Task ShowMessageAsync(XamlRoot root, string message)
        {
            if (root == null) return;

            var dialog = new ContentDialog
            {
                Title = "提示",
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = root
            };

            await dialog.ShowAsync();
        }

        /// <summary>显示确认对话框，用户点“确定”返回 true。</summary>
        public static async Task<bool> ShowConfirmAsync(XamlRoot root, string message)
        {
            if (root == null) return false;

            var dialog = new ContentDialog
            {
                Title = "确认",
                Content = message,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = root
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        /// <summary>显示输入对话框，用户取消返回 null。</summary>
        public static async Task<string?> ShowInputAsync(
            XamlRoot root, string prompt, string defaultText)
        {
            if (root == null) return null;

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
                XamlRoot = root
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
                return input.Text;

            return null;
        }

        // ============================================================
        //  文件操作
        // ============================================================

        /// <summary>用系统默认程序打开文件。</summary>
        public static async Task OpenFileAsync(XamlRoot root, FileTagItem item)
        {
            if (item == null) return;

            try
            {
                var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                var ok = await Launcher.LaunchFileAsync(file);

                if (!ok)
                    await ShowMessageAsync(root, "系统没有可用来打开该文件的程序。");
            }
            catch (Exception ex)
            {
                await ShowMessageAsync(
                    root, $"无法打开文件：{ex.Message}\n路径：{item.FilePath}");
            }
        }

        /// <summary>打开文件所在文件夹，并选中该文件。</summary>
        public static async Task OpenContainingFolderAsync(XamlRoot root, FileTagItem item)
        {
            if (item == null) return;

            try
            {
                var folder = Path.GetDirectoryName(item.FilePath);
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                {
                    await ShowMessageAsync(root, $"文件夹不存在：\n{folder}");
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
                await ShowMessageAsync(root, $"无法打开文件夹：{ex.Message}");
            }
        }

        // ============================================================
        //  剪贴板
        // ============================================================

        /// <summary>把一段文本放进剪贴板。</summary>
        public static void CopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            var dp = new DataPackage();
            dp.SetText(text);
            Clipboard.SetContent(dp);
        }

        /// <summary>把若干行文本按行放进剪贴板。</summary>
        public static void CopyToClipboard(IEnumerable<string> lines)
        {
            var text = string.Join(Environment.NewLine, lines);
            CopyToClipboard(text);
        }

        // ============================================================
        //  标签颜色
        // ============================================================

        // 未设置颜色时的默认灰
        private static readonly Color DefaultTagBackground =
            Color.FromArgb(0xFF, 0xE5, 0xE5, 0xE5);

        /// <summary>
        /// 根据标签名返回一个带透明度的背景刷，让文字仍然可读。
        /// 未设置颜色的标签返回默认灰。
        /// </summary>
        public static SolidColorBrush GetTagBackgroundBrush(string tagName)
        {
            if (string.IsNullOrEmpty(tagName))
                return new SolidColorBrush(DefaultTagBackground);

            var hex = DataService.GetTagColor(tagName);
            if (string.IsNullOrEmpty(hex))
                return new SolidColorBrush(DefaultTagBackground);

            try
            {
                var c = ParseColorHex(hex);
                // 用 45% 左右的不透明度
                return new SolidColorBrush(Color.FromArgb(0x73, c.R, c.G, c.B));
            }
            catch
            {
                return new SolidColorBrush(DefaultTagBackground);
            }
        }

        /// <summary>解析 #RRGGBB 或 #AARRGGBB。</summary>
        public static Color ParseColorHex(string hex)
        {
            hex = hex.TrimStart('#');

            if (hex.Length == 6)
            {
                return Color.FromArgb(
                    0xFF,
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16));
            }

            if (hex.Length == 8)
            {
                return Color.FromArgb(
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16),
                    Convert.ToByte(hex.Substring(6, 2), 16));
            }

            throw new FormatException("无效的颜色格式");
        }

        /// <summary>Color 转 #RRGGBB。</summary>
        public static string ToHex(Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        // ============================================================
        //  右键菜单
        // ============================================================

        // 动作标识
        public const string ActionOpen = "Open";
        public const string ActionOpenFolder = "OpenFolder";
        public const string ActionCopyPath = "CopyPath";
        public const string ActionCopyName = "CopyName";
        public const string ActionDelete = "Delete";

        /// <summary>构建文件项的可写右键菜单。</summary>
        public static MenuFlyout BuildFileMenu()
        {
            var menu = new MenuFlyout();

            menu.Items.Add(CreateMenuItem("打开文件", ActionOpen, "\uE8E5"));
            menu.Items.Add(CreateMenuItem("打开所在文件夹", ActionOpenFolder, "\uE838"));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(CreateMenuItem("复制完整路径", ActionCopyPath, "\uE8C8"));
            menu.Items.Add(CreateMenuItem("复制文件名", ActionCopyName, "\uE8C8"));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(CreateMenuItem("删除记录", ActionDelete, "\uE74D"));

            return menu;
        }

        /// <summary>构建只读视图的文件右键菜单（不含删除）。</summary>
        public static MenuFlyout BuildReadOnlyFileMenu()
        {
            var menu = new MenuFlyout();

            menu.Items.Add(CreateMenuItem("打开文件", ActionOpen, "\uE8E5"));
            menu.Items.Add(CreateMenuItem("打开所在文件夹", ActionOpenFolder, "\uE838"));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(CreateMenuItem("复制完整路径", ActionCopyPath, "\uE8C8"));
            menu.Items.Add(CreateMenuItem("复制文件名", ActionCopyName, "\uE8C8"));

            return menu;
        }

        private static MenuFlyoutItem CreateMenuItem(string text, string action, string glyph)
        {
            var item = new MenuFlyoutItem
            {
                Text = text,
                Tag = action
            };

            if (!string.IsNullOrEmpty(glyph))
                item.Icon = new FontIcon { Glyph = glyph };

            return item;
        }
    }

    /// <summary>
    /// XAML 转换器：把字符串（标签名）转成背景刷。
    /// 保留在全局资源里，由 App.xaml 注册。
    /// </summary>
    public class TagColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return UiService.GetTagBackgroundBrush(value as string);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}