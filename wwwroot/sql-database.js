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

    const rows = Array.isArray(payload.rows) ? payload.rows : [];
    renderSqlDatabaseTable(tableContainer, rows);
    tableContainer.hidden = rows.length === 0;
    tableStatus.hidden = rows.length > 0;
    if (rows.length === 0) {
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

function renderSqlDatabaseTable(container, rows) {
  container.innerHTML = '';

  const table = document.createElement('table');
  table.className = 'dataframe';

  const thead = document.createElement('thead');
  const headRow = document.createElement('tr');
  for (const header of ['Name', 'Row Count', 'COBie Count']) {
    const th = document.createElement('th');
    th.textContent = header;
    headRow.appendChild(th);
  }
  thead.appendChild(headRow);
  table.appendChild(thead);

  const tbody = document.createElement('tbody');
  for (const row of rows) {
    const tr = document.createElement('tr');

    for (const value of [row.name, row.rowCount, row.cobieCount]) {
      const td = document.createElement('td');
      td.textContent = value;
      tr.appendChild(td);
    }

    tbody.appendChild(tr);
  }

  table.appendChild(tbody);
  container.appendChild(table);
}

document.addEventListener('DOMContentLoaded', () => {
  loadSqlDatabasePage();
});