namespace ConditioningControlPanel.Core.Platform;

public interface IScreenProvider
{
    IReadOnlyList<ScreenInfo> GetAllScreens();
    ScreenInfo? GetPrimaryScreen();
    event EventHandler? ScreensChanged;
}

public sealed record ScreenInfo(string Name, PixelRect Bounds, PixelRect WorkingArea, double Scaling);

public sealed record PixelRect(double X, double Y, double Width, double Height)
{
    public static PixelRect Empty { get; } = new(0, 0, 0, 0);
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public double Right => X + Width;
    public double Bottom => Y + Height;
}
