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
      del.addEventListener('click', (evt) => {
        evt.preventDefault();
        console.log('Delete Testing');
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
