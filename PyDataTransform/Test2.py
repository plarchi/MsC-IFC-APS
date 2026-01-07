import json
import pandas as pd
from pathlib import Path

json_path = (
    Path(__file__).resolve().parent.parent
    / "JSON Export"
    / "A_B.json"
)

with open(json_path, "r", encoding="utf-8") as f:
    data = json.load(f)

# 1) "Normalized" = a flat table (each JSON item becomes a row)
df = pd.json_normalize(data)
print("Normalized table (first 10 rows):")
print(df.head(10).to_string(index=False))

# 2) If your JSON is a list of properties like:
#    {"category": "...", "displayName": "...", "value": "..."}
#    you can turn it into a one-row table with many columns.
if {"category", "displayName", "value"}.issubset(df.columns):
    wide = (
        df.assign(column=df["category"].astype(str) + "." + df["displayName"].astype(str))
        .drop(columns=["category", "displayName"])
        .set_index("column")["value"]
        .to_frame()
        .T
    )

    print("\nWide table (1 row, many columns):")
    print(f"Rows: {wide.shape[0]}, Columns: {wide.shape[1]}")

    preview_cols = min(20, wide.shape[1])
    print(f"First {preview_cols} column names:")
    print(list(wide.columns[:preview_cols]))

    print(f"\nFirst {preview_cols} values (wide row 0):")
    print(wide.iloc[0, :preview_cols].to_string())
