# Local Light Novel translation (TranslateGemma)

Jularr can translate Light Novel chapters into German with a local TranslateGemma model. This runs next to the AI translation and does not replace it:

| | AI translation | Local translation |
|---|---|---|
| Stored as | `NovelTranslations`, track `de` | `NovelTranslations`, track `de-gemma` |
| Uses story context, characters, glossary | yes | no, it only translates text |
| Needs | an AI provider | a TranslateGemma endpoint |

Both tracks can exist for the same chapter. The reader then offers Original / German / Both, and in the language menu the German source Local / AI / Both. Bookmarks, highlights and reading progress keep the track they were made on (`de` or `de-gemma`).

Cached translations stay readable when generation is off (Learning capability, no endpoint, endpoint offline). The Learning capability only gates starting a new translation.

## Running the model

The Compose file has an optional Ollama service. It runs on the CPU with the quantized `translategemma:12b` model (Q4_K_M, 8.1 GB download) and keeps it loaded.

```sh
TRANSLATEGEMMA_ENDPOINT=http://translategemma:11434/v1/chat/completions \
  docker compose --profile translategemma up -d
```

- The model is pulled into the `translategemma-models` volume on first start. The reader shows the local controls once the endpoint is set.
- The service has no host port. Only Jularr reaches it on the Compose network.
- One chapter is translated at a time. Further chapters wait in the job queue.

### Hardware

| Model | Download | RAM in use | Speed on an older 4-core Intel CPU |
|---|---|---|---|
| `translategemma:12b` (default) | 8.1 GB | about 10 GB | roughly 1–3 tokens/s, so a long chapter takes 30–90 minutes |
| `translategemma:4b` | 3.3 GB | about 4–5 GB | about three times faster, lower quality |

`TRANSLATEGEMMA_MEMORY_LIMIT` (default `12g`) caps the container. An integrated Intel GPU does not help. For the 4B model set `TRANSLATEGEMMA_OLLAMA_TAG=translategemma:4b`.

### Another server

Any OpenAI-compatible chat-completions endpoint works:

- **Ollama / LM Studio**: work as they are.
- **llama.cpp** (`llama-server`): start it with `--chat-template gemma -c 4096`. The template embedded in the GGUF cannot render normal chat messages.
- **vLLM / SGLang** with the official Hugging Face chat template: Jularr falls back to that format when the server rejects the first one.

## Settings

| Compose variable | ASP.NET key | Default | Notes |
|---|---|---|---|
| `TRANSLATEGEMMA_ENDPOINT` | `TranslateGemma__Endpoint` | empty (off) | Absolute `http`/`https` URL of `/v1/chat/completions`. An invalid URL turns the feature off and logs one warning. |
| `TRANSLATEGEMMA_MODEL` | `TranslateGemma__Model` | `translategemma-12b-it` | Model name sent to the server. The bundled service registers the Ollama model under this name. |
| `TRANSLATEGEMMA_MAX_CHUNK_CHARACTERS` | `TranslateGemma__MaxChunkCharacters` | `1200` | Characters per request (400–3000). TranslateGemma is trained for 2K input tokens. |
| `TRANSLATEGEMMA_TIMEOUT_MINUTES` | `TranslateGemma__TimeoutMinutes` | `30` | Limit per request (1–120). |
| `TRANSLATEGEMMA_OLLAMA_TAG` | – | `translategemma:12b` | Only for the bundled service. |
| `TRANSLATEGEMMA_MEMORY_LIMIT` | – | `12g` | Only for the bundled service. |

A cached local translation records model, endpoint and algorithm version. After one of them changes, the old text stays readable and the reader offers "Translate again locally".

## How a chapter is translated

- One request per group of up to 8 consecutive paragraphs, at most `MaxChunkCharacters` long, so dialogue keeps its context. A group is only accepted when the answer has as many paragraphs as the source. Otherwise it is split in half and sent again, down to single paragraphs. The stored translation always has one paragraph per source paragraph.
- A paragraph longer than the limit is split at sentence ends and joined again.
- Paragraphs without letters (scene breaks such as `◇◇◇`, `……`) are copied unchanged.
- The source language is detected per paragraph: kana/kanji means `ja`, Hangul means `ko`, otherwise `en`. The target is `de-DE`.
- Request format: the instruction of the official TranslateGemma chat template as one user message, with no system prompt. If the server answers 400/422, Jularr retries with the structured content item (`type`, `source_lang_code`, `target_lang_code`, `text`) and then with the vLLM delimiter form.
- Timeouts, connection errors, 408, 429 and 5xx are retried up to three times. When a chapter fails, its finished paragraphs are kept in memory and the retried job only sends the missing ones.
