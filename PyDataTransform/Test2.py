import json
import pandas as pd
from pathlib import Path

json_path = (
    Path(__file__).resolve().parent.parent
    / "JSON Whole Model"
    / "ASTIDC-STAN-HE-EPD-MVB1-M-E-0002.json"
)

with open(json_path, "r", encoding="utf-8") as f:
    data = json.load(f)

# Start simple: "normalize" JSON into a flat table
# (each JSON item becomes a row; nested keys become columns).
df = pd.json_normalize(data)

# Print a quick peek at what normalization produced.
# (If you want the full table, replace head(10) with just df.)
print(df.head(10).to_string(index=False))
