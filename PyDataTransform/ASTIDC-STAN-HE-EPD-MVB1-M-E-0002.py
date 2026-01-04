"""PyDataTransform: JSON -> Pandas example

Loads the APS JSON whole-model export and demonstrates a simple flattening
into tabular form using pandas.json_normalize.

Run:
  python PyDataTransform/ASTIDC-STAN-HE-EPD-MVB1-M-E-0002.py

Optional:
  python PyDataTransform/ASTIDC-STAN-HE-EPD-MVB1-M-E-0002.py --input "JSON Whole Model/ASTIDC-STAN-HE-EPD-MVB1-M-E-0002.json"
  python PyDataTransform/ASTIDC-STAN-HE-EPD-MVB1-M-E-0002.py --out "ASTIDC.csv"

Note: Requires pandas (see PyDataTransform/requirements.txt).
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

import pandas as pd


DEFAULT_INPUT = Path("JSON Whole Model") / "ASTIDC-STAN-HE-EPD-MVB1-M-E-0002.json"


def load_json(path: Path) -> Any:
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def pick_records(data: Any) -> list[dict[str, Any]]:
    """Try to find a list of records in common JSON shapes.

    - If the root is a list, treat it as records.
    - If the root is a dict with a list under common keys, use that.
    - Otherwise, wrap the root object as a single record.
    """

    if isinstance(data, list):
        return [r for r in data if isinstance(r, dict)]

    if isinstance(data, dict):
        for key in ("elements", "items", "data", "records", "objects"):
            value = data.get(key)
            if isinstance(value, list):
                return [r for r in value if isinstance(r, dict)]
        return [data]

    return [{"value": data}]


def main() -> int:
    parser = argparse.ArgumentParser(description="Load APS JSON and tabularize with pandas")
    parser.add_argument(
        "--input",
        type=Path,
        default=DEFAULT_INPUT,
        help="Path to the JSON file (relative to repo root or absolute)",
    )
    parser.add_argument(
        "--out",
        type=Path,
        default=None,
        help="Optional output CSV path; if omitted, just prints a preview",
    )
    args = parser.parse_args()

    repo_root = Path(__file__).resolve().parents[1]
    input_path = args.input
    if not input_path.is_absolute():
        input_path = (repo_root / input_path).resolve()

    if not input_path.exists():
        raise FileNotFoundError(f"Input JSON not found: {input_path}")

    data = load_json(input_path)
    records = pick_records(data)

    # Flatten nested objects into columns using dot-notation keys.
    df = pd.json_normalize(records, sep=".")

    print(f"Loaded: {input_path}")
    print(f"Rows: {len(df):,}  Columns: {len(df.columns):,}")
    print(df.head(10).to_string(index=False))

    if args.out is not None:
        out_path = args.out
        if not out_path.is_absolute():
            out_path = (repo_root / out_path).resolve()
        df.to_csv(out_path, index=False, encoding="utf-8")
        print(f"Wrote CSV: {out_path}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
