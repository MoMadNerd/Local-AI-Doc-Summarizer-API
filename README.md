# Local-AI-Doc-Summarizer-API

**Summarize documents locally — nothing leaves your machine.**

An ASP.NET 8 API that extracts text from PDF, DOCX, TXT and Markdown files and
summarizes it with a local LLM served by [Ollama](https://ollama.com). No cloud
provider, no API keys, no data egress.

> **Status: scaffolding.** The solution structure and dependencies are in place.
> The summarization pipeline is not implemented yet — see [Roadmap](#roadmap).

---

## Architecture

```
Local-AI-Doc-Summarizer-API/
├── src/
│   ├── Api/                        ASP.NET 8 Minimal API (entry point)
│   ├── Core/                       Entities, DTOs, ports (no third-party deps)
│   └── Infrastructure/             Extractors, OllamaSharp engine, chunker
├── tests/
│   └── Core.Tests/                 xUnit
├── samples/                        Sample documents for the demo
├── docs/
│   └── DEMO.md                     Scripted end-to-end walkthrough
├── Dockerfile
├── docker-compose.yml
└── LICENSE                         MIT
```

Dependencies flow one way: `Api → Infrastructure → Core`. `Core` holds no
third-party packages, so the domain logic is unit-testable without HTTP or a
running model.

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 8.0.425 | [Install](#install-the-sdk) |
| Ollama | 0.35.1 or later | Running locally for development |
| Model | `qwen3:1.7b-q4_K_M` | ~1.2 GB, pulled on first use |

## Install the SDK

The .NET 8 SDK installs without administrator rights:

```powershell
powershell -ExecutionPolicy Bypass -File dotnet-install.ps1 -Channel 8.0 -Version 8.0.425 -InstallDir "$HOME/.dotnet"
```

Then persist `PATH` for Git Bash:

```bash
printf '\nexport DOTNET_ROOT="$HOME/.dotnet"\nexport PATH="$PATH:$HOME/.dotnet"\n' >> ~/.bashrc
```

Verify:

```bash
dotnet --version   # 8.0.425
```

## Pull the model

```bash
ollama pull qwen3:1.7b-q4_K_M
```

## Build and test

```bash
dotnet restore LocalAiDocSummarizer.sln
dotnet build   LocalAiDocSummarizer.sln
dotnet test    tests/Core.Tests
```

## Run

```bash
dotnet run --project src/Api
```

Swagger UI is at `/swagger` once the API is up.

## Run with Docker

```bash
docker compose up --build
```

## Packages

| Project | Package | Version |
|---|---|---|
| Infrastructure | `PdfPig` | 0.1.16 |
| Infrastructure | `OllamaSharp` | 5.5.0 |
| Api | `Swashbuckle.AspNetCore` | 6.6.2 |

Swashbuckle is pinned to 6.x because 10.x targets `net10.0` and cannot be used
on .NET 8. It is the sole OpenAPI provider — the template's
`Microsoft.AspNetCore.OpenApi` reference was removed to keep the dependency tree
lean.

## Roadmap

- Extractor implementations (PDF via PdfPig, DOCX, TXT/MD)
- Map-Reduce summarization pipeline with chunking
- `POST /api/v1/summarize` and `POST /api/v1/summarize/text`
- **OCR for scanned/image-only PDFs** — not supported in v1. Such a file
  currently yields no text from PdfPig and will be rejected with an explicit
  error rather than returning a misleading empty summary.

## Limitations

- A 1.7B model produces moderate-quality summaries and may garble figures.
  Responses always tag the model id and source character count so output is
  never mistaken for higher-fidelity work.
- CPU-only inference on 4 cores takes **minutes** per document. This is inherent
  to local inference, not a defect.
- First run downloads ~1.2 GB of model weights.

## License

MIT — see [LICENSE](LICENSE).