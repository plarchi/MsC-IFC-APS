const HOME_JS_VERSION = '2026-03-26.2';
console.log('home.js version:', HOME_JS_VERSION);

const NOTEBOOK_FLOW_MODELS = new Set([
  'ifc2x3_duplex_architecture',
  'ifc4_samplehouse',
  'snowdon+towers+sample+structural2x3'
]);

let activeComparisonRequestId = 0;
const LEFT_PANEL_WIDTH_KEY = 'home.leftPanelWidthPercent';

function initResizableSplit() {
  const container = document.getElementById('container');
  const left = document.getElementById('left');
  const right = document.getElementById('right');
  const splitter = document.getElementById('splitter');

  if (!container || !left || !right || !splitter) {
    return;
  }

  const minPercent = 20;
  const maxPercent = 80;

  const applyLeftPercent = (percent) => {
    const clamped = Math.max(minPercent, Math.min(maxPercent, percent));
    left.style.flexBasis = `${clamped}%`;
    right.style.flexBasis = `${100 - clamped}%`;
  };

  const savedPercent = Number(localStorage.getItem(LEFT_PANEL_WIDTH_KEY));
  if (Number.isFinite(savedPercent)) {
    applyLeftPercent(savedPercent);
  }

  let isDragging = false;

  const onPointerMove = (event) => {
    if (!isDragging) {
      return;
    }

    const bounds = container.getBoundingClientRect();
    const relativeX = event.clientX - bounds.left;
    const percent = (relativeX / bounds.width) * 100;
    applyLeftPercent(percent);
  };

  const stopDragging = () => {
    if (!isDragging) {
      return;
    }
    isDragging = false;
    splitter.classList.remove('is-dragging');
    document.body.style.cursor = '';
    document.body.style.userSelect = '';

    const currentPercent = parseFloat(left.style.flexBasis);
    if (Number.isFinite(currentPercent)) {
      localStorage.setItem(LEFT_PANEL_WIDTH_KEY, String(currentPercent));
    }

    window.removeEventListener('pointermove', onPointerMove);
    window.removeEventListener('pointerup', stopDragging);
    window.removeEventListener('pointercancel', stopDragging);
  };

  splitter.addEventListener('pointerdown', (event) => {
    event.preventDefault();
    isDragging = true;
    splitter.classList.add('is-dragging');
    document.body.style.cursor = 'col-resize';
    document.body.style.userSelect = 'none';

    window.addEventListener('pointermove', onPointerMove);
    window.addEventListener('pointerup', stopDragging);
    window.addEventListener('pointercancel', stopDragging);
  });
}

function getComparisonElements() {
  return {
    title: document.getElementById('comparisonTitle'),
    summary: document.getElementById('comparisonSummary'),
    content: document.getElementById('comparisonContent')
  };
}

function getModelBaseName(fileName) {
  const safeName = String(fileName ?? '').trim();
  if (!safeName) {
    return '';
  }
  return safeName.toLowerCase().endsWith('.ifc') ? safeName.slice(0, -4) : safeName;
}

function usesNotebookCobieFlow(fileName) {
  return NOTEBOOK_FLOW_MODELS.has(getModelBaseName(fileName).toLowerCase());
}

function createRevealSection(animationDelayMs = 0) {
  const section = document.createElement('section');
  section.style.opacity = '0';
  section.style.transform = 'translateY(8px)';
  section.style.transition = 'opacity 220ms ease, transform 220ms ease';
  section.style.transitionDelay = `${animationDelayMs}ms`;

  requestAnimationFrame(() => {
    section.style.opacity = '1';
    section.style.transform = 'translateY(0)';
  });

  return section;
}

function appendChartSection({
  fileName,
  container,
  title,
  endpoint,
  alt,
  animationDelayMs,
  missingMessage
}) {
  if (!fileName || !container) {
    return;
  }

  const chartSection = createRevealSection(animationDelayMs);
  chartSection.style.margin = '0 0 16px 0';

  const chartTitle = document.createElement('div');
  chartTitle.textContent = title;
  chartTitle.style.fontWeight = '700';
  chartTitle.style.fontSize = '18px';
  chartTitle.style.margin = '0 0 10px 0';
  chartSection.appendChild(chartTitle);

  const img = document.createElement('img');
  img.alt = alt;
  img.loading = 'lazy';
  img.style.display = 'block';
  img.style.maxWidth = '100%';
  img.style.height = 'auto';
  img.style.opacity = '0';
  img.style.transition = 'opacity 220ms ease';

  const loading = document.createElement('div');
  loading.textContent = 'Loading chart...';
  loading.style.color = '#666';
  loading.style.fontSize = '13px';
  loading.style.margin = '4px 0 10px 0';

  img.addEventListener('load', () => {
    loading.remove();
    img.style.opacity = '1';
  });

  img.addEventListener('error', () => {
    loading.textContent = missingMessage || 'Could not load chart.';
    loading.style.color = '#a33';
  });

  img.src = `${endpoint}/${encodeURIComponent(fileName)}`;

  chartSection.appendChild(loading);
  chartSection.appendChild(img);
  container.appendChild(chartSection);
}

function renderComparisonLoading(message) {
  const { title, summary, content } = getComparisonElements();
  if (title) {
    title.textContent = 'Comparison Table';
  }
  if (!summary) {
    return;
  }
  summary.textContent = message;
  if (content && !content.innerHTML.trim()) {
    content.textContent = 'Loading...';
  }
}

function renderComparisonMessage(message) {
  const { title, summary, content } = getComparisonElements();
  if (!content) {
    return;
  }
  if (title) {
    title.textContent = 'Comparison Table';
  }
  if (summary) {
    summary.textContent = '';
  }
  content.textContent = message;
}

function renderComparisonTable(rows, fileName) {
  const { title, summary, content } = getComparisonElements();
  if (!content) {
    return;
  }

  if (title) {
    title.textContent = 'Comparison Table';
  }

  const safeRows = Array.isArray(rows) ? rows : [];
  const totalChangedModelElements = safeRows.reduce((sum, row) => {
    const countValue = Number(row['Count'] ?? row.count ?? row.Count ?? 0);
    return sum + (Number.isFinite(countValue) ? countValue : 0);
  }, 0);

  if (summary) {
    summary.textContent = `Total changed model elements: ${totalChangedModelElements}`;
  }

  if (safeRows.length === 0) {
    content.textContent = 'No Comparison Data Currently.';
    return;
  }

  const getRowValue = (row, keys) => {
    for (const key of keys) {
      if (row[key] !== undefined && row[key] !== null) {
        return row[key];
      }
    }
    return '';
  };

  const truncateExistingNamesForA = (existingName, editedName, maxLines = 10) => {
    if (String(editedName ?? '').trim() !== 'A') {
      return String(existingName ?? '');
    }

    const lines = String(existingName ?? '')
      .split(/\r?\n|<br\s*\/?\s*>/gi)
      .map((line) => line.trim())
      .filter((line) => line.length > 0);

    if (lines.length <= maxLines) {
      return String(existingName ?? '');
    }

    const remaining = lines.length - maxLines;
    return [...lines.slice(0, maxLines), `... (${remaining} more)`].join('\n');
  };

  const normalizedRows = safeRows.map((row) => {
    const editedName = getRowValue(row, ['Edited Name', 'editedName', 'EditedName']);
    const existingName = getRowValue(row, ['Existing Model Name', 'existingModelName', 'ExistingModelName']);

    return {
      existingName: truncateExistingNamesForA(existingName, editedName, 10),
      editedName,
      propertyCategory: getRowValue(row, ['Property Category', 'propertyCategory', 'PropertyCategory']),
      propertyDisplayName: getRowValue(row, ['Property DisplayName', 'propertyDisplayName', 'PropertyDisplayName']),
      count: getRowValue(row, ['Count', 'count'])
    };
  });

  const maxRowsToShow = 30;
  let displayRows = normalizedRows;
  if (normalizedRows.length > maxRowsToShow) {
    displayRows = [
      ...normalizedRows.slice(0, maxRowsToShow),
      {
        existingName: '...',
        editedName: '...',
        propertyCategory: '...',
        propertyDisplayName: '...',
        count: '...'
      },
      normalizedRows[normalizedRows.length - 1]
    ];
  }

  content.innerHTML = '';

  appendChartSection({
    fileName,
    container: content,
    title: 'Hierarchical Bubble Graph',
    endpoint: '/api/models/revised-hierarchical-bubble-chart',
    alt: 'Hierarchical bubble chart',
    animationDelayMs: 0,
    missingMessage: 'No pre-generated Hierarchical Bubble PNG found.'
  });

  appendChartSection({
    fileName,
    container: content,
    title: 'Edited Model Name - Nested Pie Chart',
    endpoint: '/api/models/revised-comparison-chart',
    alt: 'Nested pie chart',
    animationDelayMs: 80,
    missingMessage: 'No pre-generated Nested Pie PNG found.'
  });

  const tableSection = createRevealSection(140);
  tableSection.style.margin = '0 0 8px 0';

  const tableTitle = document.createElement('div');
  tableTitle.textContent = 'Changed IFC Names Comparison';
  tableTitle.style.fontWeight = '700';
  tableTitle.style.fontSize = '16px';
  tableTitle.style.margin = '8px 0 10px 0';
  tableSection.appendChild(tableTitle);

  const table = document.createElement('table');
  table.id = 'comparisonTable';

  const thead = document.createElement('thead');
  const headRow = document.createElement('tr');
  for (const title of ['Existing Model Name', 'Edited Name', 'Property Category', 'Property DisplayName', 'Count']) {
    const th = document.createElement('th');
    th.textContent = title;
    headRow.appendChild(th);
  }
  thead.appendChild(headRow);
  table.appendChild(thead);

  const tbody = document.createElement('tbody');
  for (const row of displayRows) {
    const tr = document.createElement('tr');

    for (const [index, value] of [
      row.existingName,
      row.editedName,
      row.propertyCategory,
      row.propertyDisplayName,
      row.count
    ].entries()) {
      const td = document.createElement('td');
      td.textContent = value;
      if (index === 0) {
        td.style.whiteSpace = 'pre-line';
      }
      tr.appendChild(td);
    }

    tbody.appendChild(tr);
  }
  table.appendChild(tbody);

  tableSection.appendChild(table);
  content.appendChild(tableSection);
}

function renderCobieImplementationTable(rows, fileName) {
  const { title, summary, content } = getComparisonElements();
  if (!content) {
    return;
  }

  if (title) {
    title.textContent = 'COBie Data implementation';
  }

  const safeRows = Array.isArray(rows) ? rows : [];
  const dataRows = safeRows.filter((row) => {
    const name = String(row.name ?? row.Name ?? '').trim();
    return name.length > 0 && name !== '---';
  });

  if (summary) {
    summary.textContent = `Changed rows shown in base data: ${dataRows.length}`;
  }

  if (safeRows.length === 0) {
    content.textContent = 'No Comparison Data Currently.';
    return;
  }

  content.innerHTML = '';

  appendChartSection({
    fileName,
    container: content,
    title: 'IFC Type Bubble Diagram (COBie Coverage)',
    endpoint: '/api/models/revised-hierarchical-bubble-chart',
    alt: 'IFC type bubble chart',
    animationDelayMs: 0,
    missingMessage: 'No pre-generated bubble PNG found.'
  });

  appendChartSection({
    fileName,
    container: content,
    title: 'COBie Item Type - Nested Pie Chart',
    endpoint: '/api/models/revised-comparison-chart',
    alt: 'COBie nested pie chart',
    animationDelayMs: 80,
    missingMessage: 'No pre-generated nested pie PNG found.'
  });

  const tableSection = createRevealSection(140);
  tableSection.style.margin = '0 0 8px 0';

  const tableTitle = document.createElement('div');
  tableTitle.textContent = 'COBie Data implementation';
  tableTitle.style.fontWeight = '700';
  tableTitle.style.fontSize = '16px';
  tableTitle.style.margin = '8px 0 10px 0';
  tableSection.appendChild(tableTitle);

  const table = document.createElement('table');
  table.id = 'comparisonTable';

  const thead = document.createElement('thead');
  const headRow = document.createElement('tr');
  for (const headerText of ['Item Type', 'IFC Type', 'Total Items count', 'Name', 'COBie Value']) {
    const th = document.createElement('th');
    th.textContent = headerText;
    headRow.appendChild(th);
  }
  thead.appendChild(headRow);
  table.appendChild(thead);

  const tbody = document.createElement('tbody');
  for (const row of safeRows) {
    const tr = document.createElement('tr');
    const cells = [
      row.itemType ?? row.ItemType ?? '',
      row.ifcType ?? row.IfcType ?? '',
      row.totalItemsCount ?? row.TotalItemsCount ?? '',
      row.name ?? row.Name ?? '',
      row.cobieValue ?? row.CobieValue ?? ''
    ];

    for (const value of cells) {
      const td = document.createElement('td');
      td.textContent = value;
      tr.appendChild(td);
    }

    tbody.appendChild(tr);
  }

  table.appendChild(tbody);
  tableSection.appendChild(table);
  content.appendChild(tableSection);
}

async function loadRevisedComparison(fileName) {
  const requestId = ++activeComparisonRequestId;
  renderComparisonLoading('Loading comparison data...');
  try {
    const useNotebookFlow = usesNotebookCobieFlow(fileName);
    const endpoint = useNotebookFlow
      ? '/api/models/revised-cobie-implementation/'
      : '/api/models/revised-comparison/';

    const resp = await fetch(`${endpoint}${encodeURIComponent(fileName)}`);
    if (requestId !== activeComparisonRequestId) {
      return;
    }
    if (resp.status === 404) {
      renderComparisonMessage('No Comparison Data Currently.');
      return;
    }
    if (!resp.ok) {
      throw new Error(await resp.text());
    }
    const rows = await resp.json();
    if (requestId !== activeComparisonRequestId) {
      return;
    }
    if (useNotebookFlow) {
      renderCobieImplementationTable(rows, fileName);
    } else {
      renderComparisonTable(rows, fileName);
    }
  } catch (err) {
    if (requestId !== activeComparisonRequestId) {
      return;
    }
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
  initResizableSplit();
  renderComparisonMessage('Select a revised model to view comparison data.');
  await loadModels();
  await loadRevisedModels();
});
