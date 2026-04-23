// Three.js scene setup + render loop. Everything that touches WebGL /
// cameras / controls lives here so main.js never imports 'three' directly.
//
// Exports mutable bindings (`camera`, `controls`) — ES module live-binding
// means importers see the new object after setCamera() swaps them. No need
// for getter shims.

import * as THREE        from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';

export const canvas = document.getElementById('canvas');

// preserveDrawingBuffer lets canvas.toDataURL() read back the rendered image.
// Without it WebGL clears the framebuffer between frames and the PNG export
// comes out transparent/black. Small memory cost, no perf hit at our scale.
export const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, preserveDrawingBuffer: true });
renderer.setPixelRatio(devicePixelRatio);
renderer.setClearColor(0xe8ecf0, 1);

export const scene = new THREE.Scene();
scene.add(new THREE.GridHelper(100, 100, 0x999999, 0xbbbbbb));

// BIM-viewer convention: hemisphere (sky/ground) + directional key light.
// Gives surfaces readable shading without any post-processing.
scene.add(new THREE.HemisphereLight(0xffffff, 0x888899, 0.8));
const keyLight = new THREE.DirectionalLight(0xffffff, 0.9);
keyLight.position.set(1, 2, 1.5);
scene.add(keyLight);

// Two cameras kept in sync; `camera` points at whichever is active so the
// render loop and OrbitControls don't care which mode is on. `let` (not const)
// so setCamera can rebind — ES module exports re-propagate to importers.
export let camera   = new THREE.PerspectiveCamera(60, 1, 0.01, 10000);
camera.position.set(20, 20, 20);
export let controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true;

// Horizontal section: normal (0,-1,0) + constant c means "keep y ≤ c".
// Applied globally via renderer.clippingPlanes so every material (mesh,
// edge overlay, grid) gets cut identically with no per-material wiring.
export const clipPlane = new THREE.Plane(new THREE.Vector3(0, -1, 0), 0);

// Swap the active camera while preserving orbit target and eye position.
// OrbitControls binds to a camera in its constructor, so we dispose and
// recreate on each swap.
export function setCamera(nextCamera) {
  nextCamera.position.copy(camera.position);
  nextCamera.quaternion.copy(camera.quaternion);

  const prevTarget = controls.target.clone();
  controls.dispose();
  camera = nextCamera;
  controls = new OrbitControls(camera, renderer.domElement);
  controls.enableDamping = true;
  controls.target.copy(prevTarget);
  controls.update();

  resize();
}

// Build an ortho camera whose frustum matches the perspective camera's
// current view — same eye, same target, same vertical extent at target
// distance. Gives a seamless-looking switch.
export function makeOrthoCamera() {
  const dist = camera.position.distanceTo(controls.target);
  const fov  = (camera.isPerspectiveCamera ? camera.fov : 60) * Math.PI / 180;
  const h    = 2 * dist * Math.tan(fov / 2);
  const w    = h * (innerWidth / innerHeight);
  return new THREE.OrthographicCamera(-w/2, w/2, h/2, -h/2, 0.01, 10000);
}

export function makePerspectiveCamera() {
  return new THREE.PerspectiveCamera(60, innerWidth / innerHeight, 0.01, 10000);
}

export function resize() {
  const w = innerWidth, h = innerHeight;
  renderer.setSize(w, h);
  if (camera.isPerspectiveCamera) {
    camera.aspect = w / h;
  } else {
    // Preserve vertical extent; recompute horizontal from new aspect.
    const vh = camera.top - camera.bottom;
    const vw = vh * (w / h);
    camera.left  = -vw / 2;
    camera.right =  vw / 2;
  }
  camera.updateProjectionMatrix();
}
window.addEventListener('resize', resize);
resize();

// Start the render loop immediately — scene additions from other modules
// appear on the next frame automatically.
renderer.setAnimationLoop(() => {
  controls.update();
  renderer.render(scene, camera);
});
