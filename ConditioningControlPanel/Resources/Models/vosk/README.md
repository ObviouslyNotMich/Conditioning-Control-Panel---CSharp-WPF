# Vosk offline speech model (Takeover "repeat after me")

`Services/Speech/SpeechService.cs` loads a [Vosk](https://alphacephei.com/vosk/models) acoustic
model from this folder at runtime. **The model binaries are NOT committed** (same policy as the
ONNX models in `Resources/Models/`) — they're dropped in by the build/release process.

Until a model is present here, `App.Speech.IsAvailable` is `false` and every recognize call
returns `PhraseResult.NotAvailable`, so the app runs normally and the voice action simply never
fires. No crash, no prompt.

## Which model

Ship the small English model — it's ~40 MB and fast enough for closed-grammar verification, which
is all the "repeat after me" mechanic needs:

- `vosk-model-small-en-us-0.15`  (https://alphacephei.com/vosk/models)

## How to install it

Unpack the zip so this folder contains **either**:

1. the model files directly here:
   ```
   Resources/Models/vosk/am/  conf/  graph/  ivector/  README
   ```
2. **or** a single nested model folder (the way the official zip unpacks):
   ```
   Resources/Models/vosk/vosk-model-small-en-us-0.15/am/ conf/ ...
   ```

`SpeechService.ResolveModelDir()` accepts both layouts (it looks for a dir containing `am/` + `conf/`).

The existing `Resources\Models\**\*` content glob in `ConditioningControlPanel.csproj` copies
everything here to the output/publish folder automatically — no csproj change needed.

## Notes

- Native `libvosk` ships inside the `Vosk` NuGet package and extracts via
  `IncludeNativeLibrariesForSelfExtract` for the single-file build.
- Privacy: audio is captured in-memory via NAudio, fed straight to Vosk, and never written to
  disk or transmitted. The mic only opens during an explicit listen window and only after
  `MicConsentGiven` is set.
