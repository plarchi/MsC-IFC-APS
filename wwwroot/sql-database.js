const SQL_DATABASE_JS_VERSION = '2026-04-11.1';
console.log('sql-database.js version:', SQL_DATABASE_JS_VERSION);

async function loadSqlDatabasePage() {
  const chartTitle = document.getElementById('chartTitle');
  const chartSummary = document.getElementById('chartSummary');
  const chartImage = document.getElementById('chartImage');
  const tableStatus = document.getElementById('tableStatus');
  const tableContainer = document.getElementById('tableContainer');

  try {
    const response = await fetch('/api/models/sql-database-cobie');
    if (!response.ok) {
      throw new Error(await response.text());
    }

    const payload = await response.json();
    chartTitle.textContent = payload.title || 'IFCAllData-COBie Bubble Chart by Name';
    chartSummary.textContent = payload.summary || 'Saved notebook output loaded.';

    tableContainer.innerHTML = payload.htmlTable || '';
    tableContainer.hidden = !payload.htmlTable;
    tableStatus.hidden = Boolean(payload.htmlTable);
    if (!payload.htmlTable) {
      tableStatus.textContent = 'No notebook table output is available.';
      tableStatus.classList.add('error');
    }

    chartImage.src = '/api/models/sql-database-cobie-chart';
    chartImage.addEventListener('error', () => {
      chartSummary.textContent = 'The notebook chart image could not be loaded.';
    }, { once: true });
  } catch (error) {
    console.error('Failed to load SQL database notebook output:', error);
    chartSummary.textContent = 'Could not load notebook output.';
    tableStatus.textContent = 'Could not load notebook table.';
    tableStatus.classList.add('error');
  }
}

document.addEventListener('DOMContentLoaded', () => {
  loadSqlDatabasePage();
});