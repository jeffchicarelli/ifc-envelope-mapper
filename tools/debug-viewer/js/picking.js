// Click picking + occupants sidecar + highlight overlays.
//
// Two click outcomes:
//   1. Hit has a userData.globalId directly → highlight that one element,
//      no voxel lookup (works even when voxels are hidden).
//   2. Otherwise → map hit point to voxel coord, show wireframe cube at the
//      voxel, lookup occupants from sidecar, highlight every element that
//      rasterized there.
//
// Sidecar shape: { voxelSize, origin: [x,y,z], nx, ny, nz,
//                  occupants: { "x,y,z": [globalIds...] } }
// Emitted by GeometryDebug.VoxelOccupants. If absent (older runs,
// lines/points-only sessions, helper-launch failure) click picking silently
// no-ops — no error banner, viewer still functional.

import * as THREE from 'three';
import { camera } from './renderer.js';

let occupantData   = null;
let elementIndex   = new Map();   // globalId → element Object3D
let highlighted    = [];          // meshes whose emissive is currently lit
let voxelHighlight = null;        // wireframe cube marking the picked voxel

export function getOccupantData() { return occupantData; }
export function getElementIndex() { return elementIndex; }
export function getHighlighted()  { return highlighted; }

export async function fetchOccupants() {
  try {
    const res = await fetch('/ifc-debug-occupants.json', { cache: 'no-store' });
    occupantData = res.ok ? await res.json() : null;
  } catch {
    occupantData = null;
  }
}

// Rebuilt on every GLB load because per-element node refs are invalidated
// when the old modelRoot is removed from the scene.
export function rebuildElementIndex(modelRoot) {
  elementIndex = new Map();
  if (!modelRoot) return;
  for (const node of modelRoot.children) {
    const gid = node.userData?.globalId;
    if (gid) elementIndex.set(gid, node);
  }
}

export function resetHighlightsForReload() {
  // Called after a GLB reload — the previous highlighted[] refs belong to
  // the discarded modelRoot, so just drop them. No disposal needed; three.js
  // reclaims on next GC.
  highlighted = [];
}

export function clearHighlights() {
  for (const mesh of highlighted) {
    mesh.material.emissive?.setHex(0x000000);
  }
  highlighted = [];
}

export function clearVoxelHighlight() {
  if (!voxelHighlight) return;
  voxelHighlight.parent?.remove(voxelHighlight);
  voxelHighlight.traverse(obj => {
    obj.geometry?.dispose();
    obj.material?.dispose();
  });
  voxelHighlight = null;
}

export function clearAll() {
  clearHighlights();
  clearVoxelHighlight();
  occupantData = null;
  elementIndex = new Map();
}

// Attached to modelRoot so the GLB-local → world rotation applies for free.
// depthTest:false + high renderOrder keeps the wireframe cube visible even
// when the voxel sits inside opaque geometry (walls, doors).
function highlightVoxel(modelRoot, x, y, z) {
  clearVoxelHighlight();
  if (!modelRoot || !occupantData) return;
  const { voxelSize, origin } = occupantData;

  // Group: filled box + wire outline. The fill (semi-transparent yellow)
  // carries the color signal — LineBasicMaterial renders at 1 px regardless
  // of zoom (WebGL driver cap on gl.linewidth) and disappears in a dense
  // voxel field, so we don't rely on lines alone. Slightly oversized (+1 %)
  // so the fill pokes past the green marker cube's faces without z-fighting.
  voxelHighlight = new THREE.Group();
  // Flag every descendant so the raycaster can skip them — the highlight is
  // added under modelRoot and would otherwise block picks of voxels behind
  // it, drifting the highlight on each subsequent click.
  voxelHighlight.userData.isHighlight = true;

  const size = voxelSize * 1.01;
  const fill = new THREE.Mesh(
    new THREE.BoxGeometry(size, size, size),
    new THREE.MeshBasicMaterial({
      color: 0xffd24a, transparent: true, opacity: 0.45,
      depthTest: false, depthWrite: false, side: THREE.DoubleSide,
    }),
  );
  fill.renderOrder = 999;
  voxelHighlight.add(fill);

  const wire = new THREE.LineSegments(
    new THREE.EdgesGeometry(new THREE.BoxGeometry(size, size, size)),
    new THREE.LineBasicMaterial({ color: 0x000000, depthTest: false }),
  );
  wire.renderOrder = 1000;
  voxelHighlight.add(wire);

  voxelHighlight.position.set(
    origin[0] + (x + 0.5) * voxelSize,
    origin[1] + (y + 0.5) * voxelSize,
    origin[2] + (z + 0.5) * voxelSize,
  );
  modelRoot.add(voxelHighlight);
}

// Yellow matches the layer-button "on" state (brand color for this viewer).
function highlightIds(ids) {
  clearHighlights();
  for (const gid of ids) {
    const node = elementIndex.get(gid);
    if (!node) continue;
    node.traverse(obj => {
      if (obj.isMesh && obj.material?.emissive) {
        obj.material.emissive.setHex(0xffd24a);
        highlighted.push(obj);
      }
    });
  }
}

const raycaster = new THREE.Raycaster();
const ndc       = new THREE.Vector2();

// Wires click + pointerdown handlers on the canvas. `getModelRoot` is a thunk
// because modelRoot is rebound on every GLB load — we can't capture the ref
// at setup time. Camera is imported directly from renderer.js so the ES
// module live binding tracks setCamera() swaps (perspective ↔ ortho).
export function installClickPicking({ canvas, getModelRoot, statusEl }) {
  // Suppress clicks that were really orbit-drag releases — OrbitControls
  // consumes the pointer events for camera motion but the browser still emits
  // `click` on mouseup, which would fire our raycaster on every rotate. 5 px
  // in screen space is well below a deliberate click.
  let downX = 0, downY = 0;
  canvas.addEventListener('pointerdown', e => { downX = e.clientX; downY = e.clientY; });

  canvas.addEventListener('click', e => {
    if (Math.hypot(e.clientX - downX, e.clientY - downY) > 5) return;
    const modelRoot = getModelRoot();
    if (!modelRoot) return;
    const rect = canvas.getBoundingClientRect();
    ndc.x =  ((e.clientX - rect.left) / rect.width)  * 2 - 1;
    ndc.y = -((e.clientY - rect.top)  / rect.height) * 2 + 1;
    raycaster.setFromCamera(ndc, camera);

    // Filter out any node walking back to a Group we tagged isHighlight so
    // the highlight never picks itself. Traverse the ancestry because the hit
    // object is the inner Mesh/LineSegments, not the tagged Group.
    const allHits = raycaster.intersectObject(modelRoot, true);
    const hits = allHits.filter(h => {
      for (let n = h.object; n && n !== modelRoot; n = n.parent) {
        if (n.userData?.isHighlight) return false;
      }
      return true;
    });
    if (hits.length === 0) {
      clearHighlights();
      clearVoxelHighlight();
      return;
    }

    // If we hit an element node directly (it has a globalId), just highlight
    // that one — bypasses the voxel lookup. Useful when voxels are hidden.
    let node = hits[0].object;
    while (node && node !== modelRoot) {
      if (node.userData?.globalId) {
        highlightIds([node.userData.globalId]);
        clearVoxelHighlight();
        statusEl.textContent = `${node.userData.ifcType ?? 'element'} ${node.userData.globalId}`;
        return;
      }
      node = node.parent;
    }

    // Otherwise: map the world-space hit point back into the GLB-local frame
    // (modelRoot is rotated -90° around X) and bucket into the voxel grid.
    if (!occupantData) return;
    const local = modelRoot.worldToLocal(hits[0].point.clone());
    const { voxelSize, origin } = occupantData;
    const x = Math.floor((local.x - origin[0]) / voxelSize);
    const y = Math.floor((local.y - origin[1]) / voxelSize);
    const z = Math.floor((local.z - origin[2]) / voxelSize);
    const key = `${x},${y},${z}`;
    highlightVoxel(modelRoot, x, y, z);
    const ids = occupantData.occupants?.[key];
    if (!ids || ids.length === 0) {
      clearHighlights();
      statusEl.textContent = `Voxel ${key} → empty`;
      return;
    }
    highlightIds(ids);
    statusEl.textContent = `Voxel ${key} → ${ids.length} element${ids.length === 1 ? '' : 's'}`;
  });
}
