async function getAccessToken(callback) {
    try {
        const resp = await fetch('/api/auth/token');
        if (!resp.ok) {
            throw new Error(await resp.text());
        }
        const { access_token, expires_in } = await resp.json();
        callback(access_token, expires_in);
    } catch (err) {
        alert('Could not obtain access token. See the console for more details.');
        console.error(err);
    }
}

export function initViewer(container) {
    return new Promise(function (resolve, reject) {
        Autodesk.Viewing.Initializer({ env: 'AutodeskProduction', getAccessToken }, function () {
            const config = {
                extensions: ['Autodesk.DocumentBrowser']
            };
            const viewer = new Autodesk.Viewing.GuiViewer3D(container, config);
            viewer.start();
            viewer.setTheme('light-theme');
            resolve(viewer);
        });
    });
}

export function loadModel(viewer, urn) {
    return new Promise(function (resolve, reject) {
        function onDocumentLoadSuccess(doc) {
            resolve(viewer.loadDocumentNode(doc, doc.getRoot().getDefaultGeometry()));
        }
        function onDocumentLoadFailure(code, message, errors) {
            reject({ code, message, errors });
        }
        viewer.setLightPreset(0);
        Autodesk.Viewing.Document.load('urn:' + urn, onDocumentLoadSuccess, onDocumentLoadFailure);
    });
}

// Extract data for selected element - 4.4.3.1 Extract IFC to JSON workflow
export function setupExtractDataButton(viewer) {
    const btn = document.getElementById('extractDataBtn');
    if (!btn) {
        return;
    }

    btn.addEventListener('click', () => {
        const selection = viewer.getSelection();
        const dbId = selection && selection.length > 0 ? selection[0] : null;
        if (!dbId) {
            console.warn('Extract Data: no object selected.');
            return;
        }

        (async () => {
            try {
                const urn = window.location.hash ? decodeURIComponent(window.location.hash.substring(1)) : null;
                if (!urn) {
                    throw new Error('Missing URN in URL.');
                }

                const resp = await fetch('/api/models/export-element-properties', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ urn, dbId })
                });
                if (!resp.ok) {
                    throw new Error(await resp.text());
                }

                const result = await resp.json();
                console.log('Export complete:', result);
            } catch (err) {
                console.error('Export failed:', err);
            }
        })();
    });
}

export function setupExtractWholeModelButton(viewer) {
    const btn = document.getElementById('extractWholeModelBtn');
    if (!btn) {
        return;
    }

    btn.addEventListener('click', () => {
        const urn = window.location.hash ? decodeURIComponent(window.location.hash.substring(1)) : null;
        if (!urn) {
            console.warn('Extract Whole Model Data: missing URN in URL.');
            return;
        }

        (async () => {
            try {
                const resp = await fetch('/api/models/export-whole-model-properties', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ urn })
                });
                if (!resp.ok) {
                    throw new Error(await resp.text());
                }
                const result = await resp.json();
                console.log('Whole model export complete:', result);
                alert(`Whole model exported to ${result.folder}/${result.fileName}`);
            } catch (err) {
                console.error('Whole model export failed:', err);
                alert('Whole model export failed. See console for details.');
            }
        })();
    });
}

export function setupLoadTransformedDataButton(viewer) {
    const btn = document.getElementById('loadTransformedDataBtn');
    if (!btn) {
        return;
    }

    btn.addEventListener('click', () => {
        const urn = window.location.hash ? decodeURIComponent(window.location.hash.substring(1)) : null;
        if (!urn) {
            alert('Missing URN in URL. Please select a model first.');
            return;
        }

        (async () => {
            btn.setAttribute('disabled', 'true');
            try {
                const resp = await fetch(`/api/models/apply-transformed-data?urn=${encodeURIComponent(urn)}`, {
                    method: 'POST'
                });
                if (!resp.ok) {
                    throw new Error(await resp.text());
                }

                const result = await resp.json();
                console.log('Source IFC path:', result.sourceIfcPath);
                console.log('JSON path:', result.jsonPath);
                console.log('Revised IFC path:', result.revisedIfcPath);
                console.log('Updated IFC by GUID:');
                console.log({
                    elementsUpdated: result.elementsUpdated ?? result.elementCount ?? 0,
                    propertiesUpdated: result.propertiesUpdated ?? result.propertyCount ?? 0,
                    outputIfc: result.outputIfc ?? result.revisedIfcPath
                });
                alert(`Revised IFC saved to ${result.revisedIfcPath}`);
            } catch (err) {
                console.error('Load transformed data failed:', err);
                alert('Could not load transformed data. See console for details.');
            } finally {
                btn.removeAttribute('disabled');
            }
        })();
    });
}
