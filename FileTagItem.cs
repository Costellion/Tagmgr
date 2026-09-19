using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Tagmgr
{
    public class FileTagItem : INotifyPropertyChanged
    {
        public string FilePath { get; set; } = "";

        [JsonIgnore]
        public string FileName => Path.GetFileName(FilePath);

        public ObservableCollection<string> Tags { get; set; } = new();

        private ImageSource? _icon;
        [JsonIgnore]
        public ImageSource? Icon
        {
            get => _icon;
            private set
            {
                _icon = value;
                OnPropertyChanged();
            }
        }

        // 是否已尝试加载过图标，避免重复加载
        [JsonIgnore]
        public bool IconLoaded { get; private set; }

        /// <summary>
        /// 异步加载文件图标。失败时静默忽略，不抛异常。
        /// </summary>
        public async Task LoadIconAsync()
        {
            if (IconLoaded) return;
            IconLoaded = true;

            try
            {
                var file = await StorageFile.GetFileFromPathAsync(FilePath);

                // SmallIcon 模式：对普通文件返回关联图标，对图片/视频返回缩略图
                using var thumb = await file.GetThumbnailAsync(
                    ThumbnailMode.SingleItem, 32);

                if (thumb != null)
                {
                    var bmp = new BitmapImage();
                    await bmp.SetSourceAsync(thumb);
                    Icon = bmp;
                }
            }
            catch
            {
                // 文件已被移动/删除，或没有访问权限时忽略
            }
        }


        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}