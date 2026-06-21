using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;
using global::Avalonia.Platform;
using ConditioningControlPanel.Models;

using Svg.Skia;
using SK = SkiaSharp;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>Story vs Free Desktop play mode. Mirrors the WPF enum.</summary>
public enum ChaosPlayMode { Story, FreeDesktop }

/// <summary>Announcement palette kind. Mirrors the WPF enum.</summary>
public enum ChaosAnnounceKind { Mantra, Temptation, Willpower, Depth, Streak, Item, PowerUp, Narrator }

/// <summary>Stubbed service bridge for the Avalonia Chaos overlay port.
/// These static facades stand in for the WPF service locator (App.* and Services.Chaos.*)
/// until the engine is fully extracted into CCP.Core and wired through DI.</summary>
public static class AvaloniaChaosEnv
{
    /// <summary>Effective assets root used by art loaders. TODO: wire IAppEnvironment.</summary>
    public static string? EffectiveAssetsPath { get; set; }

    /// <summary>Video service IsPlaying proxy. TODO: wire IVideoSurface.</summary>
    public static bool VideoIsPlaying => false;

    /// <summary>Bubble service proxy. TODO: wire IBubbleService.</summary>
    public static IAvaloniaBubbleService? Bubbles { get; set; }
}

/// <summary>Stubbed bubble service surface used by chaos overlays.</summary>
public interface IAvaloniaBubbleService
{
    double ChaosRabbitTrailSecNow { get; }
    void PopBubblesInRect(global::Avalonia.Rect rectDips);
    bool AnyDarterIntersects(global::Avalonia.Rect rectDips);
}

/// <summary>Static stub for ChaosModeService. TODO: replace with real run-state service.</summary>
public static class AvaloniaChaosMode
{
public static ChaosPlayMode ActiveMode { get; set; } = ChaosPlayMode.Story;
    public static bool DesktopMode => ActiveMode == ChaosPlayMode.FreeDesktop;
    public static bool BornTopmost => !DesktopMode;
    public static bool NarrativeActive =>
        App.Services?.GetService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>()?.Current?.NarrativeModeEnabled == true && ActiveMode == ChaosPlayMode.Story;

}

/// <summary>Static stub for ChaosSfx. TODO: replace with IAudioPlayer.</summary>
public static class AvaloniaChaosSfx
{
    public static void Play(string name, float scale = 0.6f)
    {
        // TODO: play cross-platform SFX once IAudioPlayer is wired.
    }

    public static void PlayBoonReveal(bool rare) { }
    public static void PlayBoonPicked() { }
}

/// <summary>Static stub for ChaosArt. TODO: replace with IAssetLoader + image cache.</summary>
public static class AvaloniaChaosArt
{
    private static string[] Roots()
    {
        var user = AvaloniaChaosEnv.EffectiveAssetsPath;
        return string.IsNullOrEmpty(user)
            ? new[] { AppContext.BaseDirectory }
            : new[] { user, AppContext.BaseDirectory };
    }

    public static IImage? TryLoad(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".svg")
                return LoadSvg(path);

            using var stream = File.OpenRead(path);
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Information("AvaloniaChaosArt.TryLoad failed: {E}", ex.Message);
            return null;
        }
    }

    private static IImage? LoadSvg(string path)
    {
        try
        {
            using var svg = new SKSvg();
            using var stream = File.OpenRead(path);
            var picture = svg.Load(stream);
            if (picture == null) return null;

            var rect = picture.CullRect;
            int width = Math.Max(1, (int)Math.Ceiling(rect.Width));
            int height = Math.Max(1, (int)Math.Ceiling(rect.Height));

            var info = new SK.SKImageInfo(width, height, SK.SKColorType.Bgra8888, SK.SKAlphaType.Premul);
            using var bitmap = new SK.SKBitmap(info);
            using (var canvas = new SK.SKCanvas(bitmap))
            {
                canvas.Clear(SK.SKColors.Transparent);
                canvas.DrawPicture(picture);
            }

            var wb = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);

            CopySkBitmapToWriteableBitmap(bitmap, wb);
            return wb;
        }
        catch (Exception ex)
        {
            App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Information("AvaloniaChaosArt.LoadSvg failed: {E}", ex.Message);
            return null;
        }
    }

    private static unsafe void CopySkBitmapToWriteableBitmap(SK.SKBitmap src, WriteableBitmap dst)
    {
        using var fb = dst.Lock();
var source = (byte*)src.GetPixels().ToPointer();
        var dest = (byte*)fb.Address.ToPointer();
        var rowBytes = Math.Min(src.RowBytes, fb.RowBytes);
        var height =
Math.Min(src.Height, fb.Size.Height);

        for (int y = 0; y < height; y++)
        {
            System.Buffer.MemoryCopy(
                source + y * src.RowBytes,
                dest + y * fb.RowBytes,
                rowBytes,
                rowBytes);
        }
    }

    public static IImage? Resolve(string kind, string id)
    {
        foreach (var root in Roots())
        {
            var p = Path.Combine(root, "assets", "Chaos", kind, id + ".png");
            var img = TryLoad(p);
            if (img != null) return img;
        }
        return null;
    }

}

/// <summary>Minimal data stub for a HUD boon tile. Mirrors WPF ChaosSidebarBoon.</summary>
public sealed class ChaosSidebarBoon
{
    public string Id { get; init; } = "";
    public IImage? Icon { get; init; }
    public string Glyph { get; init; } = "◈";
    public string Name { get; init; } = "";
    public int Level { get; init; }
    public string Desc { get; init; } = "";
    public string Flavor { get; init; } = "";
    public bool IsCurse { get; init; }
    public bool IsModifier { get; init; }
    public bool IsEmptySlot { get; init; }

    private static IBrush Frozen(Color c) { var b = new SolidColorBrush(c); return b; }
    private static readonly IBrush EmptyAccent = Frozen(Color.FromArgb(0x60, 0xB8, 0xB8, 0xD0));
    private static readonly IBrush PocketAccent = Frozen(Color.FromRgb(0xFF, 0x69, 0xB4));
    private static readonly IBrush BoonAccent = Frozen(Color.FromRgb(0x9C, 0xE8, 0xA0));
    private static readonly IBrush CurseAccent = Frozen(Color.FromRgb(0xFF, 0x8A, 0x8A));
    private static readonly IBrush ModAccent = Frozen(Color.FromRgb(0x8B, 0x5C, 0xF6));
    private static readonly IBrush PocketBack = Frozen(Color.FromArgb(0x33, 0xFF, 0x69, 0xB4));
    private static readonly IBrush BoonBack = Frozen(Color.FromArgb(0x2E, 0x9C, 0xE8, 0xA0));
    private static readonly IBrush CurseBack = Frozen(Color.FromArgb(0x2E, 0xFF, 0x8A, 0x8A));
    private static readonly IBrush ModBack = Frozen(Color.FromArgb(0x2E, 0x8B, 0x5C, 0xF6));

    public IBrush AccentBrush => IsEmptySlot ? EmptyAccent : IsModifier ? ModAccent : IsCurse ? CurseAccent : Level > 0 ? PocketAccent : BoonAccent;
    public IBrush TileBackBrush => IsEmptySlot ? Brushes.Transparent : IsModifier ? ModBack : IsCurse ? CurseBack : Level > 0 ? PocketBack : BoonBack;

    // Tooltip / binding helpers used by the Avalonia HUD data templates.
    public string TipTitle => Name;
    public string? Extra { get; init; }
    public bool DescVisibility => !string.IsNullOrEmpty(Desc);
    public bool FlavorVisibility => !string.IsNullOrEmpty(Flavor);
    public bool ExtraVisibility => !string.IsNullOrEmpty(Extra);
    public double TileOpacity => IsEmptySlot ? 0.55 : 1.0;
    public bool LevelBadgeVisibility => Level > 0;
    public string LevelText => "L" + Level;
}
