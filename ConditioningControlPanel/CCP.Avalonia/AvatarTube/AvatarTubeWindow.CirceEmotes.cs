using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private CirceEmoteEngine? _circeEngine;
        private bool _circeMapValid;
        private readonly List<CirceRegistryEntry> _circeRegistry = new();

        private sealed record CirceRegistryEntry(string? ModId, int AvatarSet, string Folder, bool Fallback);

        // Emote registry resolution --------------------------------------------------------

        private void TryUpdateCirceEmoteMode()
        {
            if (_portraitMode) return;

            string? folder = ResolveCirceFolderForCurrentState();
            bool wanted = _useAnimatedAvatar && !string.IsNullOrEmpty(folder);

            if (wanted)
            {
                if (!_circeEmoteMode)
                {
                    _circeEmoteMode = true;
                    EnterCirceEmoteMode(folder!);
                }
                else if (_circeEngine?.IsActive != true)
                {
                    EnterCirceEmoteMode(folder!);
                }
            }
            else if (_circeEmoteMode)
            {
                LeaveCirceEmoteMode();
            }
        }

        private string? ResolveCirceFolderForCurrentState()
        {
            LoadCirceRegistryIfNeeded();

            string modId = App.Services.GetService<global::ConditioningControlPanel.IModService>()?.ActiveMod?.Id
                           ?? "builtin-bambisleep";

            var exact = _circeRegistry.FirstOrDefault(x => !x.Fallback
                && x.ModId != null
                && x.ModId.Equals(modId, StringComparison.OrdinalIgnoreCase)
                && x.AvatarSet == _currentAvatarSet);
            if (exact != null) return exact.Folder;

            var fallback = _circeRegistry.FirstOrDefault(x => x.Fallback);
            return fallback?.Folder;
        }

        private void LoadCirceRegistryIfNeeded()
        {
            if (_circeRegistry.Count > 0) return;
            try
            {
                var loader = App.Services.GetRequiredService<IAssetLoader>();
                var json = loader.ReadTextAsync(new Uri("avares://CCP.Avalonia/Assets/avatar_emotes_registry.json")).Result;
                var root = JObject.Parse(json);
                if (root["sets"] is JArray sets)
                {
                    foreach (var item in sets)
                    {
                        var m = (string?)item["modId"];
                        var a = (int?)item["avatarSet"];
                        var f = (string?)item["folder"];
                        if (!string.IsNullOrEmpty(m) && !string.IsNullOrEmpty(f))
                            _circeRegistry.Add(new CirceRegistryEntry(m, a.GetValueOrDefault(1), f, false));
                    }
                }
                // Built-in fallback chain so the tube works even when a registry entry is missing.
                _circeRegistry.Add(new CirceRegistryEntry(null, 1, "avatar0_emotes", true));
            }
            catch (Exception ex)
            {
                _logger?.Warning(ex, "Failed to load avatar emote registry");
                _circeRegistry.Add(new CirceRegistryEntry(null, 1, "avatar0_emotes", true));
            }
        }

        private void EnterCirceEmoteMode(string folder)
        {
            if (ImgAvatarAnimated == null || ImgAvatarAnimatedB == null)
                return;

            _circeEngine ??= new CirceEmoteEngine(ImgAvatarAnimated, ImgAvatarAnimatedB,
                App.Services.GetRequiredService<IAssetLoader>(), _logger, _random);

            _circeEngine.Leave();
            _circeEngine.ClipStarted += OnCirceClipStarted;

            _ = Task.Run(async () =>
            {
                bool ok = await _circeEngine.EnterAsync(folder).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (ok)
                    {
                        _circeMapValid = true;
                        ImgAvatar!.IsVisible = false;
                        ImgAvatarB!.IsVisible = false;
                        ReassertCirceEmoteVisuals();
                    }
                    else
                    {
                        _circeMapValid = false;
                        _circeEmoteMode = false;
                        ImgAvatarAnimated.IsVisible = false;
                        ImgAvatarAnimatedB.IsVisible = false;
                        ImgAvatar!.IsVisible = true;
                    }
                });
            });
        }

        private void LeaveCirceEmoteMode()
        {
            _circeEmoteMode = false;
            _circeMapValid = false;
            _circeEngine?.Leave();
            if (ImgAvatarAnimated != null) ImgAvatarAnimated.IsVisible = false;
            if (ImgAvatarAnimatedB != null) ImgAvatarAnimatedB.IsVisible = false;
            if (ImgAvatar != null) ImgAvatar.IsVisible = true;
            if (ImgAvatarB != null) ImgAvatarB.IsVisible = false;
        }

        private void ReassertCirceEmoteVisuals()
        {
            if (_circeEngine?.IsActive != true || ImgAvatarAnimated == null) return;

            double baseScale = 1.0;
            double scaleMul = _circeEngine.EffScaleMul;
            int offX = _isAttached ? _circeEngine.EffOffsetX : _circeEngine.EffDetachedOffsetX;
            int offY = _isAttached ? _circeEngine.EffOffsetY : _circeEngine.EffDetachedOffsetY;

            ApplyImageTransform(ImgAvatarAnimated, baseScale * scaleMul, offX, offY);
            ApplyImageTransform(ImgAvatarAnimatedB, baseScale * scaleMul, offX, offY);

            ImgAvatarAnimated.IsVisible = true;
            ImgAvatarAnimatedB.IsVisible = true;
        }

        private static void ApplyImageTransform(Image img, double scale, int offX, int offY)
        {
            var group = new TransformGroup();
            group.Children.Add(new ScaleTransform(scale, scale));
            group.Children.Add(new TranslateTransform(offX, offY));
            img.RenderTransform = group;
        }

        private void OnCirceClipStarted(string clip)
        {
            if (_circeEngine?.HasLayout != true || ImgAvatarAnimated == null) return;
            ReassertCirceEmoteVisuals();
        }

        private void CircePlayEmote(string? emotionLineId, string? audioPath, string? text, string? mood)
        {
            if (!_circeEmoteMode || _circeEngine == null)
                return;
            _circeEngine.PlayEmote(emotionLineId, audioPath, text, mood);
        }

        private void CircePause()
        {
            _circeEngine?.Pause();
        }

        private void CirceResume()
        {
            _circeEngine?.Resume();
        }

        internal bool CirceClickEmote()
        {
            if (!_circeEmoteMode || _circeEngine == null) return false;
            return _circeEngine.ClickEmote();
        }

        private void RefreshVoiceLines()
        {
            // Legacy WPF voice-line cache invalidation. The LibVLC-backed IAudioPlayer
            // loads clips on demand; nothing to refresh here.
        }

        private bool LoadCirceMap()
        {
            // Replaced by async registry + engine enter; kept as sync probe.
            TryUpdateCirceEmoteMode();
            return _circeMapValid;
        }
    }
}
