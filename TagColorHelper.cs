using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Tagmgr
{
    public static class TagColorHelper
    {
        // 未设置颜色时的默认灰
        private static readonly Color DefaultBackground =
            Color.FromArgb(0xFF, 0xE5, 0xE5, 0xE5);

        /// <summary>
        /// 根据标签名返回一个带透明度的背景刷，让文字仍然可读。
        /// 未设置颜色的标签返回默认灰。
        /// </summary>
        public static SolidColorBrush GetBackgroundBrush(string? tagName)
        {
            if (string.IsNullOrEmpty(tagName))
                return new SolidColorBrush(DefaultBackground);

            var hex = DataService.GetTagColor(tagName);
            if (string.IsNullOrEmpty(hex))
                return new SolidColorBrush(DefaultBackground);

            try
            {
                var c = ParseHex(hex);
                // 用 45% 左右的不透明度
                return new SolidColorBrush(Color.FromArgb(0x73, c.R, c.G, c.B));
            }
            catch
            {
                return new SolidColorBrush(DefaultBackground);
            }
        }

        /// <summary>
        /// 解析 #RRGGBB 或 #AARRGGBB。
        /// </summary>
        public static Color ParseHex(string hex)
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

        /// <summary>
        /// Color 转 #RRGGBB。
        /// </summary>
        public static string ToHex(Color color)
            => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    /// <summary>
    /// XAML 转换器：把字符串（标签名）转成背景刷。
    /// </summary>
    public class TagColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => TagColorHelper.GetBackgroundBrush(value as string);

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}