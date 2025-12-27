const HOME_JS_VERSION = '2025-12-27.1';
console.log('home.js version:', HOME_JS_VERSION);

async function deleteModel(objectKey) {
  const resp = await fetch(`/api/models/${encodeURIComponent(objectKey)}`, {
    method: 'DELETE'
  });
  if (!resp.ok) {
    throw new Error(await resp.text());
  }
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
      li.appendChild(del);
      list.appendChild(li);
    }
  } catch (err) {
    console.error('Failed to load models:', err);
    list.innerHTML = '<li>Error loading models</li>';
  }
}

document.addEventListener('DOMContentLoaded', loadModels);
