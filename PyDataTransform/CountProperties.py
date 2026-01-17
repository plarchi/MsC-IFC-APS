"""CountProperties.py

Print two simple TSV tables from an APS exported model JSON:

Table 1 (Objects):
- Lists each element (object) that has a non-null "Name".
- Columns: No, Name, DbId, ExternalId

Table 2 (Properties per Object):
- Lists each object (including duplicate Names) with the number of Properties
	entries it has.
- Columns: No, Name, PropertiesCount

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


def main() -> None:
	with DEFAULT_INPUT.open("r", encoding="utf-8") as file_handle:
		data = json.load(file_handle)

	if not isinstance(data, list):
		raise TypeError(
			"Expected the model JSON to be a list of element objects. "
			f"Got: {type(data).__name__}"
		)

	# --- Table 1: Objects ---
	print("No\tName\tDbId\tExternalId")
	row_number = 0

	# Keep per-object properties counts for Table 2.
	per_object_props: list[tuple[str, int]] = []

	for element in data:
		if not isinstance(element, dict):
			continue
		name = element.get("Name")
		if name is None:
			continue

		row_number += 1
		db_id = element.get("DbId")
		external_id = element.get("ExternalId")
		print(f"{row_number}\t{name}\t{db_id}\t{external_id}")

		props_count = _safe_properties_count(element)
		per_object_props.append((name, props_count))

	# --- Table 2: Properties per Object (not grouped) ---
	print("\nNo\tName\tPropertiesCount")
	for index, (name, props_count) in enumerate(per_object_props, start=1):
		print(f"{index}\t{name}\t{props_count}")


if __name__ == "__main__":
	main()