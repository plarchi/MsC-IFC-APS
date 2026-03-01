const HOME_JS_VERSION = '2026-03-01.1';
console.log('home.js version:', HOME_JS_VERSION);

function getComparisonElements() {
  return {
    summary: document.getElementById('comparisonSummary'),
    content: document.getElementById('comparisonContent')
  };
}

function renderComparisonMessage(message) {
  const { summary, content } = getComparisonElements();
  if (!content) {
    return;
  }
  if (summary) {
    summary.textContent = '';
  }
  content.textContent = message;
}

function renderComparisonTable(rows) {
  const { summary, content } = getComparisonElements();
  if (!content) {
    return;
  }

  const safeRows = Array.isArray(rows) ? rows : [];
  if (summary) {
    summary.textContent = `Total changed model elements: ${safeRows.length}`;
  }

  if (safeRows.length === 0) {
    content.textContent = 'No Comparison Data Currently.';
    return;
  }

  const table = document.createElement('table');
  table.id = 'comparisonTable';

  const thead = document.createElement('thead');
  const headRow = document.createElement('tr');
  for (const title of ['Existing Model Name', 'Edited Name', 'Property Category', 'Property DisplayName']) {
    const th = document.createElement('th');
    th.textContent = title;
    headRow.appendChild(th);
  }
  thead.appendChild(headRow);
  table.appendChild(thead);

  const tbody = document.createElement('tbody');
  for (const row of safeRows) {
    const tr = document.createElement('tr');

    const existingName = row['Existing Model Name'] ?? row.existingModelName ?? row.ExistingModelName ?? '';
    const editedName = row['Edited Name'] ?? row.editedName ?? row.EditedName ?? '';
    const propertyCategory = row['Property Category'] ?? row.propertyCategory ?? row.PropertyCategory ?? '';
    const propertyDisplayName = row['Property DisplayName'] ?? row.propertyDisplayName ?? row.PropertyDisplayName ?? '';

    for (const value of [existingName, editedName, propertyCategory, propertyDisplayName]) {
      const td = document.createElement('td');
      td.textContent = value;
      tr.appendChild(td);
    }

    tbody.appendChild(tr);
  }
  table.appendChild(tbody);

  content.innerHTML = '';
  content.appendChild(table);
}

async function loadRevisedComparison(fileName) {
  renderComparisonMessage('Loading comparison data...');
  try {
    const resp = await fetch(`/api/models/revised-comparison/${encodeURIComponent(fileName)}`);
    if (resp.status === 404) {
      renderComparisonMessage('No Comparison Data Currently.');
      return;
    }
    if (!resp.ok) {
      throw new Error(await resp.text());
    }
    const rows = await resp.json();
    renderComparisonTable(rows);
  } catch (err) {
    console.error('Failed to load comparison data:', err);
    renderComparisonMessage('No Comparison Data Currently.');
  }
}

async function deleteModel(objectKey) {
  const resp = await fetch(`/api/models/${encodeURIComponent(objectKey)}`, {
    method: 'DELETE'
  });
  if (!resp.ok) {
    throw new Error(await resp.text());
  }
}

function downloadModel(objectKey) {
  // Trigger a browser file download by navigating to the download endpoint.
  window.location.href = `/api/models/download/${encodeURIComponent(objectKey)}`;
}

async function deleteRevisedModel(fileName) {
  const resp = await fetch(`/api/models/revised/${encodeURIComponent(fileName)}`, {
    method: 'DELETE'
  });
  if (!resp.ok) {
    throw new Error(await resp.text());
  }
}

function downloadRevisedModel(fileName) {
  window.location.href = `/api/models/revised/download/${encodeURIComponent(fileName)}`;
}

async function loadModels() {
  const list = document.getElementById('modelList');
  list.innerHTML = '';
  try {
    const resp = await fetch('/api/models');
    if (!resp.ok) throw new Error(await resp.text());
    const models = await resp.json();
    if (!Array.isArray(models) || models.length === 0) {
      list.innerHTML = '<li>No models found</li>';
      return;
    }
    // Render as ordered list with numbered items
    for (const model of models) {
      const li = document.createElement('li');
      const a = document.createElement('a');
      a.textContent = model.name;
      // Link to the 3D viewer page with URN in the hash
      a.href = `/index.html#${encodeURIComponent(model.urn)}`;

      const actions = document.createElement('div');
      actions.className = 'row-actions';

      const dl = document.createElement('button');
      dl.type = 'button';
      dl.textContent = 'Download';
      dl.className = 'download-btn';
      dl.addEventListener('click', (evt) => {
        evt.preventDefault();
        downloadModel(model.name);
      });

      const del = document.createElement('button');
      del.type = 'button';
      del.textContent = 'Delete';
      del.className = 'delete-btn';
      del.addEventListener('click', async (evt) => {
        evt.preventDefault();
        del.setAttribute('disabled', 'true');
        try {
          await deleteModel(model.name);
          await loadModels();
        } catch (err) {
          console.error('Failed to delete model:', err);
          alert('Could not delete model. See the console for more details.');
        } finally {
          del.removeAttribute('disabled');
        }
      });
      li.appendChild(a);
      actions.appendChild(dl);
      actions.appendChild(del);
      li.appendChild(actions);
      list.appendChild(li);
    }
  } catch (err) {
    console.error('Failed to load models:', err);
    list.innerHTML = '<li>Error loading models</li>';
  }
}

async function loadRevisedModels() {
  const list = document.getElementById('revisedModelList');
  if (!list) {
    return;
  }

  list.innerHTML = '';
  try {
    const resp = await fetch('/api/models/revised-models');
    if (!resp.ok) throw new Error(await resp.text());

    const models = await resp.json();
    if (!Array.isArray(models) || models.length === 0) {
      list.innerHTML = '<li>No revised IFC models found</li>';
      return;
    }

    for (const model of models) {
      const li = document.createElement('li');
      const a = document.createElement('a');
      a.textContent = model.name;
      a.href = '#';
      a.addEventListener('click', async (evt) => {
        evt.preventDefault();
        await loadRevisedComparison(model.name);
      });

      const actions = document.createElement('div');
      actions.className = 'row-actions';

      const dl = document.createElement('button');
      dl.type = 'button';
      dl.textContent = 'Download';
      dl.className = 'download-btn';
      dl.addEventListener('click', (evt) => {
        evt.preventDefault();
        downloadRevisedModel(model.name);
      });

      const del = document.createElement('button');
      del.type = 'button';
      del.textContent = 'Delete';
      del.className = 'delete-btn';
      del.addEventListener('click', async (evt) => {
        evt.preventDefault();
        del.setAttribute('disabled', 'true');
        try {
          await deleteRevisedModel(model.name);
          await loadRevisedModels();
        } catch (err) {
          console.error('Failed to delete revised IFC model:', err);
          alert('Could not delete revised IFC model. See the console for more details.');
        } finally {
          del.removeAttribute('disabled');
        }
      });

      li.appendChild(a);
      actions.appendChild(dl);
      actions.appendChild(del);
      li.appendChild(actions);
      list.appendChild(li);
    }
  } catch (err) {
    console.error('Failed to load revised IFC models:', err);
    list.innerHTML = '<li>Error loading revised IFC models</li>';
  }
}

document.addEventListener('DOMContentLoaded', async () => {
  renderComparisonMessage('Select a revised model to view comparison data.');
  await loadModels();
  await loadRevisedModels();
});
