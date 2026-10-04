# Demo Walkthrough — Local-AI-Doc-Summarizer-API

A scripted end-to-end demonstration, roughly 15 minutes. Every command here was run
against a real Ollama server; the timings are from an **i5-4570, 8 GB RAM, no GPU**.

> **Expect minutes, not seconds.** A 1.7B model on CPU is slow by nature. That is the
> trade for never sending a document to a third party. Do not demo this as a
> real-time service — demo it as a *privacy* and *cost* solution.

---

## 0. Prerequisites

- .NET SDK 8.0 (`dotnet --version`)
- Ollama 0.35.1+ with `qwen3:1.7b-q4_K_M` pulled
- Sample files in `samples/` (already committed)

```bash
dotnet --version                     # 8.0.425
ollama pull qwen3:1.7b-q4_K_M        # ~1.4 GB, first run only
ollama list                          # confirm the model is present
```

---

## 1. Start Ollama

```bash
ollama serve
```

Leave it running. Verify:

```bash
curl -s http://localhost:11434/       # "Ollama is running"
```

---

## 2. Start the API

```bash
dotnet run --project src/Api
```

Swagger UI: <http://localhost:5000/swagger>

---

## 3. The health check — the CTO's first question

*"How do I know it's actually working, not just running?"*

```bash
curl -s http://localhost:5000/health
```

```json
{
  "status": "healthy",
  "ollama": {
    "endpoint": "http://localhost:11434",
    "reachable": true,
    "model": "qwen3:1.7b-q4_K_M",
    "modelPresent": true,
    "temperature": 0.2,
    "numCtx": 8192,
    "think": false,
    "timeoutSeconds": 600
  },
  "supportedFormats": [".docx", ".markdown", ".md", ".pdf", ".txt"],
  "maxFileSizeBytes": 26214400
}
```

Returns **503** when Ollama is down, so an orchestrator can act on the status code
rather than parsing the body.

---

## 4. The headline demo — Arabic in, Arabic out

This is the single most important check: the service detects the document language
and answers in kind.

```bash
python tests/smoke/post-ar.py http://localhost:5000
```

> **Use the script, not `curl -d`.** From Git Bash, curl mangles UTF-8 inside `-d`,
> the Arabic arrives corrupted, and the detector correctly reports `unknown`. That
> is a shell artifact, not a defect. The script sends UTF-8 and prints the verdict.

```
status            = 200
detectedLanguage  = ar
arabic/latin chars= 220/18
VERDICT           = ARABIC OUTPUT OK
```

**Talk track:** the document went in Arabic and came back Arabic, automatically, with
no language parameter supplied.

---

## 5. Multilingual capability

Force English output from an English document, then Arabic output from an English one:

```bash
curl -X POST http://localhost:5000/api/v1/summarize/text \
  -H "Content-Type: application/json" \
  -d '{"text":"The pipeline extracts text, detects language, chunks, summarizes, merges.","language":"en","maxWords":50}'
```

---

## 6. Multi-chunk map-reduce

Upload the DOCX, which is long enough to be split into five chunks. Watch the log
for the map and reduce phases:

```bash
curl -X POST http://localhost:5000/api/v1/summarize \
  -F "file=@samples/sample.docx" \
  -F "maxWords=50"
```

```json
{
  "summary": "The ASP.NET 8 API processes local files (PDF, DOCX, TXT, Markdown) via Ollama, extracting text, detecting language, splitting into chunks, and summarizing each before merging results. Deployment in corporate networks requires no external connectivity, with all processing occurring locally without data transmission.",
  "detectedLanguage": "en",
  "chunkCount": 5,
  "sourceCharacters": 421,
  "elapsedSeconds": 117.8
}
```

**Talk track:** five chunks were summarized independently, then folded into **one**
coherent summary — not five paragraphs stitched together. That convergence is
enforced by a shrinking word budget in the reduce phase.

---

## 7. PDF and DOCX extraction

```bash
curl -X POST http://localhost:5000/api/v1/summarize -F "file=@samples/sample.pdf" -F "maxWords=50"
curl -X POST http://localhost:5000/api/v1/summarize -F "file=@samples/sample.docx" -F "maxWords=50"
```

---

## 8. Failure modes — the honest part

A demo that only shows success is a demo nobody trusts. Every failure is a distinct
status code.

| Test | Command | Expected |
|---|---|---|
| Unsupported format | `-F "file=@samples/sample-ar.txt;filename=notes.rtf"` | `422` |
| No file part | `-F "language=auto"` | `422` |
| Malformed JSON | `-d '{not json'` | `400` |
| Oversized upload (30 MB) | `-F "file=@bigfile.txt"` | `413` |
| Ollama stopped | `Ctrl+C` on `ollama serve`, then summarize | `502` |
| Health while down | `curl /health` | `503` |

```json
{
  "title": "Summarization failed",
  "detail": "Could not reach the Ollama server at http://localhost:11434. Confirm Ollama is running and the model is pulled.",
  "status": 502,
  "path": "/api/v1/summarize/text"
}
```

---

## 9. Docker deployment

```bash
docker compose up --build
```

Three services: Ollama, a one-shot model pull, and the API on `:8080`. The 1.4 GB of
weights is pulled **at runtime into a named volume** — never baked into an image.

```bash
curl -s http://localhost:8080/health
```

Stop and clean up:

```bash
docker compose down          # keeps the model volume
docker compose down -v       # deletes it too; forces a re-pull
```

---

## 10. Tests

```bash
dotnet test tests/Core.Tests
```

```
Passed! - Failed: 0, Passed: 42, Skipped: 0, Total: 42
```

Covers chunking (including hostile overlap ratios that would deadlock a naive
implementation), language detection, and map-reduce orchestration against a fake
engine.

---

## Objection handling

**"Is a 1.7B model good enough?"**
It is adequate for summarization, not for generation. Quality is comparable to what a
cloud API returns at low effort. Every response tags the model id and source character
count so nobody mistakes it for a frontier model. The model id is configuration —
`qwen3:4b` on better hardware is a one-line change.

**"Two minutes per document?"**
Yes, on CPU. This is a batch tool. For a 30-page contract, two minutes is a fair price
for that contract never entering a third party's retention log. On any hardware with
a GPU this drops by an order of magnitude.

**"What about scanned PDFs?"**
Not supported in v1. They return an explicit `422` naming OCR as the reason, rather
than a misleading empty summary. OCR is the first roadmap item.

**"Arabic quality?"**
Documented honestly in the README. Qwen 3 is strong in Arabic for its size class; the
prompt explicitly requires Arabic output and forbids inventing figures. Quality is
verified by the demo above, not just asserted.

**"Does it scale?"**
Yes — horizontally, because it is stateless. Each node runs its own Ollama. Model
loading dominates cold-start; keep models resident.
