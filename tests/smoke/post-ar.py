"""Sends an Arabic summary request as UTF-8, bypassing Git Bash encoding issues.

Usage: python smoke.py [base-url]
"""

import json
import sys
import urllib.request

# The Windows console defaults to cp1252 and cannot print Arabic. Force UTF-8 so the
# summary is readable here; this affects display only, not the request.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:5203"

ARABIC = (
    "هذا المستند يشرح عملية تلخيص النصوص باستخدام نماذج لغوية محلية تعمل بالكامل على جهازك. "
    "لا تتصل الخدمة بالإنترنت، ولا تغادر بياناتك الجهاز في أي مرحلة من مراحل المعالجة. "
    "تعمل الأداة مع ملفات PDF و DOCX و TXT و Markdown، وتكتشف لغة المستند تلقائياً قبل بدء التلخيص. "
    "يمكن نشر الخدمة داخل الشبكات المؤسسية دون الحاجة إلى أي اتصال خارجي، "
    "مما يجعلها مناسبة للوثائق الحساسة."
)


def summarize(text: str, language: str = "auto", max_words: int = 80) -> int:
    payload = json.dumps(
        {"text": text, "language": language, "maxWords": max_words},
        ensure_ascii=False,
    ).encode("utf-8")

    request = urllib.request.Request(
        f"{BASE}/api/v1/summarize/text",
        data=payload,
        headers={"Content-Type": "application/json; charset=utf-8"},
        method="POST",
    )

    with urllib.request.urlopen(request, timeout=900) as response:
        status = response.status
        body = json.loads(response.read().decode("utf-8"))

    summary = body["summary"]
    arabic = sum(1 for c in summary if "\u0600" <= c <= "\u06FF")
    latin = sum(1 for c in summary if c.isascii() and c.isalpha())

    print(f"status            = {status}")
    print(f"detectedLanguage  = {body['detectedLanguage']}")
    print(f"elapsedSeconds    = {body['elapsedSeconds']}")
    print(f"arabic/latin chars= {arabic}/{latin}")
    print(f"summary           = {summary[:400]}")
    print(f"VERDICT           = {'ARABIC OUTPUT OK' if arabic > latin else 'NOT ARABIC'}")
    return status


if __name__ == "__main__":
    summarize(ARABIC)
