from pathlib import Path
import json
import sqlite3

ROOT = Path(__file__).resolve().parents[1]
JSON_DIR = ROOT / "JSON_Edit"
SQL_DIR = ROOT / "SQL"
DB_PATH = SQL_DIR / "IFCAssetData.db"
SQL_FILE = SQL_DIR / "IFCAssetData.sql"

JSON_FILES = [
	"ASTIDC-STAN-HE-EPD-MVB1-M-E-0002.json",
	"ASTIDC-STAN-HE-MPD-RHP1-M-M-0001.json",
	"ASTIDC-STAN-HE-MPD-TXBP1-M-M-0001.json",
]

# DB column -> prioritized list of (category, displayName)
FIELD_MAP = {
	"NAME": [
		("Item", "Name"),
	],
	"GLOBALID": [
		("IFC", "GlobalId"),
	],
	"GUID": [
		("Item", "GUID"),
	],
	"COBIE": [
		("Instance Part Properties", "COBie"),
		("Item", "COBie"),
		("IFC", "COBie"),
	],
	"ABB_CLASSIFICATION_Y": [
		("Instance Part Properties", "ABB_CLASSIFICATION_Y"),
		("ABB4HVDCDesignRevision", "ABB_CLASSIFICATION_Y"),
	],
	"ABB_NOTE": [
		("Instance Part Properties", "ABB_NOTE"),
		("ABB4HVDCDesignRevision", "ABB_NOTE"),
	],
	"ABB_POSITION_NO": [
		("Instance Part Properties", "ABB_POSITION_NO"),
		("empty property set name", "ABB_POSITION_NO"),
	],
	"ABB_SUPPLIER": [
		("Instance Part Properties", "ABB_SUPPLIER"),
		("ABB4HVDCDesignRevision", "ABB_SUPPLIER"),
	],
	"ABB_UNIT": [
		("Instance Part Properties", "ABB_UNIT"),
		("ABB4ModelRevision Master", "ABB_UNIT"),
	],
	"DB_PART_DESC": [
		("Instance Part Properties", "DB_PART_DESC"),
		("ABB4HVDCDesign", "DB_PART_DESC"),
	],
	"DB_PART_NO": [
		("Instance Part Properties", "DB_PART_NO"),
		("ABB4HVDCDesign", "DB_PART_NO"),
	],
	"DB_PART_REV": [
		("Instance Part Properties", "DB_PART_REV"),
		("ABB4HVDCDesignRevision", "DB_PART_REV"),
	],
	"DB_PART_TYPE": [
		("Instance Part Properties", "DB_PART_TYPE"),
		("ABB4HVDCDesign", "DB_PART_TYPE"),
	],
	"DISCIPLINE": [
		("Instance Part Properties", "DISCIPLINE"),
	],
}

CREATE_SQL = """
CREATE TABLE IF NOT EXISTS IFCAssetItems (
	ID                   INTEGER PRIMARY KEY AUTOINCREMENT,
	NAME                 TEXT,
	GLOBALID             TEXT,
	GUID                 TEXT,
	COBIE                TEXT,
	ABB_CLASSIFICATION_Y TEXT,
	ABB_NOTE             TEXT,
	ABB_POSITION_NO      TEXT,
	ABB_SUPPLIER         TEXT,
	ABB_UNIT             TEXT,
	DB_PART_DESC         TEXT,
	DB_PART_NO           TEXT,
	DB_PART_REV          TEXT,
	DB_PART_TYPE         TEXT,
	DISCIPLINE           TEXT
)
"""


def get_first_value(prop_lookup: dict, candidates: list[tuple[str, str]]):
	for category, display_name in candidates:
		value = prop_lookup.get((category, display_name), None)
		if value is not None and str(value).strip() != "":
			return value
	return None


def build_rows(items: list[dict]) -> list[tuple]:
	rows = []
	for item in items:
		prop_lookup = {}
		for prop in item.get("Properties", []):
			category = str(prop.get("category", "")).strip()
			display_name = str(prop.get("displayName", "")).strip()
			prop_lookup[(category, display_name)] = prop.get("value", None)

		row = tuple(get_first_value(prop_lookup, candidates) for candidates in FIELD_MAP.values())
		rows.append(row)
	return rows


def main() -> None:
	json_paths = [JSON_DIR / name for name in JSON_FILES]

	conn = sqlite3.connect(DB_PATH)
	cur = conn.cursor()

	# Ensure target table exists.
	cur.execute(CREATE_SQL)
	conn.commit()

	# Clean reload so reruns do not duplicate data.
	cur.execute("DELETE FROM IFCAssetItems")
	conn.commit()

	col_names = ", ".join(FIELD_MAP.keys())
	placeholders = ", ".join("?" for _ in FIELD_MAP)
	insert_sql = f"INSERT INTO IFCAssetItems ({col_names}) VALUES ({placeholders})"

	total_inserted = 0
	for path in json_paths:
		if not path.exists():
			print(f"<file not found: {path.name}>")
			continue

		with open(path, "r", encoding="utf-8") as f:
			items = json.load(f)

		rows = build_rows(items)
		cur.executemany(insert_sql, rows)
		conn.commit()

		total_inserted += len(rows)
		print(f"Inserted {len(rows):,} rows from {path.name}")

	print(f"Total rows inserted: {total_inserted:,}")

	# Run schema SQL file as requested.
	if SQL_FILE.exists():
		sql_text = SQL_FILE.read_text(encoding="utf-8")
		if sql_text.strip():
			cur.executescript(sql_text)
			conn.commit()
			print(f"Ran SQL file: {SQL_FILE.name}")
		else:
			print(f"SQL file is empty: {SQL_FILE.name}")
	else:
		print(f"SQL file not found: {SQL_FILE}")

	# Verification checks.
	cur.execute("SELECT COUNT(*) FROM IFCAssetItems")
	row_count = cur.fetchone()[0]
	print(f"Verification row count in IFCAssetItems: {row_count:,}")

	cur.execute("SELECT NAME, GLOBALID, GUID, DB_PART_NO, DISCIPLINE FROM IFCAssetItems LIMIT 5")
	sample_rows = cur.fetchall()
	print("Sample rows:")
	for row in sample_rows:
		print(row)

	conn.close()


if __name__ == "__main__":
	main()
