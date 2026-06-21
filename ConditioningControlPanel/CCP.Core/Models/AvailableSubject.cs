using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// One entry in the Available Subjects directory.
    /// Mirrors the cclabs-web DirectoryEntry shape with cross-platform bindable properties.
    /// </summary>
    public sealed class AvailableSubject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private string _unifiedId = "";
        public string UnifiedId
        {
            get => _unifiedId;
            set { _unifiedId = value ?? ""; OnPropertyChanged(); }
        }

        private string _displayName = "Anonymous";
        public string DisplayName
        {
            get => _displayName;
            set { _displayName = value ?? "Anonymous"; OnPropertyChanged(); }
        }

        private int _level = 1;
        public int Level
        {
            get => _level;
            set { _level = value; OnPropertyChanged(); OnPropertyChanged(nameof(LevelText)); }
        }

        public string LevelText => $"Lv {Level}";

        private ObservableCollection<string> _tags = new();
        public ObservableCollection<string> Tags
        {
            get => _tags;
            set
            {
                _tags = value ?? new ObservableCollection<string>();
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasTags));
            }
        }

        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(HasStatusText)); }
        }

        private string _tier = "light";
        public string Tier
        {
            get => _tier;
            set { _tier = value ?? "light"; OnPropertyChanged(); OnPropertyChanged(nameof(TierLabel)); }
        }

        private bool _claimed;
        public bool Claimed
        {
            get => _claimed;
            set
            {
                _claimed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsConnectEnabled));
                OnPropertyChanged(nameof(ConnectButtonText));
                OnPropertyChanged(nameof(CardOpacity));
            }
        }

        public string TierLabel => Tier?.ToLowerInvariant() switch
        {
            "light" => "LIGHT",
            "standard" => "STANDARD",
            "full" => "FULL",
            _ => Tier?.ToUpperInvariant() ?? ""
        };

        public bool HasTags => Tags != null && Tags.Count > 0;
        public bool HasStatusText => !string.IsNullOrEmpty(StatusText);
        public bool IsConnectEnabled => !Claimed;
        public string ConnectButtonText => Claimed ? "Taken" : "Connect";
        public double CardOpacity => Claimed ? 0.6 : 1.0;
    }
}
