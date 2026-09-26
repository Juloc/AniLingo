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

It has no Reader-specific state. `speakSequence(items, options)` speaks an array or lazily pulled iterable of items as one cancellable session: only the current utterance plus `lookAhead` upcoming utterances are ever handed to the platform queue, and item-level `itemstart` / `itemend` / `boundary` events carry the caller's key. `resolveSpeech` is the browser mirror of `SpeechPreferenceResolver` / `SpeechAvailabilityResolver` (same provider order, voice matching and unavailable reasons), because device voices only exist on the client.

Important privacy distinction: AniLingo's browser Device provider itself sends no TTS request to the AniLingo server and makes no third-party HTTP request. Whether a platform/browser voice is fully local is controlled by that operating system/browser. `SpeechSynthesisVoice.localService` is exposed as metadata but must not be treated as a universal privacy guarantee. Only an AniLingo-managed offline-neural model is classified as `GuaranteedOffline`.

## Phase 2: Reader integration

The shared Reader owns the UI integration; the speech engine stays Reader-agnostic. `wwwroot/js/reader-tts.js` mounts through the `reader-shell.js` extension API, so Books, Light Novels and Web Novels share one implementation. Its markup is only rendered for documents whose `ReaderCapabilities.SupportsTts` is set (reflowable text); manga and fixed documents get no controls.

Controls:
- toolbar Read button (mobile: `Vorlesen` in the bottom action bar): reads the current text selection, otherwise continues from the first paragraph on screen; while active it toggles Pause/Resume
- a player bar with Pause/Resume and Stop stays visible while reading, independent of the auto-hiding chrome
- low-frequency actions in the overflow menu: read the current paragraph or the visible page

Behavior:
- paragraphs are pulled lazily with a look-ahead of two utterances; Stop cancels the session and the platform queue
- the spoken paragraph is marked and, where the browser reports word boundaries, the spoken word is highlighted with the CSS Custom Highlight API (annotated paragraph DOM is not modified)
- the view follows the spoken text: continuous mode scrolls, paged mode turns pages through the shared page-edge event; manual scrolling suspends following for a few seconds
- reading continues across the paragraphs and pages of a chapter; at the chapter end it stops unless the explicit `ttsAutoContinueChapters` setting is on, in which case it opens the next chapter and continues from its first paragraph
- Pause cancels and Resume restarts from the last spoken word, because Web Speech `pause()` is unreliable on mobile browsers
- switching the visible language stops reading

Settings live in the canonical `ReaderPreference` store and use the normal scope cascade with per-field inheritance: `ttsProviderId`, `ttsRate`, `ttsPitch`, `ttsVolume`, `ttsAutoContinueChapters` and one voice per language (`ttsVoiceId:<bcp47>`, stored as a JSON map per scope in which every language entry inherits independently). The `Vorlesen` settings tab shows one voice choice per document language and explains when a stored voice is missing on this device, when no voice for the language is installed (the device default is used) or when the browser cannot speak. `ReaderSettingsSnapshot.SpeechPreferencesFor(language)` is the server-side bridge from these settings to the resolver for native clients.

State and privacy:
- the TTS cursor lives only in the page session; reader-tts.js never writes reading progress. Progress keeps its existing meaning (what is on screen), so a view that follows the voice is saved like any other scroll
- spoken text is never sent to the server or persisted; the chapter auto-continue hand-off stores only the next chapter id in `sessionStorage` for one minute

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
