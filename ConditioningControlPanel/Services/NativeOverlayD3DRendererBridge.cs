using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using D3DDevice = SharpDX.Direct3D11.Device;
using D3DDeviceContext = SharpDX.Direct3D11.DeviceContext;

namespace ConditioningControlPanel.Services;

public sealed class NativeOverlayD3DRendererBridge : IDisposable
{
    private const string OverlayWindowClass = "CCP_NativeOverlayWindow";
    private const int SwShowNoActivate = 4;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExTopmost = 0x00000008;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolwindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExLayered = 0x00080000;
    private const int SwpNoActivate = 0x0010;
    private const int SwpNoMove = 0x0002;
    private const int SwpNoSize = 0x0001;
    private const int SwpShowWindow = 0x0040;
    private const int LwaAlpha = 0x00000002;
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly WndProc WindowProcDelegate = WindowProc;
    private static ushort _windowClassAtom;

    private D3DDevice? _device;
    private D3DDeviceContext? _context;
    private SwapChain? _swapChain;
    private Texture2D? _backBuffer;
    private RenderTargetView? _targetView;
    private uint? _attachedProcessId;
    private IntPtr _overlayHwnd;
    private Screen? _attachedScreen;
    private bool _initialized;
    private bool _disposed;

    public bool IsInitialized => _initialized;
    public bool IsAttached => _attachedProcessId.HasValue;

    public bool TryInitialize(out string reason)
    {
        reason = string.Empty;
        if (_disposed)
        {
            reason = "renderer-disposed";
            return false;
        }

        if (_initialized)
            return true;

        try
        {
            var creationFlags = DeviceCreationFlags.BgraSupport;
            _device = new D3DDevice(DriverType.Hardware, creationFlags, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1);
            _context = _device.ImmediateContext;

            _initialized = true;
            reason = "initialized";
            return true;
        }
        catch (Exception ex)
        {
            reason = "d3d-init-failed: " + ex.Message;
            DisposeResources();
            return false;
        }
    }

    public bool TryAttachTarget(NativeOverlayTargetSnapshot target, out string reason)
    {
        reason = string.Empty;

        if (!_initialized)
        {
            reason = "renderer-not-initialized";
            return false;
        }

        if (!target.IsAttachReady)
        {
            reason = "target-not-attach-ready";
            return false;
        }

        if (target.ProcessId == (uint)Environment.ProcessId)
        {
            reason = "target-is-self";
            return false;
        }

        var screen = TryResolveScreen(target.ScreenDeviceName);
        if (screen == null)
        {
            reason = "target-screen-not-found";
            return false;
        }

        if (!EnsureOverlaySurface(screen, out reason))
            return false;

        _attachedProcessId = target.ProcessId;
        _attachedScreen = screen;
        reason = "attached";
        return true;
    }

    public void DetachTarget()
    {
        _attachedProcessId = null;
        _attachedScreen = null;
        DestroyOverlaySurface();
    }

    public bool IsHealthy(out string reason)
    {
        reason = string.Empty;

        if (!_initialized || _device == null)
        {
            reason = "renderer-not-initialized";
            return false;
        }

        if (_device.DeviceRemovedReason.Failure)
        {
            reason = "d3d-device-lost";
            return false;
        }

        return true;
    }

    public bool TryRenderFrame(NativeOverlayDesiredState desiredState, out string reason)
    {
        reason = string.Empty;

        if (!_initialized || _context == null || _targetView == null || _swapChain == null || _overlayHwnd == IntPtr.Zero)
        {
            reason = "renderer-not-ready";
            return false;
        }

        if (!_attachedProcessId.HasValue)
        {
            reason = "no-attached-target";
            return false;
        }

        try
        {
            float alpha = desiredState.PinkEnabled ? Math.Clamp(desiredState.PinkOpacity / 50f, 0f, 1f) : 0f;
            var clear = new RawColor4(1f, 105f / 255f, 180f / 255f, 1f);
            byte layeredAlpha = (byte)Math.Clamp(alpha * 255f, 0f, 255f);

            _ = SetLayeredWindowAttributes(_overlayHwnd, 0, layeredAlpha, LwaAlpha);
            _ = SetWindowPos(_overlayHwnd, HwndTopmost, 0, 0, 0, 0, SwpNoActivate | SwpNoMove | SwpNoSize | SwpShowWindow);

            _context.OutputMerger.SetRenderTargets(_targetView);
            _context.ClearRenderTargetView(_targetView, clear);
            _context.Flush();
            _swapChain.Present(0, PresentFlags.None);
            reason = "rendered";
            return true;
        }
        catch (Exception ex)
        {
            reason = "render-failed: " + ex.Message;
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeResources();
    }

    private void DisposeResources()
    {
        _attachedProcessId = null;
        _attachedScreen = null;
        _initialized = false;

        DestroyOverlaySurface();

        try { _targetView?.Dispose(); } catch { }
        try { _backBuffer?.Dispose(); } catch { }
        try { _swapChain?.Dispose(); } catch { }
        try { _context?.Dispose(); } catch { }
        try { _device?.Dispose(); } catch { }

        _targetView = null;
        _backBuffer = null;
        _swapChain = null;
        _context = null;
        _device = null;
    }

    private bool EnsureOverlaySurface(Screen screen, out string reason)
    {
        reason = string.Empty;

        if (_device == null)
        {
            reason = "d3d-device-missing";
            return false;
        }

        try
        {
            var bounds = screen.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                reason = "invalid-screen-bounds";
                return false;
            }

            bool requiresRebuild = _overlayHwnd == IntPtr.Zero ||
                                   _swapChain == null ||
                                   _attachedScreen == null ||
                                   !string.Equals(_attachedScreen.DeviceName, screen.DeviceName, StringComparison.OrdinalIgnoreCase);

            if (!requiresRebuild)
            {
                _ = SetWindowPos(_overlayHwnd, HwndTopmost, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SwpNoActivate | SwpShowWindow);
                return true;
            }

            DestroyOverlaySurface();

            if (!TryCreateOverlayWindow(bounds.Left, bounds.Top, bounds.Width, bounds.Height, out reason))
                return false;

            if (!TryCreateSwapChainForWindow(bounds.Width, bounds.Height, out reason))
            {
                DestroyOverlaySurface();
                return false;
            }

            _ = ShowWindow(_overlayHwnd, SwShowNoActivate);
            _ = SetWindowPos(_overlayHwnd, HwndTopmost, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SwpNoActivate | SwpShowWindow);
            _attachedScreen = screen;
            return true;
        }
        catch (Exception ex)
        {
            reason = "overlay-surface-create-failed: " + ex.Message;
            return false;
        }
    }

    private bool TryCreateSwapChainForWindow(int width, int height, out string reason)
    {
        reason = string.Empty;

        if (_device == null || _overlayHwnd == IntPtr.Zero)
        {
            reason = "swapchain-prerequisites-missing";
            return false;
        }

        try
        {
            using var dxgiDevice = _device.QueryInterface<SharpDX.DXGI.Device>();
            using var adapter = dxgiDevice.Adapter;
            using var factory = adapter.GetParent<Factory>();

            var desc = new SwapChainDescription
            {
                BufferCount = 2,
                ModeDescription = new ModeDescription(width, height, new Rational(60, 1), Format.B8G8R8A8_UNorm),
                IsWindowed = true,
                OutputHandle = _overlayHwnd,
                SampleDescription = new SampleDescription(1, 0),
                SwapEffect = SwapEffect.Discard,
                Usage = Usage.RenderTargetOutput,
                Flags = SwapChainFlags.AllowModeSwitch
            };

            _swapChain = new SwapChain(factory, _device, desc);
            using var parentFactory = _swapChain.GetParent<Factory>();
            parentFactory.MakeWindowAssociation(_overlayHwnd, WindowAssociationFlags.IgnoreAll);

            _backBuffer = _swapChain.GetBackBuffer<Texture2D>(0);
            _targetView = new RenderTargetView(_device, _backBuffer);
            reason = "swapchain-created";
            return true;
        }
        catch (Exception ex)
        {
            reason = "swapchain-create-failed: " + ex.Message;
            return false;
        }
    }

    private static Screen? TryResolveScreen(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
            return Screen.PrimaryScreen;

        foreach (var screen in Screen.AllScreens)
        {
            if (string.Equals(screen.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                return screen;
        }

        return Screen.PrimaryScreen;
    }

    private bool TryCreateOverlayWindow(int left, int top, int width, int height, out string reason)
    {
        reason = string.Empty;
        EnsureWindowClass();

        var hInstance = GetModuleHandle(null);
        _overlayHwnd = CreateWindowEx(
            WsExTopmost | WsExTransparent | WsExToolwindow | WsExNoActivate | WsExLayered,
            OverlayWindowClass,
            "CCP Native Overlay",
            WsPopup,
            left,
            top,
            width,
            height,
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (_overlayHwnd == IntPtr.Zero)
        {
            reason = "create-overlay-window-failed: " + Marshal.GetLastWin32Error();
            return false;
        }

        _ = SetLayeredWindowAttributes(_overlayHwnd, 0, 0, LwaAlpha);
        return true;
    }

    private void DestroyOverlaySurface()
    {
        try { _targetView?.Dispose(); } catch { }
        try { _backBuffer?.Dispose(); } catch { }
        try { _swapChain?.Dispose(); } catch { }

        _targetView = null;
        _backBuffer = null;
        _swapChain = null;

        if (_overlayHwnd != IntPtr.Zero)
        {
            try { _ = DestroyWindow(_overlayHwnd); } catch { }
            _overlayHwnd = IntPtr.Zero;
        }
    }

    private static void EnsureWindowClass()
    {
        if (_windowClassAtom != 0) return;

        var wcx = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WindowProcDelegate),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = GetModuleHandle(null),
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = OverlayWindowClass,
            hIconSm = IntPtr.Zero
        };

        _windowClassAtom = RegisterClassEx(ref wcx);
        if (_windowClassAtom == 0)
        {
            int err = Marshal.GetLastWin32Error();
            if (err != 1410)
                throw new InvalidOperationException("RegisterClassEx failed: " + err);
        }
    }

    private static IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, int uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
