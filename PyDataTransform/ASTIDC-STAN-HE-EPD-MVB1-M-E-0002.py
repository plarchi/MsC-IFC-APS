from __future__ import annotations

import json
from pathlib import Path
from typing import Any

DEFAULT_INPUT = Path("JSON Whole Model") / "ASTIDC-STAN-HE-EPD-MVB1-M-E-0002.json"


def load_json(path: Path) -> Any:
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def collect_categories(data: Any, categories: set[str]) -> None:
    if isinstance(data, dict):
        category_value = data.get("category")
        if isinstance(category_value, str) and category_value.strip():
            categories.add(category_value)
        for value in data.values():
            collect_categories(value, categories)
        return

    if isinstance(data, list):
        for item in data:
            collect_categories(item, categories)


def main() -> int:
    repo_root = Path(__file__).resolve().parents[1]
    input_path = (repo_root / DEFAULT_INPUT).resolve()
    if not input_path.exists():
        raise FileNotFoundError(f"Input JSON not found: {input_path}")

    data = load_json(input_path)
    categories: set[str] = set()
    collect_categories(data, categories)

    print(f"Loaded: {input_path}")
    print(f"Total categories (unique): {len(categories)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
