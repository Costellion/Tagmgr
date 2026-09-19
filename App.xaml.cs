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
            _ = DataService.EnsureLoadedAsync();

            MainWindow = new MainWindow();
            MainWindow.Activate();
        }
    }
}