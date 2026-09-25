using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
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
        // 文件是否失联（被移动、重命名或删除）
        private bool _isMissing;
        [JsonIgnore]
        public bool IsMissing
        {
            get => _isMissing;
            private set { _isMissing = value; OnPropertyChanged(); }
        }
        // 图标缓存：文件路径 → 图标。跨 FileTagItem 实例共享，避免重复访问文件系统。
        private static readonly Dictionary<string, ImageSource?> _iconCache = new();

        /// <summary>
        /// 异步加载文件图标与修改时间。失败时静默忽略。
        /// </summary>
        public async Task LoadMetadataAsync()
        {
            if (MetadataLoaded) return;
            MetadataLoaded = true;
            // 缓存命中：直接使用，不再访问文件系统
            if (_iconCache.TryGetValue(FilePath, out var cachedIcon))
            {
                Icon = cachedIcon;
            }
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(FilePath);

                // 只有在缓存没命中时才真正取图标
                if (cachedIcon == null)
                {
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

                    _iconCache[FilePath] = Icon;
                }

                // 修改时间
                var props = await file.GetBasicPropertiesAsync();
                ModifiedTime = props.DateModified;
                IsMissing = false;
            }
            catch
            {
                // 文件已被移动/删除，或没有访问权限时忽略
                IsMissing = true;
            }
        }
        public async Task<bool> CheckExistsAsync()
        {
            try
            {
                await StorageFile.GetFileFromPathAsync(FilePath);
                IsMissing = false;
                return true;
            }
            catch
            {
                IsMissing = true;
                return false;
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}