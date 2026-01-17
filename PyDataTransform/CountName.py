"""CountName.py

Print a simple table (tab-separated) for an APS exported model JSON.

Logic:
1) Load the model JSON file from the "JSON Whole Model" folder.
2) The JSON is expected to be a list of element objects (dictionaries).
3) For each element that has a non-null "Name", print one row with:
	No, Name, DbId, ExternalId

Notes:
- This intentionally does NOT print the nested "Properties" values.
- Output format is TSV (easy to copy/paste into Excel).
"""

from __future__ import annotations

import json
from pathlib import Path

DEFAULT_INPUT = (
	Path(__file__).resolve().parent.parent
	/ "JSON Whole Model"
	/ "ASTIDC-STAN-HE-EPD-MVB1-M-E-0002.json"
)


def main() -> None:
	with DEFAULT_INPUT.open("r", encoding="utf-8") as file_handle:
		data = json.load(file_handle)

	if not isinstance(data, list):
		raise TypeError(
			"Expected the model JSON to be a list of element objects. "
			f"Got: {type(data).__name__}"
		)

	# Print a table of No, Name, DbId, ExternalId for each object.
	print("No\tName\tDbId\tExternalId")
	row_number = 0
	for element in data:
		if not isinstance(element, dict):
			continue
		name = element.get("Name")
		if name is None:
			continue
		db_id = element.get("DbId")
		external_id = element.get("ExternalId")
		row_number += 1
		print(f"{row_number}\t{name}\t{db_id}\t{external_id}")


if __name__ == "__main__":
	main()