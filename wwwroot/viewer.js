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

        viewer.getProperties(
            dbId,
            async (props) => {
                const extracted = (props.properties || []).map(p => ({
                    displayName: p.displayName,
                    displayValue: String(p.displayValue),
                    category: p.displayCategory
                }));

                console.log('Extracted metadata array:', extracted);

                try {
                    const urn = window.location.hash ? window.location.hash.substring(1) : null;
                    const resp = await fetch('/api/models/extract-data', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({
                            urn,
                            dbId,
                            name: props.name,
                            externalId: props.externalId,
                            properties: extracted
                        })
                    });
                    if (!resp.ok) {
                        throw new Error(await resp.text());
                    }
                    const echoed = await resp.json();
                    console.log('ExtractData backend response:', echoed);
                } catch (err) {
                    console.error('ExtractData backend call failed:', err);
                }
            },
            (err) => {
                console.error('Extract Data: getProperties failed:', err);
            }
        );
    });
}
