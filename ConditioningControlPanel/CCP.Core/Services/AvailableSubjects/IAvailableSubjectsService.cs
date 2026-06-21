using System.Collections.ObjectModel;
using System.ComponentModel;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Core.Services.AvailableSubjects
{
    /// <summary>
    /// Cross-platform directory client for the Available Subjects feature.
    /// </summary>
    public interface IAvailableSubjectsService : INotifyPropertyChanged, IDisposable
    {
        /// <summary>
        /// Current directory entries. Updated on the UI thread.
        /// </summary>
        ObservableCollection<AvailableSubject> Entries { get; }

        /// <summary>
        /// True while a refresh is in-flight.
        /// </summary>
        bool IsLoading { get; }

        /// <summary>
        /// True if the last successful refresh returned zero entries.
        /// </summary>
        bool IsEmpty { get; }

        /// <summary>
        /// True if the last refresh failed.
        /// </summary>
        bool HasError { get; }

        /// <summary>
        /// True when the periodic poll loop is active.
        /// </summary>
        bool IsPolling { get; }

        /// <summary>
        /// Start the periodic poll loop and perform an immediate refresh.
        /// </summary>
        void StartPolling();

        /// <summary>
        /// Stop the periodic poll loop and abort any in-flight refresh.
        /// </summary>
        void StopPolling();

        /// <summary>
        /// Fetch the current directory list now.
        /// </summary>
        Task RefreshAsync();

        /// <summary>
        /// Attempt to claim a subject. On success returns the one-click pairing URL
        /// that should be opened in the user's default browser. Never logs the URL.
        /// </summary>
        Task<string?> TryClaimAsync(string subjectUnifiedId);
    }
}
