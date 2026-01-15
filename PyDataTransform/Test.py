"""Test.py

Simple data check for APS exported model JSON:

1) Reads the model JSON file (ASTIDC-STAN-HE-EPD-MVB1-M-E-0002.json).
2) Uses pandas to normalize/flatten the nested `Properties` arrays into a table.
3) Counts unique vs duplicate property entries for category == "Item" grouped by
   (category, displayName, value).

Output is printed to the console (no files written).
"""

from __future__ import annotations

import json
from pathlib import Path

import pandas as pd


DEFAULT_INPUT = (
	Path(__file__).resolve().parent.parent
	/ "JSON Whole Model"
	/ "ASTIDC-STAN-HE-EPD-MVB1-M-E-0002.json"
)


def main() -> None:
	with DEFAULT_INPUT.open("r", encoding="utf-8") as file_handle:
		data = json.load(file_handle)

	# The file is a list of elements like:
	# {"Name": ..., "DbId": ..., "ExternalId": ..., "Properties": [{"category":..., "displayName":..., "value":...}, ...]}
	df = pd.json_normalize(
		data,
		record_path="Properties",
		meta=["Name", "DbId", "ExternalId"],
		errors="ignore",
	)

	print("Normalized rows:", len(df))
	print(df.head(10).to_string(index=False))

	# Keep it focused: "Item" category only.
	items = df[df["category"] == "Item"].copy()

	# Count occurrences for each (category, displayName, value).
	group_cols = ["category", "displayName", "value"]
	counts = (
		items.groupby(group_cols, dropna=False)
		.size()
		.reset_index(name="count")
		.sort_values(["count", "displayName"], ascending=[False, True])
	)
	counts["occurrence"] = counts["count"].apply(lambda n: "duplicate" if n > 1 else "unique")

	# High-level totals
	unique_groups = int((counts["count"] == 1).sum())
	duplicate_groups = int((counts["count"] > 1).sum())
	duplicate_rows = int(items.duplicated(subset=group_cols, keep=False).sum())

	print("\nItem property summary")
	print("Unique (category, displayName, value) groups:", unique_groups)
	print("Duplicate (category, displayName, value) groups:", duplicate_groups)
	print("Rows participating in duplicates:", duplicate_rows)

	print("\nTop duplicates (count > 1):")
	dupes = counts[counts["count"] > 1]
	if len(dupes) == 0:
		print("(none)")
	else:
		print(dupes.head(50).to_string(index=False))


if __name__ == "__main__":
	main()