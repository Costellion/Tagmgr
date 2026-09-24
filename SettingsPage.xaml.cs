using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Tagmgr
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            this.InitializeComponent();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            await DataService.EnsureLoadedAsync();

            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tagmgr");

            DataPathText.Text = Path.Combine(folder, "tags.json");

            FileCountText.Text = $"文件记录：{DataService.FileItems.Count} 条";

            var tagCount = DataService.FileItems
                .SelectMany(f => f.Tags)
                .Distinct()
                .Count();

            TagCountText.Text = $"标签数量：{tagCount} 个";
        }

        private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tagmgr");

            try
            {
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = folder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _ = ShowMessageAsync($"无法打开文件夹：{ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task ShowMessageAsync(string message)
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