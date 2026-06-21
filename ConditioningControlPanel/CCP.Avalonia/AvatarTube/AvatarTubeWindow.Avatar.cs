using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;

#pragma warning disable CS0169 // Avalonia port: unused stub fields kept for future companion/avatar work
#pragma warning disable CS0414
#pragma warning disable CS0649

namespace ConditioningControlPanel.Avalonia.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        // Emotive portrait fields (stubbed)
        private object? _portraitSet;
        private int _skinIndex;
        private string _currentEmotion = "neutral";
        private int _emotionPoseIndex;
        private DispatcherTimer? _emotionReturnTimer;
        private DispatcherTimer? _poseSeqTimer;

        private double _breathPhase;
        private double _wobblePhase;
        private double _mistPhase;
        private double _speakPhase;
        private double _speakEnvPhase;

        private const double BreathAmplitude = 0.01;
        private const double WobbleAmplitudeDeg = 0.4;
        private const double SpeakWobbleDeg = 0.175;
        private const double SpeakShakePx = 0.25;
        private const double PortraitSizeScale = 0.88;
        private const double PortraitRaisePx = 30;
        private const double PortraitShiftX = 10;
        private const double LegacyAvatarMaxHeight = 306;
        private const double LegacyAvatarMaxWidth = 198;

        private readonly string[] AvatarTitleKeys =
        {
            "avatar_title_basic_bimbo",
            "avatar_title_dumb_airhead",
            "avatar_title_synthetic_blowdoll",
            "avatar_title_perfect_fuckpuppet",
            "avatar_title_brainwashed_slavedoll",
            "avatar_title_platinum_puppet",
            "avatar_title_bambi_cow"
        };

        public void StartPoseAnimation() => _poseTimer.Start();
        public void StopPoseAnimation() => _poseTimer.Stop();

        public void SetPose(int poseNumber)
        {
            if (poseNumber < 1 || poseNumber > 4) return;
            if (_avatarPoses.Length == 0) return;
            _currentPoseIndex = poseNumber - 1;
            if (ImgAvatar != null) ImgAvatar.Source = _avatarPoses[_currentPoseIndex];
        }

        public void SetPoseInterval(TimeSpan interval)
        {
            _poseTimer.Interval = interval;
        }

        public static int GetAvatarSetForLevel(int level) => 7;
        public static bool IsAvatarSetUnlocked(int setNumber, int level) => true;

        public int[] GetUnlockedAvatarSets(int level)
        {
            return new[] { 1, 2, 3, 4, 7, 5, 6 };
        }

        public int[] EffectiveAvatarSets()
        {
            if (IsSingleEmoteAvatarMod(out int onlySet)) return new[] { onlySet };
            return GetUnlockedAvatarSets(_settings?.Current?.PlayerLevel ?? 1);
        }

        public void UpdateAvatarForLevel(int newLevel)
        {
            int newMax = GetAvatarSetForLevel(newLevel);
            if (newMax > _maxUnlockedSet)
            {
                _maxUnlockedSet = newMax;
                _selectedAvatarSet = newMax;
                if (_settings?.Current != null)
                    _settings.Current.SelectedAvatarSet = _selectedAvatarSet;
                SwitchToAvatarSet(newMax, animate: true);
            }
            UpdateTitleDisplay(newLevel);
            UpdateNavigationArrows();
        }

        private bool HasAnimatedAvatar(int setNumber)
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"Resources/animated{setNumber}_1.gif");
                return File.Exists(path);
            }
            catch { return false; }
        }

        private void LoadAnimatedAvatar(int setNumber)
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"Resources/animated{setNumber}_1.gif");
                if (ImgAvatar != null) ImgAvatar.IsVisible = false;
                if (ImgAvatarAnimated != null)
                {
                    ImgAvatarAnimated.IsVisible = true;
                    ImgAvatarAnimated.Source = File.Exists(path) ? new Bitmap(path) : null;
                }
                _poseTimer.Stop();
                TryUpdateCirceEmoteMode();
            }
            catch (Exception ex)
            {
                _logger?.Warning("Failed to load animated avatar {Set}: {Error}", setNumber, ex.Message);
                _useAnimatedAvatar = false;
                if (ImgAvatar != null) ImgAvatar.IsVisible = true;
                if (ImgAvatarAnimated != null) ImgAvatarAnimated.IsVisible = false;
            }
        }

        private Bitmap[] LoadAvatarPoses(int setNumber)
        {
            var list = new List<Bitmap>();
            for (int i = 1; i <= 4; i++)
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"Resources/avatar{setNumber}_pose{i}.png");
                if (File.Exists(path))
                {
                    try { list.Add(new Bitmap(path)); }
                    catch { }
                }
            }
            return list.ToArray();
        }

        private void RefreshAvatarAnimation()
        {
            if (!_useAnimatedAvatar) return;
            LoadAnimatedAvatar(_currentAvatarSet);
        }

        private void PauseAvatarGif()
        {
            if (_circeEmoteMode) { CircePause(); return; }
            // TODO: pause Avalonia GIF animation.
        }

        private void ResumeAvatarGif()
        {
            if (_circeEmoteMode) { CirceResume(); return; }
            // TODO: resume Avalonia GIF animation.
        }

        private int _avatarSwitchGen;
        private void SwitchToAvatarSet(int setNumber, bool animate = true)
        {
            _currentAvatarSet = setNumber;
            _selectedAvatarSet = setNumber;
            _useAnimatedAvatar = HasAnimatedAvatar(setNumber);
            if (_settings?.Current != null)
                _settings.Current.SelectedAvatarSet = setNumber;

            // TODO: cross-fade animation, portrait mode, companion switch.
            if (_useAnimatedAvatar)
                LoadAnimatedAvatar(setNumber);
            else
            {
                _avatarPoses = LoadAvatarPoses(setNumber);
                _currentPoseIndex = 0;
                if (ImgAvatar != null) ImgAvatar.Source = _avatarPoses.Length > 0 ? _avatarPoses[0] : null;
                if (!_portraitMode && _avatarPoses.Length > 1) _poseTimer.Start();
            }

            UpdateTitleDisplay(_settings?.Current?.PlayerLevel ?? 1);
            UpdateNavigationArrows();
            ApplyAvatarTransform(setNumber);
            TryUpdateCirceEmoteMode();
        }

        private static CompanionId? GetCompanionForAvatarSet(int setNumber)
        {
            return setNumber switch
            {
                3 => CompanionId.OGBambiSprite,
                4 => CompanionId.CultBunny,
                5 => CompanionId.BrainParasite,
                6 => CompanionId.BambiTrainer,
                7 => CompanionId.BimboCow,
                _ => null
            };
        }

        public static int GetAvatarSetForCompanion(CompanionId companionId)
        {
            return companionId switch
            {
                CompanionId.OGBambiSprite => 3,
                CompanionId.CultBunny => 4,
                CompanionId.BrainParasite => 5,
                CompanionId.BambiTrainer => 6,
                CompanionId.BimboCow => 7,
                _ => 1
            };
        }

        private void UpdateTitleDisplay(int level)
        {
            var companionId = GetCompanionForAvatarSet(_currentAvatarSet);
            if (companionId.HasValue)
            {
                var def = CompanionDefinition.GetById(companionId.Value);
                var progress = new CompanionProgress { Level = 1 };
                bool isMax = progress.IsMaxLevel;
                if (TxtAvatarTitle != null) TxtAvatarTitle.Text = def.GetDisplayName(false).ToUpperInvariant();
                if (TxtAvatarLevel != null)
                {
                    TxtAvatarLevel.IsVisible = true;
                    TxtAvatarLevel.Text = isMax
                        ? Loc.Get("avatar_level_max")
                        : Loc.GetF("avatar_level_format", progress.Level);
                }
            }
            else
            {
                int idx = Math.Clamp(_currentAvatarSet - 1, 0, AvatarTitleKeys.Length - 1);
                if (TxtAvatarTitle != null) TxtAvatarTitle.Text = Loc.Get(AvatarTitleKeys[idx]);
                if (TxtAvatarLevel != null)
                {
                    TxtAvatarLevel.IsVisible = _currentAvatarSet > 2;
                    if (_currentAvatarSet > 2) TxtAvatarLevel.Text = Loc.GetF("avatar_level_format", level);
                }
            }
        }

        public void RefreshCompanionDisplay()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(RefreshCompanionDisplay);
                return;
            }
            UpdateTitleDisplay(_settings?.Current?.PlayerLevel ?? 1);
        }

        private void UpdateNavigationArrows()
        {
            var sets = EffectiveAvatarSets();
            bool hasMultiple = sets.Length > 1;
            int idx = Array.IndexOf(sets, _currentAvatarSet);
            if (BtnPrevAvatar != null) BtnPrevAvatar.IsVisible = hasMultiple && idx > 0;
            if (BtnNextAvatar != null) BtnNextAvatar.IsVisible = hasMultiple && idx < sets.Length - 1;
        }

        private void ApplyAvatarTransform(int setNumber)
        {
            if (AvatarBorder == null) return;
            if (setNumber > 1)
            {
                var group = new TransformGroup();
                group.Children.Add(new ScaleTransform(1.12, 1.12));
                group.Children.Add(new TranslateTransform(10, 0));
                AvatarBorder.RenderTransform = group;
                AvatarBorder.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            }
            else
            {
                AvatarBorder.RenderTransform = null;
            }
        }

        public void SwitchToCompanionAvatar(CompanionId companionId)
        {
            int target = GetAvatarSetForCompanion(companionId);
            int level = _settings?.Current?.PlayerLevel ?? 1;
            if (IsAvatarSetUnlocked(target, level))
                SwitchToAvatarSet(target, animate: true);
        }

        private void OnModChanged(object? sender, global::ConditioningControlPanel.Models.ModPackage e)
        {
            UpdateTitleDisplay(_settings?.Current?.PlayerLevel ?? 1);
            TryUpdateCirceEmoteMode();
            // TODO: refresh tube frame image and video links when mod assets are exposed.
        }

        private void ApplyTubeLayoutOffsets()
        {
            // TODO: apply mod tube layout offsets to margins.
        }

        private void SetTubeStyle(bool detached)
        {
            // TODO: swap tube frame image for mod/attach state.
        }

        private bool ModOverridesAttachedTubeOnly() => false;
        internal double EffAvatarScale() => 1.0;
        internal int EffAvatarOffsetX() => 0;
        internal int EffAvatarOffsetY() => 0;
        internal int EffAvatarDetachedOffsetX() => 0;
        internal int EffAvatarDetachedOffsetY() => 0;

        // Portrait mode stubs
        private bool UsePortraitSystem() => false;
        private void TryEnterPortraitMode() { }
        private void LeavePortraitMode()
        {
            _portraitMode = false;
            _portraitSet = null;
            _poseSeqTimer?.Stop();
            _emotionReturnTimer?.Stop();
            if (ImgAvatarB != null) { ImgAvatarB.IsVisible = false; ImgAvatarB.Source = null; }
            if (MistOverlay != null) MistOverlay.IsVisible = false;
        }
        private void ReloadPortraitSkin() { }
        private void ApplyPortraitChrome() { }
        private void CancelCrossfade() { }
        private void CrossfadeTo(Bitmap? next, bool preempt = false) { }

        private bool IsSingleEmoteAvatarMod(out int set)
        {
            set = 0;
            return false;
        }
    }
}

#pragma warning restore CS0169
#pragma warning restore CS0414
#pragma warning restore CS0649
