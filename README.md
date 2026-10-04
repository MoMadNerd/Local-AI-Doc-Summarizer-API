# Local-AI-Doc-Summarizer-API

# Enterprise-Grade Local AI Document Summarization. Zero Data Leakage. Runs on Commodity Hardware.

**An ASP.NET 8 API that summarizes PDF, DOCX, TXT and Markdown documents entirely on your own infrastructure. No document ever leaves your network — not to OpenAI, not to Azure, not to any cloud provider.**

**Validated on a 2013 Intel i5-4570, 8 GB RAM, zero GPU.**

---

## The Problem

Most enterprises sit in one of two bad options:

| Option | What it costs you |
|---|---|
| **Cloud LLM APIs** (OpenAI, Azure) | Every document is transmitted to a third party. Contracts, PII, source code and M&A material sit in someone else's retention logs. This is a legal and compliance liability, not a technical one. |
| **Enterprise GPU clusters** | A single A100 node costs more than this entire project. And for *summarization* — not training, not inference at scale — that hardware is wildly over-provisioned. |

The result: teams either accept the data risk, or accept the hardware cost. Most end up quietly not deploying summarization at all.

## The Solution

A self-hosted summarization API that runs on the CPU you already own.

- **No data egress.** The document is read, chunked, summarized, and discarded on your machine. `docker compose up` is the entire trust boundary.
- **No GPU.** Tested and validated end-to-end on a **legacy i5-4570, 8 GB RAM, zero GPU** — hardware you have thousands of already deployed.
- **No API keys, no per-token billing, no rate limits.**
- **OpenAI-compatible inference** via Ollama. Swap the model without touching a line of C#.

### Who it is for

Legal, compliance, HR and finance teams processing contracts, case files, medical records or board documents — anywhere sending text to a third-party API is legally or reputationally unacceptable.

---

## Architecture & Engineering Rigor

```
Upload → Extract text → Detect language → Chunk → Summarize each chunk (MAP) → Merge (REDUCE) → JSON
```

| Component | Implementation |
|---|---|
| **PDF** | PdfPig, per-page. Scanned PDFs fail explicitly rather than returning a misleading empty summary. |
| **DOCX** | Direct OOXML parse via `System.IO.Compression` — no third-party dependency. |
| **TXT / Markdown** | UTF-8 with Latin-1 fallback; Markdown syntax stripped. |
| **Language detection** | Deterministic script counting. Arabic must beat Latin by a 1.5× margin, so borrowed English technical terms cannot flip a bilingual document. No model call, no token cost. |
| **Chunking** | Paragraph-aware with ~10% overlap. |
| **Summarization** | Sequential map-reduce over OllamaSharp. |

### Three decisions worth knowing about

**1. Overlap can never deadlock the splitter.** A naive offset-based chunker deadlocks when the requested overlap meets or exceeds the chunk size. Ours advances through a *paragraph list* with the overlap clamped into `[start+1, end]`, so forward progress is guaranteed by construction. Tested against hostile ratios — `0.0`, `1.0`, `5.0`, `-1.0` and `NaN` — all terminate and preserve every paragraph.

**2. Map-reduce is strictly sequential.** On a 4-core CPU a 1.7B model is already the bottleneck; concurrent requests would add memory pressure and make wall-clock time *less* predictable, not more. Verified by a test that fails if two model calls ever overlap.

**3. The model returns plain text; JSON lives only in the API envelope.** Constraining a 1.7B model to a JSON grammar measurably degrades prose and produces spurious failures. Structure is applied by the API, where it is deterministic.

### Correctness

Every pipeline failure maps to a status code a client can act on without reading the body:

`400` bad request · `413` file too large · `422` accepted but unreadable · `502` model backend failed · `503` degraded (Ollama or model unavailable) · `504` backend timeout.

**42 automated tests** cover chunking, language detection and map-reduce, including two regression tests for defects found during live testing (a dropped language hint, and a reduce phase that failed to converge).

---

## API

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/api/v1/summarize` | multipart upload → summary |
| `POST` | `/api/v1/summarize/text` | raw JSON text → summary |
| `GET` | `/health` | service + Ollama reachability + model presence |
| `GET` | `/swagger` | interactive API explorer |

```bash
curl -X POST http://localhost:8080/api/v1/summarize/text \
  -H "Content-Type: application/json" \
  -d '{"text":"Your confidential document...","language":"auto","maxWords":150}'
```

```json
{
  "summary": "…",
  "detectedLanguage": "ar",
  "chunkCount": 3,
  "sourceCharacters": 18422,
  "model": "qwen3:1.7b-q4_K_M",
  "elapsedSeconds": 118.4,
  "warnings": []
}
```

Arabic documents return **Arabic** summaries — verified live, not just asserted.

---

## Quick Start

### Docker (recommended)

```bash
docker compose up --build
```

Three services start: Ollama, a one-shot model pull, and the API on `:8080`. The ~1.4 GB of weights is pulled **at runtime into a named volume** — never baked into an image — so builds stay fast and the image stays small.

### Native

```bash
# 1. .NET 8 SDK
curl -sSL https://dot.net/v1/dotnet-install.ps1 | bash -s -- -Channel 8.0

# 2. Ollama + the model
ollama pull qwen3:1.7b-q4_K_M

# 3. Run
dotnet run --project src/Api
```

Full walkthrough in [docs/DEMO.md](docs/DEMO.md).

### Performance reality

| Document | Hardware | Time |
|---|---|---|
| 367-char text | i5-4570, CPU only | ~17–30 s |
| 5-chunk document | i5-4570, CPU only | ~118 s |

**Be honest with your team about this:** a 1.7B model on CPU is slow. It is not a latency-optimized service. It is a *privacy* and *cost* solution that runs on hardware you already own. Summarization is a batch task, not a real-time one — and for a 30-page document, two minutes is an acceptable price for never filing that document with a third party.

---

## Roadmap

- **OCR for scanned and image-only PDFs** — the single biggest gap. Currently returns an explicit `422` rather than a misleading empty result.
- **Larger models** — `qwen3:4b` or larger on hardware that can afford them; the model id is configuration, not code.
- **Batch endpoint** — submit a directory, receive a manifest.
- **Key-point extraction** — ranked bullet output alongside prose.
- **Streaming responses** — progressive delivery for long documents.

---

## License

MIT — see [LICENSE](LICENSE).

---

---

# تلخيص المستندات محلياً بالذكاء الاصطناعي — بدون تسريب أي بيانات

**تلخيص مستندات على مستوى المؤسسات، يعمل بالكامل داخل شبكتك. لا تغادر أي وثيقة جهازك — لا إلى OpenAI، ولا إلى Azure، ولا إلى أي مزوّد سحابي.**

**تم اختباره والتحقق منه على معالج Intel i5-4570 من عام 2013، وذاكرة 8 جيجابايت، بدون كرت شاشة.**

## المشكلة

تعاني معظم المؤسسات من أحد خيارين، وكلاهما مكلف:

| الخيار | ما يكلّفك |
|---|---|
| **واجهات LLM سحابية** | كل مستند يُنقل إلى طرف ثالث. العقود والبيانات الشخصية وشيفرة المصدر ومواد الاندماج تبقى في سجلات احتفاظ لا تملكها. هذا التزام قانوني وامتثال، لا مشكلة تقنية. |
| **عُقد من كروت GPU** | عقدة A100 واحدة تكلّف أكثر من هذا المشروع بأكمله. والتلخيص — لا التدريب ولا الاستدلال الضخم — لا يحتاج هذه القدرة. |

النتيجة: إمّا القبول بمخاطر البيانات، أو قبول كلفة العتاد. أغلب الفرق تختار بهدوء عدم نشر أي حل تلخيص.

## الحل

واجهة تلخيص ذاتية الاستضافة تعمل على المعالج الذي تملكه أصلاً.

- **لا صادر للبيانات.** تُقرأ الوثيقة، وتُقطَّع، وتُلخَّص، وتُمحى على جهازك. `docker compose up` هي حدود الثقة كاملة.
- **لا GPU.** مُختبَر ومُتحقَّق منه من طرف إلى طرف على **i5-4570 قديم، 8GB، بدون GPU** — عتاد تملك آلاف النسخ منه.
- **لا مفاتيح API، ولا فوترة لكل رمز، ولا حدود استخدام.**
- **توافق مع OpenAI** عبر Ollama. بدّل النموذج دون تعديل سطر واحد في C#.

### لمن هذا المشروع

فرق القانونية والامتثال والموارد البشرية والمالية التي تعالج عقوداً أو ملفات قضايا أو سجلات طبية أو وثائق مجالس — في كل مكان لا يُقبل فيه إرسال النص إلى واجهة طرف ثالث قانونياً أو تجارياً.

## البنية الهندسية

```
رفع ملف → استخراج النص → كشف اللغة → التقطيع → تلخيص كل قطعة (MAP) → الدمج (REDUCE) → JSON
```

**ثلاث قرارات هندسية تستحق الذكر:**

1. **تراكب القطع لا يمكن أن يعلّق المُقطِّع أبداً.** المُقطِّع يسير عبر *قائمة الفقرات* مع قصّ التراكب داخل `[start+1, end]`، فالتقدّم مضمون بنيوياً. مُختبَر مع نسب استفزازية: `0.0` و`1.0` و`5.0` و`-1.0` و`NaN` — كلها تنتهي وتحفظ كل فقرة.

2. **map-reduce تسلسلي تماماً.** على معالج 4 أنوية، النموذج 1.7B هو عنق الزجاجة أصلاً؛ التوازي يزيد الضغط على الذاكرة ويجعل الزمن **أقل** قابلية للتنبؤ لا أكثر. مُتحقَّق منه باختبار يفشل إذا تداخل نداءان.

3. **النموذج يُعيد نصاً عادياً؛ وJSON في غلاف الـAPI فقط.** تقييد نموذج صغير على قواعد JSON يُرفع جودة الصياغة ويفشل عشوائياً. البنية يضيفها الـAPI حيث تكون حتمية.

**42 اختباراً آلياً** تغطي التقطيع وكشف اللغة وmap-reduce، منها اختباران للحالتين اكتُشفتا أثناء الاختبار الحي: تمرير اللغة إلى المحرّك، وتقارب مرحلة الدمج.

## التشغيل السريع

```bash
# Docker (موصى به)
docker compose up --build

# محلياً
ollama pull qwen3:1.7b-q4_K_M
dotnet run --project src/Api
```

الشرح الكامل في [docs/DEMO.md](docs/DEMO.md).

## خارطة الطريق

- **OCR للمستندات الممسوحة ضوئياً** — أكبر فجوة حالياً؛ يُرجع `422` صريحاً بدل نتيجة فارغة مضلّلة.
- **نماذج أكبر** — `qwen3:4b` أو ما فوقه على عتاد يتحمّل ذلك.
- **نقطة دفع مجمّعة** — مجلد كامل يُعالج دفعة واحدة.
- **استخراج النقاط المهمة** — مخرجات نقطية مرتّبة إلى جانب النص.
- **بثّ النتائج تدريجياً.**

## الترخيص

MIT — راجع [LICENSE](LICENSE).
