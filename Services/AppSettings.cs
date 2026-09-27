using System;
using Microsoft.UI.Xaml;
using Windows.Storage;

namespace Tagmgr
{
    public static class AppSettings
    {
        private const string ThemeKey = "AppTheme";
        private const string LanguageKey = "AppLanguage";

        //用户选择的界面语言。空字符串表示跟随系统。
        public static string Language
        {
            get
            {
                var value = ApplicationData.Current.LocalSettings.Values[LanguageKey] as string;
                return value ?? "";
            }
            set
            {
                if (string.IsNullOrEmpty(value))
                    ApplicationData.Current.LocalSettings.Values.Remove(LanguageKey);
                else
                    ApplicationData.Current.LocalSettings.Values[LanguageKey] = value;
            }
        }

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