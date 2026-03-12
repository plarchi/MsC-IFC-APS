"""CountProperties.py

Print a simple TSV table from an APS exported model JSON:

Table: Properties per Object (not grouped)
- Lists each object (including duplicate Names) with:
	- total number of Properties entries
	- number of distinct property categories within the object
- Columns: No, Name, PropertiesCount, CategoryCount

Notes:
- This intentionally does NOT print the nested "Properties" values.
- Output is tab-separated (TSV), easy to copy/paste into Excel.
"""

from __future__ import annotations

import json
from pathlib import Path

DEFAULT_INPUT = (
	Path(__file__).resolve().parent.parent
	/ "JSON Whole Model"
	/ "ASTIDC-STAN-HE-EPD-MVB1-M-E-0002.json"
)


def _safe_properties_count(element: dict) -> int:
	properties = element.get("Properties")
	if isinstance(properties, list):
		return len(properties)
	return 0


def _safe_distinct_category_count(element: dict) -> int:
	properties = element.get("Properties")
	if not isinstance(properties, list):
		return 0

	categories: set[str] = set()
	for item in properties:
		if not isinstance(item, dict):
			continue
		category = item.get("category")
		if category is None:
			continue
		category_text = str(category).strip()
		if not category_text:
			continue
		categories.add(category_text)

	return len(categories)


def main() -> None:
	with DEFAULT_INPUT.open("r", encoding="utf-8") as file_handle:
		data = json.load(file_handle)

	if not isinstance(data, list):
		raise TypeError(
			"Expected the model JSON to be a list of element objects. "
			f"Got: {type(data).__name__}"
		)

	# --- Table: Properties per Object (not grouped) ---
	print("No\tName\tPropertiesCount\tCategoryCount")
	row_number = 0

	for element in data:
		if not isinstance(element, dict):
			continue
		name = element.get("Name")
		if name is None:
			continue

		row_number += 1
		props_count = _safe_properties_count(element)
		category_count = _safe_distinct_category_count(element)
		print(f"{row_number}\t{name}\t{props_count}\t{category_count}")


if __name__ == "__main__":
	main()