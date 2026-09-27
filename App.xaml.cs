using Microsoft.UI.Xaml;

namespace Tagmgr
{
    public partial class App : Application
    {
        public Window? MainWindow { get; private set; }

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // 应用保存的语言设置。必须早于任何 UI 创建。
            ApplySavedLanguage();

            _ = DataService.EnsureLoadedAsync();

            MainWindow = new MainWindow();
            MainWindow.Activate();
        }

        // 根据保存的设置，设置 PrimaryLanguageOverride
        private static void ApplySavedLanguage()
        {
            try
            {
                var saved = AppSettings.Language;

                if (string.IsNullOrEmpty(saved))
                {
                    // 跟随系统：清空覆盖，让系统决定
                    Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "";
                }
                else
                {
                    Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = saved;
                }
            }
            catch
            {
                // 某些环境下设置可能失败，忽略，使用系统默认
            }
        }
    }
}