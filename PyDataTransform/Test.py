from __future__ import annotations
import json
from pathlib import Path

DEFAULT_INPUT = (
	Path(__file__).resolve().parent.parent
	/ "JSON Whole Model"
	/ "ASTIDC-STAN-HE-EPD-MVB1-M-E-0002.json"
)

# Read the JSON file into a Python variable
with DEFAULT_INPUT.open("r", encoding="utf-8") as file_handle:
	data = json.load(file_handle)

# Print it (note: for large models, this can be a lot of text)
# print(data)

data['test'] = True

new_json = json.dumps(data, indent=4, sort_keys=True)
print(new_json)