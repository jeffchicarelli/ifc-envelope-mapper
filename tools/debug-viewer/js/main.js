// Entry point. Wires poll loop, timeline, and UI buttons to the modules
// that own rendering, layers, and picking. Everything stateful-and-shared
// lives in those modules; main.js just orchestrates.

import * as THREE                     from 'three';
import { GLTFLoader }                 from 'three/addons/loaders/GLTFLoader.js';
import {
  canvas, renderer, scene, clipPlane,
  camera, setCamera, makeOrthoCamera, makePerspectiveCamera,
} from './renderer.js';
import { upgradeMaterials }           from './materials.js';
import { rebuildLayers, clearLayerStates } from './layers.js';
import {
  fetchOccupants, rebuildElementIndex,
  resetHighlightsForReload, clearAll, clearVoxelHighlight,
  getOccupantData, getElementIndex, getHighlighted,
  installClickPicking,
} from './picking.js';

// ── DOM refs ──────────────────────────────────────────────────────────────
const statusEl         = document.getElementById('status');
const layersEl         = document.getElementById('layers');
const timelineSliderEl = document.getElementById('timelineSlider');
const tlLabelEl        = document.getElementById('tlLabel');
const liveBtn          = document.getElementById('liveBtn');
const clipBtn          = document.getElementById('clipBtn');
const clipSliderEl     = document.getElementById('clipSlider');
const orthoBtn         = document.getElementById('orthoBtn');

// ── GLTF state ────────────────────────────────────────────────────────────
const loader     = new GLTFLoader();
let   modelRoot  = null;
let   lastMod        = null;
let   helperWasDown  = false;
let   currentSession = null;
const GLB_URL    = '/ifc-debug-output.glb';

// Timeline: ring buffer of ArrayBuffers fetched this session. 30 slots ×
// ~6 MB typical = ~180 MB worst case. Cleared on session change. Raw buffers
// (not parsed scenes) because re-parsing on scrub is O(10 ms) while holding
// parsed three.js objects alive would leak GPU memory unless we disposed on
// eviction (much more code).
const TIMELINE_MAX = 30;
const timeline     = [];
let   isLive       = true;

// ── DevTools hatch ────────────────────────────────────────────────────────
// Module-scope vars are invisible to the DevTools console. Expose a read-only
// accessor bag so you can inspect state while paused, e.g.
//   __dbg.modelRoot.children.map(c => c.name)
window.__dbg = {
  get scene()         { return scene; },
  get modelRoot()     { return modelRoot; },
  get occupantData()  { return getOccupantData(); },
  get elementIndex()  { return getElementIndex(); },
  get highlighted()   { return getHighlighted(); },
  get timeline()      { return timeline; },
};

// ── GLB load ──────────────────────────────────────────────────────────────
function loadBuffer(buf) {
  loader.parse(buf, '', gltf => {
    if (modelRoot) scene.remove(modelRoot);
    modelRoot = gltf.scene;
    // IFC coordinates are Z-up; three.js is Y-up. Rotate -90° around X so
    // vertical building axis points up in the viewer.
    modelRoot.rotation.x = -Math.PI / 2;
    upgradeMaterials(modelRoot);
    scene.add(modelRoot);
    rebuildLayers(modelRoot, layersEl);
    rebuildElementIndex(modelRoot);
    resetHighlightsForReload();
    updateClipRange();
    statusEl.textContent = `Updated ${new Date().toLocaleTimeString()}`;
  }, err => {
    statusEl.textContent = `Parse error: ${err}`;
  });
}

// Slider range tracks the model's world-space Y extent so the slider always
// has meaningful bounds regardless of model size. Called on every load
// because the model can grow/shrink between debug flushes.
function updateClipRange() {
  if (!modelRoot) return;
  const bbox = new THREE.Box3().setFromObject(modelRoot);
  if (!isFinite(bbox.min.y)) return;
  clipSliderEl.min  = bbox.min.y;
  clipSliderEl.max  = bbox.max.y;
  clipSliderEl.step = Math.max((bbox.max.y - bbox.min.y) / 200, 0.01);
  // Preserve current value if still in range; otherwise snap to top (no cut).
  const v = parseFloat(clipSliderEl.value);
  if (!isFinite(v) || v < bbox.min.y || v > bbox.max.y) {
    clipSliderEl.value = bbox.max.y;
  }
  clipPlane.constant = parseFloat(clipSliderEl.value);
}

// ── Poll loop ─────────────────────────────────────────────────────────────
// 200 ms cadence is faster than human F10 in a debugger, so stepping past a
// GeometryDebug.* call shows the new state before you reach the next line.
// Cheap in the common case: localhost fetch + Last-Modified string compare;
// the expensive loader.parse only runs when the header actually changes.
async function poll() {
  try {
    const res = await fetch(GLB_URL, { cache: 'no-store' });
    if (!res.ok) {
      statusEl.textContent = `Server returned ${res.status}`;
      return;
    }
    // Helper transitioned from down → up (new debug session just started).
    // Full page reload picks up any new viewer HTML/JS AND resets every
    // piece of state (scene, lastMod, layer buttons, slider) in one shot.
    if (helperWasDown) {
      location.reload();
      return;
    }
    // Session-ID check: covers the case where the old→new helper handoff is
    // faster than our poll interval (no fetch error ever observed). A
    // different GUID = different helper process = new debug session.
    const session = res.headers.get('X-Debug-Session');
    if (currentSession !== null && session !== currentSession) {
      location.reload();
      return;
    }
    currentSession = session;
    const mod = res.headers.get('Last-Modified');
    if (mod === lastMod) return;
    lastMod = mod;
    const buf = await res.arrayBuffer();
    pushSnapshot(buf);
    // Occupants sidecar changes in lock-step with the GLB (CLI flushes both
    // from the same code path), so we refetch here on every GLB change
    // rather than polling it independently.
    fetchOccupants();
    // In paused (scrubbed) mode, keep displaying whatever the user picked;
    // new snapshots still accumulate for later scrubbing.
    if (isLive) loadBuffer(buf);
  } catch (e) {
    helperWasDown = true;
    lastMod       = null;
    statusEl.textContent = `Fetch error: ${e.message}`;
  }
}

setInterval(poll, 200);
poll();

// ── Timeline scrubber ─────────────────────────────────────────────────────
function pushSnapshot(buf) {
  timeline.push(buf);
  if (timeline.length > TIMELINE_MAX) timeline.shift();
  timelineSliderEl.max      = Math.max(0, timeline.length - 1);
  timelineSliderEl.disabled = timeline.length < 2;
  if (isLive) {
    timelineSliderEl.value = timelineSliderEl.max;
  }
  updateTimelineLabel();
}

function updateTimelineLabel() {
  const idx = parseInt(timelineSliderEl.value, 10);
  tlLabelEl.textContent = timeline.length === 0
    ? '0/0'
    : `${idx + 1}/${timeline.length}`;
}

timelineSliderEl.oninput = () => {
  isLive = false;
  liveBtn.classList.remove('on');
  liveBtn.textContent = 'Paused';
  const idx = parseInt(timelineSliderEl.value, 10);
  if (timeline[idx]) loadBuffer(timeline[idx].slice(0));
  updateTimelineLabel();
};

liveBtn.onclick = () => {
  isLive = true;
  liveBtn.classList.add('on');
  liveBtn.textContent = 'Live';
  if (timeline.length > 0) {
    timelineSliderEl.value = timeline.length - 1;
    loadBuffer(timeline[timeline.length - 1].slice(0));
  }
  updateTimelineLabel();
};

// ── Click picking ─────────────────────────────────────────────────────────
// Module needs modelRoot fresh each click (rebound on every GLB load), so we
// pass a thunk rather than the current value. picking.js imports camera
// directly from renderer.js so the ES module live binding tracks setCamera.
installClickPicking({ canvas, getModelRoot: () => modelRoot, statusEl });

// ── Clear button ──────────────────────────────────────────────────────────
document.getElementById('clearBtn').onclick = () => {
  if (modelRoot) { scene.remove(modelRoot); modelRoot = null; }
  layersEl.innerHTML = '';
  clearLayerStates();
  lastMod            = null;
  timeline.length    = 0;
  timelineSliderEl.max      = 0;
  timelineSliderEl.value    = 0;
  timelineSliderEl.disabled = true;
  clearAll();
  clearVoxelHighlight();
  updateTimelineLabel();
  statusEl.textContent = 'Cleared';
};

// ── PNG export ────────────────────────────────────────────────────────────
// Render one frame synchronously before readback so the canvas buffer is
// guaranteed fresh (render loop is rAF-driven; without this, a click right
// after a state change could capture the pre-change frame).
document.getElementById('shotBtn').onclick = () => {
  renderer.render(scene, camera);
  const ts = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
  const a  = document.createElement('a');
  a.href     = canvas.toDataURL('image/png');
  a.download = `ifc-debug-${ts}.png`;
  a.click();
};

// ── Camera toggle ─────────────────────────────────────────────────────────
orthoBtn.onclick = () => {
  const goOrtho = camera.isPerspectiveCamera;
  setCamera(goOrtho ? makeOrthoCamera() : makePerspectiveCamera());
  orthoBtn.classList.toggle('on', goOrtho);
  orthoBtn.textContent = goOrtho ? 'Persp' : 'Ortho';
};

// ── Clipping plane toggle + slider ────────────────────────────────────────
let clipActive = false;
clipBtn.onclick = () => {
  clipActive = !clipActive;
  renderer.clippingPlanes = clipActive ? [clipPlane] : [];
  clipSliderEl.disabled = !clipActive;
  clipBtn.classList.toggle('on', clipActive);
  if (clipActive) updateClipRange();
};

clipSliderEl.addEventListener('input', () => {
  clipPlane.constant = parseFloat(clipSliderEl.value);
});
