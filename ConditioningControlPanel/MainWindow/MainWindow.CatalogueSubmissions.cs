using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Catalogue share feedback for Presets & Sessions — the generalized sibling of
    // MainWindow.DeeperSubmissions.cs. After a fire-and-forget share, remember the
    // submission and surface when it gets approved/published: a status badge on the
    // card + a one-time accepted notification on the next status poll. Sessions are
    // file-backed (keyed by canonical path); presets live in UserPresets (keyed by Id).
    public partial class MainWindow
    {
        // Route segments / dict selectors.
        public const string CatalogueKindPresets = "presets";
        public const string CatalogueKindSessions = "sessions";

        private static readonly TimeSpan CatalogueCheckThrottle = TimeSpan.FromSeconds(90);
        private DateTime _lastCataloguePresetCheckUtc = DateTime.MinValue;
        private DateTime _lastCatalogueSessionCheckUtc = DateTime.MinValue;
        private bool _cataloguePresetCheckInFlight;
        private bool _catalogueSessionCheckInFlight;

        private static bool IsCatalogueAcceptedStatus(string? status) =>
            string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "published", StringComparison.OrdinalIgnoreCase);

        private static string CanonicalCataloguePathKey(string filePath)
        {
            try { return System.IO.Path.GetFullPath(filePath); }
            catch { return filePath; }
        }

        private static Dictionary<string, DeeperSubmissionRecord>? GetCatalogueDict(string kind)
        {
            var s = App.Settings?.Current;
            if (s == null) return null;
            return kind switch
            {
                CatalogueKindPresets => s.CataloguePresetSubmissions,
                CatalogueKindSessions => s.CatalogueSessionSubmissions,
                _ => null,
            };
        }

        // Look up the current moderation status for a given key, or null if not yet
        // submitted. Used by card builders to render the status badge.
        private DeeperSubmissionRecord? GetCatalogueRecord(string kind, string key)
        {
            var dict = GetCatalogueDict(kind);
            if (dict == null || string.IsNullOrEmpty(key)) return null;
            dict.TryGetValue(key, out var rec);
            return rec;
        }

        // Called after a share attempt resolves. Only Success/Duplicate carry a
        // server id worth remembering; other outcomes are no-ops.
        private void RecordCatalogueSubmission(string kind, string key, SubmissionResult result)
        {
            try
            {
                string id;
                string status;
                switch (result)
                {
                    case SubmissionResult.Success s:
                        id = s.Id;
                        status = string.IsNullOrEmpty(s.Status) ? "pending" : s.Status;
                        break;
                    case SubmissionResult.Duplicate d:
                        id = d.ExistingId;
                        status = string.IsNullOrEmpty(d.ExistingStatus) ? "pending" : d.ExistingStatus;
                        break;
                    default:
                        return;
                }

                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(key)) return;
                var dict = GetCatalogueDict(kind);
                if (dict == null) return;

                dict.TryGetValue(key, out var existing);
                var rec = existing ?? new DeeperSubmissionRecord { SubmittedUtc = DateTime.UtcNow };
                rec.CatalogueId = id;
                rec.Status = status;
                rec.LastCheckedUtc = DateTime.UtcNow;
                // A re-share that reports the asset is already accepted shouldn't fire a
                // retroactive "published" toast — the duplicate toast already told the user.
                if (IsCatalogueAcceptedStatus(status)) rec.AcceptedNotified = true;

                dict[key] = rec;
                App.Settings?.Save();

                RefreshCatalogueShareBadges(kind);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Catalogue] RecordCatalogueSubmission failed: {Error}", ex.Message);
            }
        }

        // Polls the catalogue for status changes on tracked submissions of one kind
        // and, on a pending → accepted transition, fires a one-time notification.
        // Safe to call on startup and on tab open; cheap no-op when nothing is pending.
        public async Task CheckCatalogueSubmissionStatusesAsync(string kind, bool force = false)
        {
            try
            {
                var settings = App.Settings?.Current;
                if (settings == null || App.Catalogue == null) return;
                if (string.IsNullOrEmpty(settings.AuthToken)) return;

                var dict = GetCatalogueDict(kind);
                if (dict == null || dict.Count == 0) return;

                bool inFlight = kind == CatalogueKindPresets ? _cataloguePresetCheckInFlight : _catalogueSessionCheckInFlight;
                if (inFlight) return;

                bool anyOpen = dict.Values.Any(r => !IsCatalogueAcceptedStatus(r.Status) || !r.AcceptedNotified);
                if (!anyOpen) return;

                bool isPresets = kind == CatalogueKindPresets;
                var lastCheck = isPresets ? _lastCataloguePresetCheckUtc : _lastCatalogueSessionCheckUtc;
                if (!force && DateTime.UtcNow - lastCheck < CatalogueCheckThrottle) return;

                if (isPresets) { _cataloguePresetCheckInFlight = true; _lastCataloguePresetCheckUtc = DateTime.UtcNow; }
                else { _catalogueSessionCheckInFlight = true; _lastCatalogueSessionCheckUtc = DateTime.UtcNow; }

                var statuses = await App.Catalogue.FetchMyCatalogueAssetsAsync(kind, default).ConfigureAwait(true);
                if (statuses == null) return;

                bool changed = false;
                foreach (var kvp in dict)
                {
                    var rec = kvp.Value;
                    if (rec == null || string.IsNullOrEmpty(rec.CatalogueId)) continue;
                    if (!statuses.TryGetValue(rec.CatalogueId, out var serverStatus) || string.IsNullOrEmpty(serverStatus))
                        continue;

                    rec.LastCheckedUtc = DateTime.UtcNow;

                    if (!string.Equals(rec.Status, serverStatus, StringComparison.OrdinalIgnoreCase))
                    {
                        rec.Status = serverStatus;
                        changed = true;
                    }

                    if (IsCatalogueAcceptedStatus(serverStatus) && !rec.AcceptedNotified)
                    {
                        rec.AcceptedNotified = true;
                        changed = true;
                        NotifyCatalogueSubmissionAccepted(kind, rec.CatalogueId, kvp.Key);
                    }
                }

                if (changed)
                {
                    App.Settings?.Save();
                    RefreshCatalogueShareBadges(kind);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Catalogue] CheckCatalogueSubmissionStatuses failed: {Error}", ex.Message);
            }
            finally
            {
                if (kind == CatalogueKindPresets) _cataloguePresetCheckInFlight = false; else _catalogueSessionCheckInFlight = false;
            }
        }

        // Sticky toast keyed by catalogue id so it survives until the user dismisses
        // it — the "notify next time you start up" behaviour, shared with Deeper.
        private void NotifyCatalogueSubmissionAccepted(string kind, string catalogueId, string key)
        {
            try
            {
                string name = ResolveCatalogueDisplayName(kind, key);
                var msg = Loc.GetF("catalogue_submission_accepted_toast_fmt", name);
                App.Notifications?.ShowSticky(
                    "catalogue_submission_accepted_" + catalogueId,
                    msg,
                    NotificationType.Success);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Catalogue] NotifyCatalogueSubmissionAccepted failed: {Error}", ex.Message);
            }
        }

        // Best-effort human-readable name for toasts: session file name, or the
        // preset's display name from UserPresets.
        private string ResolveCatalogueDisplayName(string kind, string key)
        {
            try
            {
                if (kind == CatalogueKindSessions)
                {
                    var fileName = System.IO.Path.GetFileNameWithoutExtension(key);
                    if (fileName.EndsWith(".session", StringComparison.OrdinalIgnoreCase))
                        fileName = fileName[..^8];
                    return fileName;
                }
                if (kind == CatalogueKindPresets)
                {
                    var preset = App.Settings?.Current?.UserPresets?.FirstOrDefault(p => p.Id == key);
                    if (preset != null && !string.IsNullOrEmpty(preset.Name)) return preset.Name;
                }
            }
            catch { }
            return key;
        }
    }
}
