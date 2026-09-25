# Text-to-speech architecture

Issue: #317

AniLingo uses one provider-neutral speech contract. Reader, learning and future native clients must not bind directly to one vendor or one model family.

## Provider order

The canonical fallback order is:

1. explicitly selected provider/voice when it is available and language-compatible
2. installed offline-neural provider
3. platform/device TTS
4. explicitly configured cloud provider
5. unavailable

The resolver uses BCP-47 language tags. Voice matching is deterministic: exact locale first, then base language. A platform provider may fall back to its own default voice selection rather than selecting a known wrong-language voice.

## Phase 1: browser Device TTS

`wwwroot/js/tts.js` wraps the browser Web Speech API behind `AniLingoTts.DeviceSpeechProvider`.

It provides:
- asynchronous voice discovery including `voiceschanged`
- language/voice selection
- bounded long-text chunking
- rate, pitch and volume controls
- play, stop, pause and resume
- utterance and boundary events with offsets in the original input text
- session cancellation so an old queue cannot continue after Stop or a new Speak request

It has no Reader-specific state. The shared Reader can consume the event API after its current architecture work is merged.

Important privacy distinction: AniLingo's browser Device provider itself sends no TTS request to the AniLingo server and makes no third-party HTTP request. Whether a platform/browser voice is fully local is controlled by that operating system/browser. `SpeechSynthesisVoice.localService` is exposed as metadata but must not be treated as a universal privacy guarantee. Only an AniLingo-managed offline-neural model is classified as `GuaranteedOffline`.

## Phase 2: Reader integration

The shared Reader should own the UI integration, not the speech engine.

Expected behavior:
- read selection, current paragraph/page, or continue from the current logical Reader position
- small bounded look-ahead queue
- spoken paragraph/word follow highlighting when boundary events exist
- pause/resume/stop in shared Reader chrome
- per-language voice choice
- per-profile defaults plus existing Reader scope inheritance
- TTS cursor remains separate from persisted reading progress

No duplicate Book/Novel TTS implementation should be introduced.

## Phase 3: offline neural

Use sherpa-onnx as the execution boundary. It supports multiple offline TTS model families, including VITS/Piper and Kokoro, and documents Android, iOS and WebAssembly builds.

The model manager must use explicit downloadable model packs. A manifest owns:
- provider/model id and model version
- supported language tags and voices
- required runtime/model files
- expected byte size
- SHA-256 for every downloaded artifact
- minimum compatible AniLingo/runtime version

Activation is download -> verify -> atomic move. Partial or checksum-failed downloads never become selectable.

Model files stay outside the main web image/APK so server/Docker and Android release paths are not inflated by every language model.

## Native clients

Android:
- isolated `core-tts` module
- system provider uses Android `TextToSpeech`
- offline provider uses sherpa-onnx locally
- app-mobile consumes the provider-neutral contract
- native TTS build work remains outside the server/Docker release critical path

iOS, when a native client exists:
- system provider uses `AVSpeechSynthesizer`
- offline provider follows the same model-manifest contract through sherpa-onnx iOS runtime

## Source references

- Web Speech API: https://developer.mozilla.org/docs/Web/API/SpeechSynthesisUtterance
- sherpa-onnx TTS model families: https://k2-fsa.github.io/sherpa/onnx/c-api/html/tts.html
- sherpa-onnx Android build: https://k2-fsa.github.io/sherpa/onnx/android/build-sherpa-onnx.html
- sherpa-onnx iOS build: https://k2-fsa.github.io/sherpa/onnx/ios/build-sherpa-onnx-swift.html
- sherpa-onnx WebAssembly TTS: https://k2-fsa.github.io/sherpa/onnx/tts/wasm/build.html
