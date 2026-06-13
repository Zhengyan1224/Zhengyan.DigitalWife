from __future__ import annotations

import json
from pathlib import Path


LABEL_SUMMARY = "\u8bf4\u660e"
LABEL_CSHARP = "C# \u5165\u53e3"
LABEL_PYTHON = "Python \u5165\u53e3"


def parse_frontmatter(text: str) -> dict[str, object]:
    meta: dict[str, object] = {}
    if not text.startswith("---"):
        return meta

    end = text.find("\n---", 3)
    if end < 0:
        return meta

    current_key: str | None = None
    for raw_line in text[3:end].strip().splitlines():
        line = raw_line.rstrip()
        if not line:
            continue

        if line.startswith("  - ") and current_key:
            values = meta.setdefault(current_key, [])
            if isinstance(values, list):
                values.append(line[4:].strip())
            continue

        if ":" in line:
            key, value = line.split(":", 1)
            current_key = key.strip()
            value = value.strip()
            meta[current_key] = value.strip('"') if value else []

    return meta


def extract_index_value(text: str, label: str) -> str:
    for raw_line in text.splitlines():
        line = raw_line.strip()
        if not line.startswith("|") or not line.endswith("|"):
            continue

        cells = [cell.strip() for cell in line.strip("|").split("|")]
        if len(cells) >= 2 and cells[0] == label:
            return cells[1]

    return ""


def build_manifest(api_docs_dir: Path) -> list[dict[str, object]]:
    docs: list[dict[str, object]] = []

    for path in sorted(api_docs_dir.glob("*.md")):
        text = path.read_text(encoding="utf-8")
        meta = parse_frontmatter(text)

        title = str(meta.get("title") or path.stem)
        category = str(meta.get("category") or "")
        objects = meta.get("objects") if isinstance(meta.get("objects"), list) else []
        keywords = meta.get("keywords") if isinstance(meta.get("keywords"), list) else []
        summary = extract_index_value(text, LABEL_SUMMARY)
        csharp_entry = extract_index_value(text, LABEL_CSHARP)
        python_entry = extract_index_value(text, LABEL_PYTHON)
        search_text = " ".join(
            [
                title,
                category,
                summary,
                " ".join(str(item) for item in objects),
                " ".join(str(item) for item in keywords),
                text,
            ]
        )

        docs.append(
            {
                "id": str(meta.get("id") or path.stem),
                "title": title,
                "category": category,
                "file": f"api_docs/{path.name}",
                "summary": summary,
                "objects": objects,
                "keywords": keywords,
                "csharpEntry": csharp_entry,
                "pythonEntry": python_entry,
                "searchText": search_text,
                "content": text,
            }
        )

    return docs


def main() -> None:
    api_docs_dir = Path(__file__).resolve().parent
    docs = build_manifest(api_docs_dir)
    json_text = json.dumps(docs, ensure_ascii=False, separators=(",", ":"))

    (api_docs_dir / "api_manifest.json").write_text(json_text, encoding="utf-8")
    with (api_docs_dir / "api_manifest.js").open("w", encoding="utf-8", newline="\n") as js_file:
        js_file.write(f"window.API_DOCS = {json_text};\n")

    print(f"Generated {len(docs)} API docs.")


if __name__ == "__main__":
    main()
