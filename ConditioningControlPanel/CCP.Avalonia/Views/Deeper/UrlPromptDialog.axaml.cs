using System;
using Avalonia.Controls;
using Avalonia.Input;
using ConditioningControlPanel.Core.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

/// <summary>
/// Tiny modal that prompts for a https://... URL pointing at a .ccpenh.json file.
/// </summary>
public partial class UrlPromptDialog : Window
{
    public string? Result { get; private set; }

    public UrlPromptDialog(string? initial = null)
    {
        InitializeComponent();
        if (!string.IsNullOrEmpty(initial)) TxtUrl.Text = initial;
        Loaded += (_, _) => { TxtUrl.Focus(); TxtUrl.SelectAll(); };
    }

    private void BtnOk_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var url = (TxtUrl.Text ?? "").Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            TxtError.Text = Loc.Get("deeper_url_prompt_invalid");
            TxtError.IsVisible = true;
            return;
        }
        Result = url;
        Close(true);
    }

    private void BtnCancel_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(false);
    }

    private void TxtUrl_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) BtnOk_Click(sender, new global::Avalonia.Interactivity.RoutedEventArgs());
    }
}
