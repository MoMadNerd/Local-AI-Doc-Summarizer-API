"""Generates a minimal single-page PDF with a real text layer.

Used only to produce samples/sample.pdf for manual extractor verification.
No third-party packages: the PDF is assembled directly from its objects.
"""

lines = [
    "Local AI Document Summarizer - Sample Document",
    "",
    "This PDF was generated to verify text extraction works end to end.",
    "The pipeline extracts text, detects language, chunks, summarizes, merges.",
    "All processing runs locally. No document data leaves the machine.",
]


def escape(text: str) -> str:
    """Escapes the characters that terminate a PDF literal string."""
    return text.replace("\\", r"\\").replace("(", r"\(").replace(")", r"\)")


content = "BT\n/F1 12 Tf\n72 720 Td\n14 TL\n"
for line in lines:
    content += f"({escape(line)}) Tj\nT*\n"
content += "ET"

objects = [
    "<< /Type /Catalog /Pages 2 0 R >>",
    "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
    "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] "
    "/Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
    f"<< /Length {len(content)} >>\nstream\n{content}\nendstream",
    "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
]

pdf = "%PDF-1.4\n"
offsets = []
for index, obj in enumerate(objects, start=1):
    offsets.append(len(pdf))
    pdf += f"{index} 0 obj\n{obj}\nendobj\n"

xref_offset = len(pdf)
pdf += f"xref\n0 {len(objects) + 1}\n0000000000 65535 f \n"
for offset in offsets:
    pdf += f"{offset:010d} 00000 n \n"
pdf += (
    f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R >>\n"
    f"startxref\n{xref_offset}\n%%EOF\n"
)

with open("sample.pdf", "w", encoding="latin-1") as handle:
    handle.write(pdf)

print(f"wrote sample.pdf ({len(pdf)} bytes)")
