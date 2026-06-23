namespace ConditioningControlPanel.Core.Services.Auth;

/// <summary>
/// Cross-platform seam for SP3 device-code login.
/// User confirms the code on the web dashboard; the desktop polls until confirmed.
/// </summary>
public interface IV2DeviceCodeService
{
    /// <summary>
    /// Canonical pairing page URL shown to the user.
    /// </summary>
    string VerificationUrl { get; }

    /// <summary>
    /// Initiate a new device-code session.
    /// </summary>
    Task<InitiateResponse> InitiateAsync(CancellationToken ct = default);

    /// <summary>
    /// Poll for confirmation of the given device code.
    /// </summary>
    Task<PollResponse> PollAsync(string code, CancellationToken ct = default);
}
