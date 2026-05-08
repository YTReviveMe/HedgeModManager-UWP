using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.Storage;

namespace HedgeModManager.Uwp.Models
{
    public sealed class ModEntry : INotifyPropertyChanged
    {
        private bool _enabled;

        public event PropertyChangedEventHandler PropertyChanged;

        public StorageFolder Folder { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string UpdateServer { get; set; } = string.Empty;
        public string FolderName => Folder?.Name ?? string.Empty;
        public string ModIniPath => string.IsNullOrEmpty(Folder?.Path) ? $"{FolderName}\\mod.ini" : $"{Folder.Path}\\mod.ini";
        public bool HasUpdateServer => !string.IsNullOrWhiteSpace(UpdateServer);

        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value)
                    return;

                _enabled = value;
                OnPropertyChanged();
            }
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
