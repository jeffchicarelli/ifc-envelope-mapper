// Layer toggle strip — one button per ifcType (or per node name for
// non-element shapes). Per-button 3-state cycle: solid → wireframe → hidden
// → solid.
//
// layerStates survives poll-driven rebuilds so a user-clicked toggle is not
// snapped back to "solid" the next time the algorithm streams in a new
// flush. Module-scope Map rather than per-call closure because every call
// to rebuildLayers wipes and recreates the button strip.

const layerStates = new Map();

export function clearLayerStates() {
    layerStates.clear();
}

function applyState(nodes, state) {
    for (const node of nodes) {
        node.visible = state !== 2;
        node.traverse(obj => {
            if (obj.isMesh && obj.material) obj.material.wireframe = (state === 1);
            // Hide edge overlay in wireframe mode — the wireframe already draws
            // every triangle edge; keeping the overlay makes double-drawn lines.
            if (obj.userData.isEdgeOverlay) obj.visible = (state === 0);
        });
    }
}

function getMeshColor(node) {
    let hex = '#888';
    node.traverse(obj => {
        if (obj.isMesh && obj.material?.color) {
            const c = obj.material.color;
            const r = Math.round(c.r * 255).toString(16).padStart(2, '0');
            const g = Math.round(c.g * 255).toString(16).padStart(2, '0');
            const b = Math.round(c.b * 255).toString(16).padStart(2, '0');
            hex = `#${r}${g}${b}`;
        }
    });
    return hex;
}

// Per-element emission writes one glTF node per BuildingElement tagged with
// { ifcType, globalId } in extras (surfaced as userData by GLTFLoader). We
// group those nodes back into one button per ifcType so the UI stays compact
// — click-picking uses the underlying globalId per mesh. Non-element nodes
// (lines, points, merged debug meshes) have no ifcType and fall back to
// per-node buttons keyed by name.
export function rebuildLayers(modelRoot, layersEl) {
    layersEl.innerHTML = '';
    if (!modelRoot) return;

    // Preserve first-seen order (Map iterates insertion-order) so layer
    // buttons match the order GeometryDebug emissions arrived in.
    const groups = new Map();
    for (const child of modelRoot.children) {
        const key = child.userData?.ifcType || child.name || 'unnamed';
        if (!groups.has(key)) groups.set(key, []);
        groups.get(key).push(child);
    }

    for (const [label, nodes] of groups) {
        const btn = document.createElement('button');
        btn.className = 'layer-btn';
        btn.textContent = nodes.length > 1 ? `${label} (${nodes.length})` : label;
        btn.style.borderColor = getMeshColor(nodes[0]);

        let state = layerStates.get(label) ?? 0; // 0 = solid, 1 = wireframe, 2 = hidden
        applyState(nodes, state);
        btn.classList.toggle('wire', state === 1);
        btn.classList.toggle('off', state === 2);

        btn.onclick = () => {
            state = (state + 1) % 3;
            layerStates.set(label, state);
            applyState(nodes, state);
            btn.classList.toggle('wire', state === 1);
            btn.classList.toggle('off', state === 2);
        };
        layersEl.appendChild(btn);
    }
}
