using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.AvailableSubjects;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs
{
    /// <summary>
    /// View-model for the Available Subjects directory tab.
    /// </summary>
    public sealed partial class AvailableSubjectsTabViewModel : TabItemViewModel
    {
        private readonly IAvailableSubjectsService _service;
        private readonly ISettingsService _settingsService;
        private readonly IAppLogger? _logger;

        [ObservableProperty]
        private ObservableCollection<AvailableSubject> _entries = new();

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private bool _isEmpty = true;

        [ObservableProperty]
        private bool _hasError;

        [ObservableProperty]
        private bool _showPremiumUpsell = true;

        public AvailableSubjectsTabViewModel()
            : base("availablesubjects", "Available Subjects", "🛰️")
        {
            _service = null!;
            _settingsService = null!;
            _entries = new ObservableCollection<AvailableSubject>();
        }

        public AvailableSubjectsTabViewModel(
            IAvailableSubjectsService service,
            ISettingsService settingsService,
            IAppLogger? logger = null)
            : base("availablesubjects", "Available Subjects", "🛰️")
        {
            _service = service;
            _settingsService = settingsService;
            _logger = logger;
            _entries = service.Entries;

            service.PropertyChanged += OnServicePropertyChanged;
            UpdateStateFromService();
            UpdatePremiumUpsell();
        }

        private void OnServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(IAvailableSubjectsService.IsLoading):
                    IsLoading = _service.IsLoading;
                    break;
                case nameof(IAvailableSubjectsService.IsEmpty):
                    IsEmpty = _service.IsEmpty;
                    OnPropertyChanged(nameof(ShowEmptyPanel));
                    break;
                case nameof(IAvailableSubjectsService.HasError):
                    HasError = _service.HasError;
                    OnPropertyChanged(nameof(ShowErrorPanel));
                    OnPropertyChanged(nameof(ShowEmptyPanel));
                    break;
            }
        }

        private void UpdateStateFromService()
        {
            if (_service == null) return;
            IsLoading = _service.IsLoading;
            IsEmpty = _service.IsEmpty;
            HasError = _service.HasError;
            Entries = _service.Entries;
            OnPropertyChanged(nameof(ShowErrorPanel));
            OnPropertyChanged(nameof(ShowEmptyPanel));
        }

        private void UpdatePremiumUpsell()
        {
            var settings = _settingsService?.Current;
            if (settings == null) return;
            ShowPremiumUpsell = !(settings.HasLinkedPatreon || settings.HasLinkedDiscord);
        }

        public override void OnSelected()
        {
            base.OnSelected();
            _service?.StartPolling();
            UpdatePremiumUpsell();
        }

        public override void OnDeselected()
        {
            base.OnDeselected();
            _service?.StopPolling();
        }

        [RelayCommand]
        private async Task ConnectAsync(AvailableSubject? subject)
        {
            if (subject == null || string.IsNullOrEmpty(subject.UnifiedId)) return;

            _logger?.Information("[AvailableSubjects] claiming subject {DisplayName}", subject.DisplayName);
            var url = await _service.TryClaimAsync(subject.UnifiedId);
            if (string.IsNullOrEmpty(url)) return;

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[AvailableSubjects] failed to open pairing URL");
            }
        }

        [RelayCommand]
        private async Task RefreshAsync()
        {
            await (_service?.RefreshAsync() ?? Task.CompletedTask);
        }

        [RelayCommand]
        private void BecomeSubject()
        {
            // Navigation to the Remote Control tab is handled by the view,
            // which raises RequestSelectTab when this command executes.
            RequestSelectTab?.Invoke("remotecontrol");
        }

        /// <summary>
        /// Raised when the user clicks "Become a subject!" so the view can
        /// switch to the Remote Control tab.
        /// </summary>
        public event Action<string>? RequestSelectTab;

        public string HeaderTitle => Loc.Get("tab_available_subjects");
        public string HeaderDescription => Loc.Get("desc_available_subjects");
        public string EmptyMessage => Loc.Get("empty_available_subjects");
        public string ErrorMessage => Loc.Get("error_available_subjects");
        public string BecomeSubjectText => Loc.Get("btn_become_a_subject");
        public string BecomeSubjectTooltip => Loc.Get("tooltip_become_a_subject");
        public string BecomeSubjectLockedText => Loc.Get("desc_become_a_subject_locked");

        public bool ShowErrorPanel => HasError;
        public bool ShowEmptyPanel => IsEmpty && !HasError;
    }
}
