const relationColors = {
  calls: '#5b8cff',
  references: '#35c9a3',
  inherits: '#f0b429',
  implements: '#ff8f5b',
  injects: '#c77dff',
  registers: '#7ee787',
  contains: '#6f7d96',
  returns: '#4cc9f0',
  overrides: '#ff6b6b',
  project_references: '#adb5bd',
  dispatches: '#ff79c6',
  handles: '#f1fa8c',
  publishes: '#ffb86c',
  routes: '#7ee787'
};

const compositionRelations = new Set([
  'calls',
  'registers',
  'injects',
  'contains',
  'implements'
]);

const kindColors = {
  Type: '#5b8cff',
  Method: '#35c9a3',
  Property: '#c77dff',
  Field: '#f0b429',
  Namespace: '#6f7d96',
  Assembly: '#adb5bd',
  Project: '#adb5bd'
};

const state = {
  network: null,
  nodeDataset: null,
  edgeDataset: null,
  selectedNodeId: null,
  centerNodeId: null,
  pendingFocusId: null,
  enabledRelations: new Set(compositionRelations),
  justMyCode: true,
  physicsSettled: false,
  startupMode: false
};

const networkOptions = {
  autoResize: true,
  physics: {
    enabled: true,
    stabilization: {
      enabled: true,
      iterations: 80,
      fit: true
    },
    barnesHut: {
      gravitationalConstant: -6000,
      springLength: 110,
      damping: 0.12
    }
  },
  interaction: {
    dragView: true,
    zoomView: true,
    dragNodes: true,
    scrollToZoom: true,
    multiselect: false
  },
  nodes: {
    font: { color: '#e8eefc', size: 14, face: 'arial' }
  },
  edges: {
    width: 1,
    smooth: false,
    font: { size: 0 }
  }
};

const metaEl = document.getElementById('meta');
const searchInput = document.getElementById('search');
const searchResults = document.getElementById('searchResults');
const nodeDetail = document.getElementById('nodeDetail');
const depthInput = document.getElementById('depth');
const depthValue = document.getElementById('depthValue');
const relationFilters = document.getElementById('relationFilters');
const legend = document.getElementById('legend');
const graphPlaceholder = document.getElementById('graphPlaceholder');
const startupFileSelect = document.getElementById('startupFile');
const consultQuestion = document.getElementById('consultQuestion');
const consultResult = document.getElementById('consultResult');

function initFilters() {
  relationFilters.innerHTML = Object.keys(relationColors).map(relation => `
    <label>
      <input type="checkbox" data-relation="${relation}" ${state.enabledRelations.has(relation) ? 'checked' : ''} />
      <span>${relation}</span>
    </label>
  `).join('');

  relationFilters.querySelectorAll('input[type="checkbox"]').forEach(input => {
    input.addEventListener('change', () => {
      if (input.checked) {
        state.enabledRelations.add(input.dataset.relation);
      } else {
        state.enabledRelations.delete(input.dataset.relation);
      }
      refreshEdgeFilters();
    });
  });

  legend.innerHTML = `
    <div>Drag background to pan • Scroll/pinch to zoom • Drag nodes to reposition</div>
    <div>Shortcuts: + / - zoom, F fit, 0 reset</div>
    ${['registers', 'injects', 'calls', 'implements', 'contains'].map(relation => `
      <div class="legend-row"><span class="swatch" style="background:${relationColors[relation]}"></span>${relation}</div>
    `).join('')}
  `;
}

function setCompositionPreset() {
  state.enabledRelations = new Set(compositionRelations);
  relationFilters.querySelectorAll('input[type="checkbox"]').forEach(input => {
    input.checked = state.enabledRelations.has(input.dataset.relation);
  });
}

function shortLabel(label, max = 42) {
  if (!label) {
    return '';
  }
  return label.length > max ? `${label.slice(0, max - 1)}…` : label;
}

function buildNodeItem(node) {
  const selected = node.id === state.selectedNodeId;
  const isEntry = node.isEntryPoint;
  return {
    id: node.id,
    label: shortLabel(node.label),
    title: node.title,
    color: {
      background: kindColors[node.kind] ?? '#8b95a8',
      border: selected ? '#ffffff' : (isEntry ? '#7ee787' : '#1f2a44'),
      highlight: { background: '#ffffff', border: '#5b8cff' }
    },
    font: { color: '#e8eefc', size: 14 },
    borderWidth: selected ? 3 : (isEntry ? 2 : 1),
    shape: node.kind === 'Method' ? 'box' : 'dot',
    size: isEntry ? 24 : (node.kind === 'Type' ? 20 : 16)
  };
}

function buildEdgeItems(data) {
  return data.edges
    .filter(edge => state.enabledRelations.has(edge.relation))
    .map(edge => ({
      id: edge.id,
      from: edge.from,
      to: edge.to,
      title: `${edge.label}: ${edge.title ?? ''}`,
      arrows: 'to',
      color: {
        color: relationColors[edge.relation] ?? '#8b95a8',
        highlight: '#ffffff'
      },
      width: edge.relation === 'injects' || edge.relation === 'registers' ? 2 : 1
    }));
}

function focusNode(nodeId) {
  if (!state.network) {
    return;
  }

  state.network.selectNodes([nodeId]);
  state.network.focus(nodeId, { scale: 1.15, animation: false });
}

function settlePhysics(shouldFit) {
  if (!state.network) {
    return;
  }

  state.network.setOptions({ physics: false });
  state.physicsSettled = true;

  if (shouldFit) {
    state.network.fit({ animation: { duration: 300, easingFunction: 'easeInOutQuad' } });
  }
}

function whenStabilized(shouldFit) {
  if (!state.network) {
    return;
  }

  let settled = false;
  const finish = () => {
    if (settled) {
      return;
    }
    settled = true;
    settlePhysics(shouldFit);
  };

  const onDone = () => {
    state.network.off('stabilizationIterationsDone', onDone);
    state.network.off('stabilized', onStabilized);
    finish();
  };

  const onStabilized = () => {
    state.network.off('stabilizationIterationsDone', onDone);
    state.network.off('stabilized', onStabilized);
    finish();
  };

  state.network.on('stabilizationIterationsDone', onDone);
  state.network.on('stabilized', onStabilized);
  window.setTimeout(finish, 6000);
}

function startPhysics() {
  if (!state.network) {
    return;
  }

  state.physicsSettled = false;
  state.network.setOptions({
    physics: {
      enabled: true,
      stabilization: {
        enabled: true,
        iterations: 80,
        fit: true
      }
    }
  });
}

function bindNetwork(container) {
  if (!state.network) {
    return;
  }

  state.network.on('click', async params => {
    if (!params.nodes.length) {
      return;
    }

    state.selectedNodeId = params.nodes[0];
    highlightSelectedNode();
    await showNodeDetail(state.selectedNodeId);
  });

  state.network.on('doubleClick', async params => {
    if (!params.nodes.length) {
      return;
    }

    state.centerNodeId = params.nodes[0];
    state.selectedNodeId = params.nodes[0];
    state.startupMode = false;
    await loadGraph(state.centerNodeId, { focusId: params.nodes[0], restartPhysics: true });
    await showNodeDetail(state.selectedNodeId);
  });

  state.network.on('dragStart', params => {
    if (!params.nodes.length) {
      container.style.cursor = 'grabbing';
    }
  });
  state.network.on('dragEnd', () => {
    container.style.cursor = 'grab';
  });
}

function createNetwork(container, nodeItems, edgeItems) {
  if (typeof vis === 'undefined' || !vis.Network) {
    throw new Error('vis-network failed to load. Check your network connection or ad blocker.');
  }

  state.nodeDataset = new vis.DataSet(nodeItems);
  state.edgeDataset = new vis.DataSet(edgeItems);
  state.network = new vis.Network(container, {
    nodes: state.nodeDataset,
    edges: state.edgeDataset
  }, networkOptions);
  bindNetwork(container);
}

function finishGraphUpdate(shouldFit) {
  if (state.pendingFocusId) {
    const focusId = state.pendingFocusId;
    state.pendingFocusId = null;
    whenStabilized(false);
    focusNode(focusId);
    return;
  }

  whenStabilized(shouldFit);
}

function highlightSelectedNode() {
  if (!state.nodeDataset || !state.lastGraphData) {
    return;
  }

  state.nodeDataset.update(state.lastGraphData.nodes.map(buildNodeItem));
}

function refreshEdgeFilters() {
  if (!state.edgeDataset || !state.lastGraphData) {
    return;
  }

  state.edgeDataset.clear();
  state.edgeDataset.add(buildEdgeItems(state.lastGraphData));
}

function setPlaceholderVisible(visible) {
  graphPlaceholder.classList.toggle('hidden', !visible);
}

function zoomBy(factor) {
  if (!state.network) {
    return;
  }

  const scale = state.network.getScale();
  const nextScale = Math.min(Math.max(scale * factor, 0.05), 8);
  state.network.moveTo({ scale: nextScale, animation: false });
}

function fitGraph() {
  state.network?.fit({ animation: false });
}

function resetView() {
  if (!state.network) {
    return;
  }

  state.network.moveTo({
    position: { x: 0, y: 0 },
    scale: 1,
    animation: false
  });
}

function bindGraphControls() {
  document.getElementById('zoomInBtn').addEventListener('click', () => zoomBy(1.25));
  document.getElementById('zoomOutBtn').addEventListener('click', () => zoomBy(0.8));
  document.getElementById('zoomFitBtn').addEventListener('click', fitGraph);
  document.getElementById('zoomResetBtn').addEventListener('click', resetView);

  document.getElementById('graph').addEventListener('keydown', event => {
    if (!state.network) {
      return;
    }

    if (event.key === '+' || event.key === '=') {
      event.preventDefault();
      zoomBy(1.25);
    } else if (event.key === '-' || event.key === '_') {
      event.preventDefault();
      zoomBy(0.8);
    } else if (event.key === 'f' || event.key === 'F') {
      event.preventDefault();
      fitGraph();
    } else if (event.key === '0') {
      event.preventDefault();
      resetView();
    }
  });
}

async function fetchJson(url) {
  const response = await fetch(url);
  if (!response.ok) {
    throw new Error(`Request failed: ${response.status}`);
  }
  return response.json();
}

function getSelectedRelations() {
  return Array.from(state.enabledRelations).join(',');
}

function getJustMyCodeParam() {
  return state.justMyCode ? 'true' : 'false';
}

function appendCommonParams(params) {
  params.set('justMyCode', getJustMyCodeParam());
  return params;
}

async function loadOverview() {
  const overview = await fetchJson(`/api/overview?justMyCode=${getJustMyCodeParam()}`);
  const builtAt = overview.metadata?.built_at ?? 'unknown';
  const solution = overview.metadata?.solution_path ?? 'unknown solution';
  metaEl.textContent = `${overview.metadata?.node_count ?? 0} nodes • ${overview.metadata?.edge_count ?? 0} edges • ${solution} • built ${builtAt}`;
}

async function loadStartupEntryPoints() {
  const entryPoints = await fetchJson(`/api/startup/entrypoints?justMyCode=${getJustMyCodeParam()}`);
  const files = [...new Set(entryPoints.map(point => point.filePath).filter(Boolean))];

  startupFileSelect.innerHTML = files
    .map(file => `<option value="${escapeHtml(file)}">${escapeHtml(shortPath(file))}</option>`)
    .join('');
  startupFileSelect.hidden = files.length <= 1;
}

function shortPath(filePath) {
  const parts = filePath.split('/');
  return parts.length > 2 ? parts.slice(-2).join('/') : filePath;
}

async function loadStartupMap() {
  setCompositionPreset();
  state.startupMode = true;
  depthInput.value = '4';
  depthValue.textContent = '4';

  const params = appendCommonParams(new URLSearchParams({
    depth: '4',
    maxNodes: '250'
  }));
  if (!startupFileSelect.hidden && startupFileSelect.value) {
    params.set('file', startupFileSelect.value);
  }

  const data = await fetchJson(`/api/startup?${params.toString()}`);
  const entryIds = new Set((data.entryPoints ?? []).map(point => point.id));
  const graph = data.graph ?? data;
  graph.nodes = (graph.nodes ?? []).map(node => ({
    ...node,
    isEntryPoint: entryIds.has(node.id)
  }));

  state.centerNodeId = data.entryPoints?.[0]?.id ?? null;
  state.lastGraphData = graph;
  state.pendingFocusId = state.centerNodeId;
  applyGraphData(graph, { shouldFit: true, restartPhysics: true });
  setPlaceholderVisible(false);

  if (data.entryPoints?.length) {
    nodeDetail.classList.remove('empty');
    nodeDetail.innerHTML = `
      <h3>Startup map</h3>
      <div class="meta">Tracing ${data.entryPoints.length} entry point(s) from Program.cs through registrations and constructor injection.</div>
      ${data.entryPoints.map(point => `
        <div class="edge-item" data-node-id="${encodeURIComponent(point.id)}">
          <strong>${escapeHtml(point.label)}</strong><br />
          <span>${escapeHtml(point.filePath ?? '')}:${point.line ?? ''}</span>
        </div>
      `).join('')}
    `;
    nodeDetail.querySelectorAll('.edge-item[data-node-id]').forEach(item => {
      item.addEventListener('click', async () => {
        const targetId = decodeURIComponent(item.dataset.nodeId);
        await selectNode(targetId, { reloadGraph: false });
      });
    });
  }
}

async function loadGraph(center = state.centerNodeId, { focusId = null, restartPhysics = false } = {}) {
  const depth = depthInput.value;
  const params = appendCommonParams(new URLSearchParams({
    depth,
    maxNodes: '300',
    relations: getSelectedRelations()
  }));
  if (center) {
    params.set('center', center);
  }

  const data = await fetchJson(`/api/graph?${params.toString()}`);
  state.lastGraphData = data;
  state.pendingFocusId = focusId;
  applyGraphData(data, { shouldFit: !focusId, restartPhysics });
  setPlaceholderVisible(false);
}

function applyGraphData(data, { shouldFit = false, restartPhysics = false } = {}) {
  const nodeItems = data.nodes.map(buildNodeItem);
  const edgeItems = buildEdgeItems(data);
  const container = document.getElementById('graph');

  try {
    if (!state.network) {
      container.tabIndex = 0;
      createNetwork(container, nodeItems, edgeItems);
      finishGraphUpdate(shouldFit);
      return;
    }

    state.nodeDataset.clear();
    state.nodeDataset.add(nodeItems);
    state.edgeDataset.clear();
    state.edgeDataset.add(edgeItems);

    if (restartPhysics) {
      startPhysics();
    }

    finishGraphUpdate(shouldFit);
  } catch (error) {
    console.error(error);
    metaEl.textContent = `Graph error: ${error.message}`;
    setPlaceholderVisible(true);
    graphPlaceholder.textContent = `Graph failed to render: ${error.message}. Hard-refresh (Cmd+Shift+R) and try again.`;
  }
}

async function selectNode(nodeId, { reloadGraph = false } = {}) {
  state.selectedNodeId = nodeId;
  state.centerNodeId = nodeId;

  if (reloadGraph) {
    state.startupMode = false;
    await loadGraph(nodeId, { focusId: nodeId, restartPhysics: true });
  } else {
    highlightSelectedNode();
    focusNode(nodeId);
  }

  await showNodeDetail(nodeId);
}

async function runSearch() {
  const query = searchInput.value.trim();
  const results = await fetchJson(`/api/search?q=${encodeURIComponent(query)}&justMyCode=${getJustMyCodeParam()}`);
  searchResults.innerHTML = results.map(node => `
    <li>
      <button type="button" data-node-id="${encodeURIComponent(node.id)}">
        <strong>${escapeHtml(node.fullName ?? node.name)}</strong><br />
        <span>${escapeHtml(node.kind)}${node.filePath ? ` • ${escapeHtml(node.filePath)}:${node.line ?? ''}` : ''}</span>
      </button>
    </li>
  `).join('');

  searchResults.querySelectorAll('button').forEach(button => {
    button.addEventListener('click', async () => {
      const nodeId = decodeURIComponent(button.dataset.nodeId);
      setCompositionPreset();
      await selectNode(nodeId, { reloadGraph: true });
    });
  });
}

function renderEdgeGroup(title, items) {
  if (!items?.length) {
    return `<div class="edge-group"><h4>${title}</h4><div class="edge-item">None</div></div>`;
  }

  return `
    <div class="edge-group">
      <h4>${title}</h4>
      ${items.map(item => `
        <div class="edge-item" data-node-id="${encodeURIComponent(item.otherNode.id)}">
          <strong>${escapeHtml(item.otherNode.fullName ?? item.otherNode.name)}</strong><br />
          <span>${escapeHtml(item.edge.relation)} • ${escapeHtml(item.edge.confidence)}${item.edge.sourceFile ? ` • ${escapeHtml(item.edge.sourceFile)}:${item.edge.line ?? ''}` : ''}</span>
        </div>
      `).join('')}
    </div>
  `;
}

async function showNodeDetail(nodeId) {
  const detail = await fetchJson(`/api/nodes/${encodeURIComponent(nodeId)}?justMyCode=${getJustMyCodeParam()}`);
  nodeDetail.classList.remove('empty');
  nodeDetail.innerHTML = `
    <h3>${escapeHtml(detail.node.fullName ?? detail.node.name)}</h3>
    <div class="meta">${escapeHtml(detail.node.kind)}${detail.node.filePath ? ` • ${escapeHtml(detail.node.filePath)}:${detail.node.line ?? ''}` : ''}</div>
    ${renderEdgeGroup('Callers', detail.callers)}
    ${renderEdgeGroup('Callees', detail.callees)}
    ${renderEdgeGroup('Referenced by', detail.referencesIn)}
    ${renderEdgeGroup('References', detail.referencesOut)}
    ${renderEdgeGroup('Other incoming', detail.otherIncoming)}
    ${renderEdgeGroup('Other outgoing', detail.otherOutgoing)}
  `;

  nodeDetail.querySelectorAll('.edge-item[data-node-id]').forEach(item => {
    item.addEventListener('click', async () => {
      const targetId = decodeURIComponent(item.dataset.nodeId);
      await selectNode(targetId, { reloadGraph: true });
    });
  });
}

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}

document.getElementById('searchBtn').addEventListener('click', runSearch);
document.getElementById('startupBtn').addEventListener('click', () => loadStartupMap().catch(showError));
startupFileSelect.addEventListener('change', () => loadStartupMap().catch(showError));
searchInput.addEventListener('keydown', event => {
  if (event.key === 'Enter') {
    runSearch();
  }
});
document.getElementById('reloadBtn').addEventListener('click', () => {
  if (state.startupMode) {
    loadStartupMap().catch(showError);
  } else if (state.centerNodeId) {
    loadGraph(state.centerNodeId, { restartPhysics: true }).catch(showError);
  }
});
depthInput.addEventListener('input', () => {
  depthValue.textContent = depthInput.value;
});
depthInput.addEventListener('change', () => {
  if (state.startupMode) {
    loadStartupMap().catch(showError);
  } else if (state.centerNodeId) {
    loadGraph(state.centerNodeId, { restartPhysics: true }).catch(showError);
  }
});
document.getElementById('justMyCode').addEventListener('change', async event => {
  state.justMyCode = event.target.checked;
  await loadOverview();
  await loadStartupEntryPoints();
  if (state.startupMode) {
    await loadStartupMap();
  } else if (state.centerNodeId) {
    await loadGraph(state.centerNodeId, { focusId: state.selectedNodeId, restartPhysics: true });
    if (state.selectedNodeId) {
      await showNodeDetail(state.selectedNodeId);
    }
  }
});

function showError(error) {
  metaEl.textContent = `Failed to load graph: ${error.message}`;
}

function renderConsultMarkdown(markdown) {
  return markdown
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll(/^## (.+)$/gm, '<strong>$1</strong>')
    .replaceAll(/^### (.+)$/gm, '<strong>$1</strong>')
    .replaceAll(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
    .replaceAll(/^- (.+)$/gm, '• $1');
}

async function runConsult(question) {
  const q = question.trim();
  if (!q) {
    return;
  }

  consultResult.classList.remove('empty');
  consultResult.textContent = 'Consulting knowledge graph…';

  const data = await fetchJson(`/api/consult?q=${encodeURIComponent(q)}`);
  consultResult.innerHTML = renderConsultMarkdown(data.markdown ?? 'No answer.');
}

document.getElementById('consultBtn').addEventListener('click', () => runConsult(consultQuestion.value).catch(error => {
  consultResult.classList.remove('empty');
  consultResult.textContent = error.message;
}));
document.getElementById('consultMediatorBtn').addEventListener('click', () => {
  consultQuestion.value = 'What if we swapped MediatR for direct handler calls?';
  runConsult(consultQuestion.value).catch(error => {
    consultResult.classList.remove('empty');
    consultResult.textContent = error.message;
  });
});

initFilters();
bindGraphControls();
loadOverview()
  .then(() => loadStartupEntryPoints())
  .catch(showError);
