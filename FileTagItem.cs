using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Tagmgr
{
    public class FileTagItem : INotifyPropertyChanged
    {
        public string FilePath { get; set; } = "";

        [JsonIgnore]
        public string FileName => Path.GetFileName(FilePath);

        public ObservableCollection<string> Tags { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}