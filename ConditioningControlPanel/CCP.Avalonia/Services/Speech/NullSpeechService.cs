using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Services.Speech;

namespace ConditioningControlPanel.Avalonia.Services.Speech;

/// <summary>
/// Default no-op speech recognition for heads without a native engine (non-Windows, or Windows
/// without the Vosk model). Always unavailable: every recognize call returns
/// <see cref="PhraseResult.NotAvailable"/> so callers degrade gracefully (skip, don't fail).
/// The Windows head overrides this with the real Vosk/NAudio implementation.
/// </summary>
public sealed class NullSpeechService : ISpeechRecognitionService
{
    public event EventHandler<string>? PartialTranscript { add { } remove { } }
    public event EventHandler<double>? LevelChanged { add { } remove { } }
    public event EventHandler<bool>? ListeningChanged { add { } remove { } }

    public bool IsListening => false;
    public bool IsAvailable => false;
    public bool HasCaptureDevice => false;

    public IReadOnlyList<SpeechInputDevice> EnumerateInputDevices() => Array.Empty<SpeechInputDevice>();

    public void StopListening() { }

    public Task<PhraseResult> RecognizePhraseAsync(string target, RecognizeOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(PhraseResult.NotAvailable);

    public Task<PhraseResult> RecognizeOneOfAsync(IReadOnlyList<string> phrases, RecognizeOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(PhraseResult.NotAvailable);

    public Task<string?> WaitForWakeWordAsync(IEnumerable<string> wakeWords, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}
