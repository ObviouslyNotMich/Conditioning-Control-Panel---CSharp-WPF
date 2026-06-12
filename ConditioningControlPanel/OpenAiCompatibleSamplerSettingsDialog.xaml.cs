using System;
using System.Globalization;
using System.Windows;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Modal editor for the OpenAI-compatible provider's sampler parameters.
    /// Only populated values are persisted; empty fields become null and are
    /// omitted from the outbound request so strict OpenAI endpoints don't see
    /// unsupported keys like top_k/repetition_penalty/min_p.
    /// </summary>
    public partial class OpenAiCompatibleSamplerSettingsDialog : Window
    {
        private readonly CompanionPromptSettings _settings;

        public OpenAiCompatibleSamplerSettingsDialog(CompanionPromptSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            InitializeComponent();
            Loaded += (_, _) => LoadFromSettings();
        }

        private void LoadFromSettings()
        {
            ChkUseCustomSamplers.IsChecked = _settings.OpenAiCompatibleUseCustomSamplerSettings;
            UpdateInputState();

            TxtTemperature.Text = FormatNullable(_settings.OpenAiCompatibleTemperature);
            TxtTopP.Text = FormatNullable(_settings.OpenAiCompatibleTopP);
            TxtTopK.Text = FormatNullable(_settings.OpenAiCompatibleTopK);
            TxtFrequencyPenalty.Text = FormatNullable(_settings.OpenAiCompatibleFrequencyPenalty);
            TxtPresencePenalty.Text = FormatNullable(_settings.OpenAiCompatiblePresencePenalty);
            TxtRepetitionPenalty.Text = FormatNullable(_settings.OpenAiCompatibleRepetitionPenalty);
            TxtMinP.Text = FormatNullable(_settings.OpenAiCompatibleMinP);
        }

        private static string FormatNullable(double? value)
            => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;

        private static string FormatNullable(int? value)
            => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;

        private void ChkUseCustomSamplers_Changed(object sender, RoutedEventArgs e)
        {
            UpdateInputState();
        }

        private void UpdateInputState()
        {
            InputsPanel.IsEnabled = ChkUseCustomSamplers.IsChecked == true;
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            ChkUseCustomSamplers.IsChecked = false;
            TxtTemperature.Text = string.Empty;
            TxtTopP.Text = string.Empty;
            TxtTopK.Text = string.Empty;
            TxtFrequencyPenalty.Text = string.Empty;
            TxtPresencePenalty.Text = string.Empty;
            TxtRepetitionPenalty.Text = string.Empty;
            TxtMinP.Text = string.Empty;
            UpdateInputState();
            TxtError.Visibility = Visibility.Collapsed;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            TxtError.Visibility = Visibility.Collapsed;

            var useCustom = ChkUseCustomSamplers.IsChecked == true;

            if (!useCustom)
            {
                _settings.OpenAiCompatibleUseCustomSamplerSettings = false;
                DialogResult = true;
                Close();
                return;
            }

            if (!TryParseDouble(TxtTemperature.Text, "temperature", out var temperature)
                || !TryParseDouble(TxtTopP.Text, "top_p", out var topP)
                || !TryParseInt(TxtTopK.Text, "top_k", out var topK)
                || !TryParseDouble(TxtFrequencyPenalty.Text, "frequency_penalty", out var frequencyPenalty)
                || !TryParseDouble(TxtPresencePenalty.Text, "presence_penalty", out var presencePenalty)
                || !TryParseDouble(TxtRepetitionPenalty.Text, "repetition_penalty", out var repetitionPenalty)
                || !TryParseDouble(TxtMinP.Text, "min_p", out var minP))
            {
                return;
            }

            _settings.OpenAiCompatibleUseCustomSamplerSettings = true;
            _settings.OpenAiCompatibleTemperature = temperature;
            _settings.OpenAiCompatibleTopP = topP;
            _settings.OpenAiCompatibleTopK = topK;
            _settings.OpenAiCompatibleFrequencyPenalty = frequencyPenalty;
            _settings.OpenAiCompatiblePresencePenalty = presencePenalty;
            _settings.OpenAiCompatibleRepetitionPenalty = repetitionPenalty;
            _settings.OpenAiCompatibleMinP = minP;

            DialogResult = true;
            Close();
        }

        private bool TryParseDouble(string? text, string fieldName, out double? value)
        {
            value = null;
            var trimmed = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed)) return true;

            if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                ShowError($"'{fieldName}' must be a number.");
                return false;
            }

            value = parsed;
            return true;
        }

        private bool TryParseInt(string? text, string fieldName, out int? value)
        {
            value = null;
            var trimmed = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed)) return true;

            if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                ShowError($"'{fieldName}' must be a whole number.");
                return false;
            }

            value = parsed;
            return true;
        }

        private void ShowError(string message)
        {
            TxtError.Text = message;
            TxtError.Visibility = Visibility.Visible;
        }
    }
}
