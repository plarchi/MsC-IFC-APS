from __future__ import annotations

from datetime import datetime
import importlib.util
from pathlib import Path
import shutil
import sqlite3


ROOT = Path(__file__).resolve().parents[1]
SQL_DIR = ROOT / "SQL"
TARGET_DB = SQL_DIR / "IFCAllData.db"
TEMP_DB = SQL_DIR / "IFCAllData.rebuilt.db"
LOADER_PATH = Path(__file__).with_name("Load-IFC-All-To-SQLite.py")


def load_loader_module():
	if not LOADER_PATH.exists():
		raise FileNotFoundError(f"Loader script not found: {LOADER_PATH}")

	spec = importlib.util.spec_from_file_location("load_ifc_all_to_sqlite", LOADER_PATH)
	if spec is None or spec.loader is None:
		raise RuntimeError(f"Unable to load module spec from {LOADER_PATH}")

	module = importlib.util.module_from_spec(spec)
	spec.loader.exec_module(module)
	return module


def validate_database(db_path: Path) -> None:
	conn = sqlite3.connect(db_path)
	try:
		quick_check = conn.execute("PRAGMA quick_check").fetchone()
		if quick_check != ("ok",):
			raise RuntimeError(f"quick_check failed for {db_path}: {quick_check}")

		row_count = conn.execute("SELECT COUNT(*) FROM IFCAllData").fetchone()[0]
		print(f"Validated {db_path.name}: quick_check=ok, IFCAllData rows={row_count:,}")
	finally:
		conn.close()


def backup_existing_database(target_db: Path) -> Path | None:
	if not target_db.exists():
		return None

	timestamp = datetime.now().strftime("%Y%m%d-%H%M%S")
	backup_path = target_db.with_name(f"{target_db.stem}.backup-{timestamp}{target_db.suffix}")
	shutil.move(str(target_db), str(backup_path))
	print(f"Backed up existing database to: {backup_path}")
	return backup_path


def main() -> None:
	if TEMP_DB.exists():
		TEMP_DB.unlink()

	loader = load_loader_module()
	loader.DB_PATH = TEMP_DB
	loader.main()

	validate_database(TEMP_DB)
	backup_existing_database(TARGET_DB)
	shutil.move(str(TEMP_DB), str(TARGET_DB))
	print(f"Replaced database: {TARGET_DB}")


if __name__ == "__main__":
	main()