using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.WindowsOnly;

/// <summary>
/// Windows Forms NotifyIcon shim for <see cref="ITrayIcon"/>.
/// </summary>
public sealed class WpfTrayIcon : ITrayIcon
{
    private readonly NotifyIcon _notifyIcon;
    private readonly WpfTrayMenu _menu;

    public WpfTrayIcon()
    {
        _notifyIcon = new NotifyIcon
        {
            Text = "Conditioning Control Panel",
            Visible = false
        };

        _menu = new WpfTrayMenu(_notifyIcon);
        LoadIcon();
    }

    public ITrayMenu Menu => _menu;

    public void Show() => _notifyIcon.Visible = true;

    public void Hide() => _notifyIcon.Visible = false;

    public void SetTooltip(string text) => _notifyIcon.Text = text ?? string.Empty;

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private void LoadIcon()
    {
        try
        {
            var packUri = new Uri("pack://application:,,,/Resources/app.ico", UriKind.Absolute);
            var streamInfo = System.Windows.Application.GetResourceStream(packUri);
            if (streamInfo != null)
            {
                _notifyIcon.Icon = new Icon(streamInfo.Stream);
                return;
            }
        }
        catch
        {
            // ignored
        }

        var filePaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Resources", "app.ico"),
            Path.Combine(AppContext.BaseDirectory, "app.ico")
        };

        foreach (var path in filePaths)
        {
            if (File.Exists(path))
            {
                try
                {
                    _notifyIcon.Icon = new Icon(path);
                    return;
                }
                catch
                {
                    // try next
                }
            }
        }

        _notifyIcon.Icon = SystemIcons.Application;
    }
}

/// <summary>
/// Context menu shim for <see cref="ITrayMenu"/>.
/// </summary>
public sealed class WpfTrayMenu : ITrayMenu
{
    private readonly ContextMenuStrip _contextMenu;

    public WpfTrayMenu(NotifyIcon notifyIcon)
    {
        _contextMenu = new ContextMenuStrip();
        notifyIcon.ContextMenuStrip = _contextMenu;
    }

    public void AddItem(string label, Action callback, bool isSeparator = false)
    {
        if (isSeparator)
        {
            _contextMenu.Items.Add(new ToolStripSeparator());
        }
        else
        {
            var item = new ToolStripMenuItem(label);
            item.Click += (_, _) => callback();
            _contextMenu.Items.Add(item);
        }
    }

    public void Clear() => _contextMenu.Items.Clear();
}
