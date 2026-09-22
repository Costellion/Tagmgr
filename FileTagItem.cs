using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
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

        // 文件图标，不写入 JSON
        private ImageSource? _icon;
        [JsonIgnore]
        public ImageSource? Icon
        {
            get => _icon;
            private set { _icon = value; OnPropertyChanged(); }
        }

        // 文件修改时间，不写入 JSON
        private DateTimeOffset? _modifiedTime;
        [JsonIgnore]
        public DateTimeOffset? ModifiedTime
        {
            get => _modifiedTime;
            private set { _modifiedTime = value; OnPropertyChanged(); }
        }

        // 是否已尝试加载过元数据，避免重复加载
        [JsonIgnore]
        public bool MetadataLoaded { get; private set; }

        /// <summary>
        /// 异步加载文件图标与修改时间。失败时静默忽略。
        /// </summary>
        public async Task LoadMetadataAsync()
        {
            if (MetadataLoaded) return;
            MetadataLoaded = true;

            try
            {
                var file = await StorageFile.GetFileFromPathAsync(FilePath);

                // 图标
                using (var thumb = await file.GetThumbnailAsync(
                    ThumbnailMode.SingleItem, 32))
                {
                    if (thumb != null)
                    {
                        var bmp = new BitmapImage();
                        await bmp.SetSourceAsync(thumb);
                        Icon = bmp;
                    }
                }

                // 修改时间
                var props = await file.GetBasicPropertiesAsync();
                ModifiedTime = props.DateModified;
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