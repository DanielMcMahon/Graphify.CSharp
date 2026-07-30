const relationColors = {
  calls: '#5b8cff',
  references: '#35c9a3',
  inherits: '#f0b429',
  implements: '#ff8f5b',
  injects: '#c77dff',
  contains: '#6f7d96',
  returns: '#4cc9f0',
  overrides: '#ff6b6b',
  project_references: '#adb5bd',
  dispatches: '#ff79c6',
  handles: '#f1fa8c',
  publishes: '#ffb86c'
};

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
  enabledRelations: new Set(Object.keys(relationColors)),
  justMyCode: true
};

const networkOptions = {
  layout: {
    hierarchical: {
      enabled: true,
      direction: 'LR',
      sortMethod: 'directed',
      levelSeparation: 200,
      nodeSpacing: 120
    }
  },
  physics: {
    enabled: false
  },
  interaction: {
    dragView: true,
    zoomView: true,
    dragNodes: true,
    scrollToZoom: true,
    hover: true,
    tooltipDelay: 120,
    multiselect: false,
    keyboard: {
      enabled: true,
      bindToWindow: false,
      speed: { x: 10, y: 10, zoom: 0.02 }
    },
    navigationButtons: true,
    zoomSpeed: 0.12
  },
  edges: {
    width: 1.5
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

function initFilters() {
  relationFilters.innerHTML = Object.keys(relationColors).map(relation => `
    <label>
      <input type="checkbox" data-relation="${relation}" checked />
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
    ${Object.entries(relationColors).slice(0, 5).map(([relation, color]) => `
      <div class="legend-row"><span class="swatch" style="background:${color}"></span>${relation}</div>
    `).join('')}
  `;
}

function buildNodeItem(node) {
  const selected = node.id === state.selectedNodeId;
  return {
    id: node.id,
    label: node.label,
    title: node.title,
    color: {
      background: kindColors[node.kind] ?? '#8b95a8',
      border: selected ? '#ffffff' : '#1f2a44',
      highlight: { background: '#ffffff', border: '#5b8cff' }
    },
    font: { color: '#e8eefc', size: 14 },
    borderWidth: selected ? 3 : 1,
    shape: node.kind === 'Method' ? 'box' : 'dot',
    size: node.kind === 'Type' ? 22 : 16
  };
}

function buildEdgeItems(data) {
  return data.edges
    .filter(edge => state.enabledRelations.has(edge.relation))
    .map(edge => ({
      id: edge.id,
      from: edge.from,
      to: edge.to,
      label: edge.label,
      title: edge.title,
      arrows: 'to',
      color: {
        color: relationColors[edge.relation] ?? '#8b95a8',
        highlight: '#ffffff'
      },
      font: { align: 'middle', color: '#cbd5e1', strokeWidth: 0, size: 11 },
      smooth: false
    }));
}

function focusNode(nodeId) {
  if (!state.network) {
    return;
  }

  state.network.selectNodes([nodeId]);
  state.network.focus(nodeId, { scale: 1.15, animation: false });
}

function finishGraphUpdate(shouldFit) {
  if (state.pendingFocusId) {
    const focusId = state.pendingFocusId;
    state.pendingFocusId = null;
    focusNode(focusId);
    return;
  }

  if (shouldFit) {
    fitGraph();
  }
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

async function loadGraph(center = state.centerNodeId, { focusId = null } = {}) {
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
  applyGraphData(data, { shouldFit: !focusId });
}

function applyGraphData(data, { shouldFit = false } = {}) {
  const nodeItems = data.nodes.map(buildNodeItem);
  const edgeItems = buildEdgeItems(data);
  const container = document.getElementById('graph');

  if (!state.network) {
    container.tabIndex = 0;
    state.nodeDataset = new vis.DataSet(nodeItems);
    state.edgeDataset = new vis.DataSet(edgeItems);
    state.network = new vis.Network(container, {
      nodes: state.nodeDataset,
      edges: state.edgeDataset
    }, networkOptions);

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
      await loadGraph(state.centerNodeId, { focusId: params.nodes[0] });
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

    finishGraphUpdate(shouldFit);
    return;
  }

  state.nodeDataset.clear();
  state.nodeDataset.add(nodeItems);
  state.edgeDataset.clear();
  state.edgeDataset.add(edgeItems);
  finishGraphUpdate(shouldFit);
}

async function selectNode(nodeId, { reloadGraph = false } = {}) {
  state.selectedNodeId = nodeId;
  state.centerNodeId = nodeId;

  if (reloadGraph) {
    await loadGraph(nodeId, { focusId: nodeId });
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
searchInput.addEventListener('keydown', event => {
  if (event.key === 'Enter') {
    runSearch();
  }
});
document.getElementById('reloadBtn').addEventListener('click', () => loadGraph(state.centerNodeId));
depthInput.addEventListener('input', () => {
  depthValue.textContent = depthInput.value;
});
depthInput.addEventListener('change', () => loadGraph(state.centerNodeId));
document.getElementById('justMyCode').addEventListener('change', async event => {
  state.justMyCode = event.target.checked;
  await loadOverview();
  await loadGraph(state.centerNodeId, { focusId: state.selectedNodeId });
  if (state.selectedNodeId) {
    await showNodeDetail(state.selectedNodeId);
  }
});

initFilters();
bindGraphControls();
loadOverview()
  .then(() => loadGraph())
  .catch(error => {
    metaEl.textContent = `Failed to load graph: ${error.message}`;
  });
