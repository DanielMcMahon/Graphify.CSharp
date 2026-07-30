const relationColors = {
  calls: '#5eead4',
  references: '#93c5fd',
  inherits: '#f0b429',
  implements: '#fb923c',
  injects: '#c4b5fd',
  contains: '#64748b',
  returns: '#38bdf8',
  overrides: '#f87171',
  project_references: '#94a3b8',
  dispatches: '#f472b6',
  handles: '#fde047',
  publishes: '#fdba74',
  routes: '#86efac'
};

const kindColors = {
  Type: '#f0b429',
  Method: '#5eead4',
  Property: '#c4b5fd',
  Field: '#fb923c',
  Namespace: '#64748b',
  Assembly: '#94a3b8',
  Project: '#94a3b8'
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
      levelSeparation: 220,
      nodeSpacing: 140,
      treeSpacing: 180
    }
  },
  physics: { enabled: false },
  interaction: {
    dragView: true,
    zoomView: true,
    dragNodes: true,
    scrollToZoom: true,
    hover: true,
    tooltipDelay: 80,
    multiselect: false,
    navigationButtons: false,
    keyboard: {
      enabled: true,
      bindToWindow: false,
      speed: { x: 10, y: 10, zoom: 0.02 }
    },
    zoomSpeed: 0.1
  },
  nodes: {
    borderWidth: 1,
    borderWidthSelected: 2,
    font: {
      face: 'Instrument Sans, ui-sans-serif, sans-serif',
      color: '#f3f4f6',
      size: 13
    },
    margin: 10,
    shadow: {
      enabled: true,
      color: 'rgba(0,0,0,0.35)',
      size: 8,
      x: 0,
      y: 3
    }
  },
  edges: {
    width: 1.25,
    selectionWidth: 2,
    smooth: false,
    font: {
      face: 'IBM Plex Mono, ui-monospace, monospace',
      size: 10,
      color: '#9aa3b2',
      strokeWidth: 0
    }
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
    <label class="chip active" data-relation="${relation}">
      <input type="checkbox" data-relation="${relation}" checked />
      <span class="dot" style="background:${relationColors[relation]}"></span>
      <span>${relation}</span>
    </label>
  `).join('');

  relationFilters.querySelectorAll('.chip').forEach(chip => {
    const input = chip.querySelector('input');
    chip.addEventListener('click', () => {
      input.checked = !input.checked;
      input.dispatchEvent(new Event('change'));
    });
    input.addEventListener('change', () => {
      if (input.checked) {
        state.enabledRelations.add(input.dataset.relation);
        chip.classList.add('active');
      } else {
        state.enabledRelations.delete(input.dataset.relation);
        chip.classList.remove('active');
      }
      refreshEdgeFilters();
    });
  });

  legend.innerHTML = `
    <div>Pan the canvas, scroll to zoom, drag nodes to rearrange.</div>
    <div style="margin-top:0.35rem">+ / − zoom · F fit · 0 reset</div>
  `;
}

function buildNodeItem(node) {
  const selected = node.id === state.selectedNodeId;
  const accent = kindColors[node.kind] ?? '#94a3b8';
  const isMethod = node.kind === 'Method';

  return {
    id: node.id,
    label: shortenLabel(node.label),
    title: node.title,
    color: {
      background: selected ? '#252b38' : '#181c25',
      border: selected ? '#5eead4' : accent,
      highlight: {
        background: '#252b38',
        border: '#5eead4'
      }
    },
    font: {
      color: selected ? '#ffffff' : '#f3f4f6',
      size: isMethod ? 12 : 13
    },
    borderWidth: selected ? 2 : 1,
    shape: isMethod ? 'box' : 'dot',
    size: isMethod ? undefined : 18,
    widthConstraint: isMethod ? { minimum: 90, maximum: 180 } : undefined,
    shapeProperties: isMethod ? { borderRadius: 8 } : undefined
  };
}

function shortenLabel(label) {
  if (!label || label.length <= 42) {
    return label;
  }

  return `${label.slice(0, 20)}…${label.slice(-18)}`;
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
      arrows: { to: { enabled: true, scaleFactor: 0.65 } },
      color: {
        color: relationColors[edge.relation] ?? '#64748b',
        highlight: '#f3f4f6',
        opacity: 0.85
      }
    }));
}

function focusNode(nodeId) {
  if (!state.network) {
    return;
  }

  state.network.selectNodes([nodeId]);
  state.network.focus(nodeId, { scale: 1.1, animation: false });
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
  const solution = (overview.metadata?.solution_path ?? 'unknown solution').split(/[/\\]/).pop();
  metaEl.textContent = `${overview.metadata?.node_count ?? 0} nodes · ${overview.metadata?.edge_count ?? 0} edges · ${solution}`;
  metaEl.title = `${overview.metadata?.solution_path ?? ''} · built ${builtAt}`;
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
  if (!query) {
    searchResults.innerHTML = '';
    return;
  }

  const results = await fetchJson(`/api/search?q=${encodeURIComponent(query)}&justMyCode=${getJustMyCodeParam()}`);
  if (results.length === 0) {
    searchResults.innerHTML = '<li class="sub" style="color:var(--muted);padding:0.5rem;">No matches.</li>';
    return;
  }

  searchResults.innerHTML = results.map(node => `
    <li>
      <button type="button" class="result-card" data-node-id="${encodeURIComponent(node.id)}">
        <span class="kind-badge">${escapeHtml(node.kind)}</span>
        <strong>${escapeHtml(node.fullName ?? node.name)}</strong>
        <span class="sub">${node.filePath ? `${escapeHtml(node.filePath)}:${node.line ?? ''}` : 'No source location'}</span>
      </button>
    </li>
  `).join('');

  searchResults.querySelectorAll('.result-card').forEach(button => {
    button.addEventListener('click', async () => {
      const nodeId = decodeURIComponent(button.dataset.nodeId);
      await selectNode(nodeId, { reloadGraph: true });
    });
  });
}

function renderEdgeGroup(title, items) {
  if (!items?.length) {
    return `<div class="edge-group"><h4>${title}</h4><div class="edge-item" style="cursor:default"><span>None</span></div></div>`;
  }

  return `
    <div class="edge-group">
      <h4>${title}</h4>
      ${items.map(item => `
        <div class="edge-item" data-node-id="${encodeURIComponent(item.otherNode.id)}">
          <span class="relation-tag" style="background:${relationColors[item.edge.relation] ?? '#94a3b8'}">${escapeHtml(item.edge.relation)}</span>
          <strong>${escapeHtml(item.otherNode.fullName ?? item.otherNode.name)}</strong>
          <span>${escapeHtml(item.edge.confidence)}${item.edge.sourceFile ? ` · ${escapeHtml(item.edge.sourceFile)}:${item.edge.line ?? ''}` : ''}</span>
        </div>
      `).join('')}
    </div>
  `;
}

async function showNodeDetail(nodeId) {
  const detail = await fetchJson(`/api/nodes/${encodeURIComponent(nodeId)}?justMyCode=${getJustMyCodeParam()}`);
  nodeDetail.classList.remove('empty');
  nodeDetail.innerHTML = `
    <span class="kind-badge">${escapeHtml(detail.node.kind)}</span>
    <h3>${escapeHtml(detail.node.fullName ?? detail.node.name)}</h3>
    <div class="meta">${detail.node.filePath ? `${escapeHtml(detail.node.filePath)}:${detail.node.line ?? ''}` : 'No source location'}</div>
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
