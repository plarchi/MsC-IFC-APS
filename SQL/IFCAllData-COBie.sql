-- Build a COBie-focused table from IFCAllData with selected source files only.
DROP TABLE IF EXISTS "IFCAllData-COBie";

CREATE TABLE "IFCAllData-COBie" (
	SOURCE_FILE TEXT,
	NAME TEXT,
	GLOBALID TEXT,
	COBie TEXT
);

INSERT INTO "IFCAllData-COBie" (SOURCE_FILE, NAME, GLOBALID, COBie)
SELECT
	SOURCE_FILE,
	OBJECTTYPE AS NAME,
	GLOBALID,
	COBie
FROM IFCAllData
WHERE SOURCE_FILE IN (
	'ACD-18040-ALL-ST-N2x3.json',
	'Ifc2x3_Duplex_Architecture.json',
	'Ifc4_SampleHouse.json',
	'Snowdon+Towers+Sample+Structural2x3.json'
);

-- Summary count (first result set).
SELECT
	COUNT(*) AS TOTAL_ROWS,
	SUM(CASE WHEN COBie IS NOT NULL AND TRIM(COBie) <> '' THEN 1 ELSE 0 END) AS COBIE_DATA_COUNT
FROM "IFCAllData-COBie";

-- Detailed rows (second result set).
SELECT SOURCE_FILE, NAME, GLOBALID, COBie
FROM "IFCAllData-COBie"
ORDER BY SOURCE_FILE, NAME;
