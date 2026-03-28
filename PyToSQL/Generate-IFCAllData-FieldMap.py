from pathlib import Path
import json
import re

ROOT = Path(__file__).resolve().parents[1]
JSON_DIR = ROOT / "JSON_Edit"
OUT_PATH = ROOT / "SQL" / "IFCAllData.field_map.json"


def normalize_field_name(name: str) -> str:
    s = name.strip().upper()
    s = re.sub(r"[^A-Z0-9]+", "_", s)
    s = re.sub(r"_+", "_", s).strip("_")
    if not s:
        s = "EMPTY"
    if s[0].isdigit():
        s = "F_" + s
    return s


def collect_pairs(json_paths: list[Path]) -> list[tuple[str, str]]:
    pairs = set()
    for path in json_paths:
        with open(path, "r", encoding="utf-8") as f:
            items = json.load(f)

        for item in items:
            for prop in item.get("Properties", []):
                category = str(prop.get("category", "")).strip()
                display_name = str(prop.get("displayName", "")).strip()
                if category and display_name:
                    pairs.add((category, display_name))

    return sorted(pairs, key=lambda x: (x[0], x[1]))


def build_logical_field_map(pairs: list[tuple[str, str]], priority: list[str]) -> dict[str, list[list[str]]]:
    # Group by normalized display name so Name/NAME fall under one logical key.
    logical_to_pairs: dict[str, set[tuple[str, str]]] = {}
    for category, display_name in pairs:
        logical_name = normalize_field_name(display_name)
        logical_to_pairs.setdefault(logical_name, set()).add((category, display_name))

    priority_rank = {category: i for i, category in enumerate(priority)}

    logical_field_map: dict[str, list[list[str]]] = {}
    for logical_name in sorted(logical_to_pairs.keys()):
        candidates = sorted(
            logical_to_pairs[logical_name],
            key=lambda x: (priority_rank.get(x[0], 999), x[0].upper(), x[1].upper()),
        )
        logical_field_map[logical_name] = [[category, display_name] for category, display_name in candidates]

    return logical_field_map


def main() -> None:
    json_paths = sorted(JSON_DIR.glob("*.json"))
    if not json_paths:
        raise FileNotFoundError(f"No JSON files found in {JSON_DIR}")

    pairs = collect_pairs(json_paths)

    priority = [
        "Instance Part Properties",
        "Item",
        "IFC",
        "ABB4HVDCDesignRevision",
        "ABB4HVDCDesign",
        "ABB4ModelRevision Master",
        "empty property set name",
    ]

    logical_field_map = build_logical_field_map(pairs, priority)

    payload = {
        "source_folder": str(JSON_DIR),
        "json_files": [path.name for path in json_paths],
        "category_count": len({category for category, _ in pairs}),
        "pair_count": len(pairs),
        "logical_field_count": len(logical_field_map),
        "category_priority": priority,
        "logical_field_map": logical_field_map,
    }

    OUT_PATH.write_text(json.dumps(payload, indent=2, ensure_ascii=False), encoding="utf-8")

    print(f"Wrote: {OUT_PATH}")
    print(f"JSON files: {len(json_paths)}")
    print(f"Categories: {payload['category_count']}")
    print(f"Pairs: {payload['pair_count']}")
    print(f"Logical fields: {payload['logical_field_count']}")


if __name__ == "__main__":
    main()
