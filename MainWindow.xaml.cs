using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage.Pickers;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Tagmgr
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
            this.AppWindow.SetIcon("Assets/Tagmgr.ico");
            // 默认选中第一项并显示“所有文件”页
            NavView.SelectedItem = NavView.MenuItems[0];
            ContentFrame.Navigate(typeof(FilesPage));
        }
        
        private void NavView_SelectionChanged(
            NavigationView sender,
            NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is not NavigationViewItem item)
                return;

            switch (item.Tag)
            {
                case "files":
                    ContentFrame.Navigate(typeof(FilesPage));
                    break;
                case "tags":
                    ContentFrame.Navigate(typeof(TagsPage));
                    break;
                case "about":
                    ContentFrame.Navigate(typeof(AboutPage));
                    break;
            }
        }
    }
}