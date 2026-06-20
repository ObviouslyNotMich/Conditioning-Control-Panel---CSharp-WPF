namespace ConditioningControlPanel.Core.Platform;

public interface IScreenProvider
{
    IReadOnlyList<ScreenInfo> GetAllScreens();
    ScreenInfo? GetPrimaryScreen();
    event EventHandler? ScreensChanged;
}

public sealed record ScreenInfo(string Name, PixelRect Bounds, PixelRect WorkingArea, double Scaling);

public sealed record PixelRect(double X, double Y, double Width, double Height);
