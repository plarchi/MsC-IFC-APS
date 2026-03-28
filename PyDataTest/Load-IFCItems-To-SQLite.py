from pathlib import Path
import json
import sqlite3

ROOT = Path(__file__).resolve().parents[1]
JSON_DIR = ROOT / "JSON Whole Model"
SQL_DIR = ROOT / "SQL"
DB_PATH = SQL_DIR / "IFCData.db"
SQL_FILE = SQL_DIR / "IFCData.sql"

JSON_FILES = [
    "ASTIDC-STAN-HE-EPD-MVB1-M-E-0002.json",
    "ASTIDC-STAN-HE-MPD-RHP1-M-M-0001.json",
    "ASTIDC-STAN-HE-MPD-TXBP1-M-M-0001.json",
]

# DB column -> (category, displayName)
FIELD_MAP = {
    "SOURCE_FILE": ("Item", "Source File"),
    "NAME": ("Item", "Name"),
    "TYPE": ("Item", "Type"),
    "UNIT": ("Item", "Unit"),
    "GUID": ("Item", "GUID"),
    "GLOBAL_ID": ("IFC", "GlobalId"),
    "OBJECT_TYPE": ("IFC", "ObjectType"),
    "MATERIAL": ("Item", "Material"),
    "NX_AREA": ("Materials", "NX_Area"),
    "NX_VOLUME": ("Materials", "NX_Volume"),
    "NX_VOLUME_SOURCE": ("Materials", "NX_VolumeSource"),
    "NX_WEIGHT": ("Materials", "NX_Weight"),
    "NX_WEIGHT_SOURCE": ("Materials", "NX_WeightSource"),
}

CREATE_SQL = """
CREATE TABLE IF NOT EXISTS IFCItems (
    ID               INTEGER PRIMARY KEY AUTOINCREMENT,
    SOURCE_FILE      TEXT,
    NAME             TEXT,
    TYPE             TEXT,
    UNIT             TEXT,
    GUID             TEXT,
    GLOBAL_ID        TEXT,
    OBJECT_TYPE      TEXT,
    MATERIAL         TEXT,
    NX_AREA          TEXT,
    NX_VOLUME        TEXT,
    NX_VOLUME_SOURCE TEXT,
    NX_WEIGHT        TEXT,
    NX_WEIGHT_SOURCE TEXT
)
"""


def build_rows(items: list[dict]) -> list[tuple]:
    rows = []
    for item in items:
        prop_lookup = {}
        for prop in item.get("Properties", []):
            category = str(prop.get("category", "")).strip()
            display_name = str(prop.get("displayName", "")).strip()
            prop_lookup[(category, display_name)] = prop.get("value", None)

        row = tuple(prop_lookup.get((cat, name), None) for cat, name in FIELD_MAP.values())
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
    cur.execute("DELETE FROM IFCItems")
    conn.commit()

    col_names = ", ".join(FIELD_MAP.keys())
    placeholders = ", ".join("?" for _ in FIELD_MAP)
    insert_sql = f"INSERT INTO IFCItems ({col_names}) VALUES ({placeholders})"

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
    cur.execute("SELECT COUNT(*) FROM IFCItems")
    row_count = cur.fetchone()[0]
    print(f"Verification row count in IFCItems: {row_count:,}")

    cur.execute("SELECT SOURCE_FILE, NAME, TYPE, GLOBAL_ID, NX_AREA FROM IFCItems LIMIT 5")
    sample_rows = cur.fetchall()
    print("Sample rows:")
    for row in sample_rows:
        print(row)

    conn.close()


if __name__ == "__main__":
    main()
