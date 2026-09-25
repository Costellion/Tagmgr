using System;
using Microsoft.UI.Xaml;
using Windows.Storage;

namespace Tagmgr
{
    public static class AppSettings
    {
        private const string ThemeKey = "AppTheme";

        // 主题变化时通知 MainWindow 刷新
        public static event Action? ThemeChanged;

        public static ElementTheme Theme
        {
            get
            {
                var value = ApplicationData.Current.LocalSettings.Values[ThemeKey] as string;
                return value switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };
            }
            set
            {
                var str = value switch
                {
                    ElementTheme.Light => "Light",
                    ElementTheme.Dark => "Dark",
                    _ => "Default"
                };

                ApplicationData.Current.LocalSettings.Values[ThemeKey] = str;
                ThemeChanged?.Invoke();
            }
        }
    }
}