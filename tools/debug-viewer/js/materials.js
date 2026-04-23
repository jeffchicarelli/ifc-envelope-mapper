// BIM-viewer material upgrade. The GLB ships unlit (SharpGLTF
// WithUnlitShader) with no vertex normals. We need lit shading + edge overlay
// to actually read geometry — the look Solibri / BIMcollab / xeokit converge
// on. Done client-side so the serializer stays simple.
//
// Pure function: takes a scene subtree, mutates every Mesh found. No state,
// no DOM — easy to unit-test if we ever grow a JS test harness.

import * as THREE from 'three';

export function upgradeMaterials(root) {
  root.traverse(obj => {
    if (!obj.isMesh) return;

    obj.geometry.computeVertexNormals();

    const old = obj.material;
    const color   = old.color   ? old.color.clone() : new THREE.Color(0xcccccc);
    const opacity = old.opacity ?? 1;
    obj.material = new THREE.MeshLambertMaterial({
      color, opacity,
      transparent: opacity < 1,
      side:        THREE.DoubleSide,
      flatShading: true,
    });
    if (old.dispose) old.dispose();

    // Edge overlay — 30° crease threshold picks up real corners,
    // ignores triangulation seams inside flat faces.
    const edges = new THREE.LineSegments(
      new THREE.EdgesGeometry(obj.geometry, 30),
      new THREE.LineBasicMaterial({ color: 0x000000, transparent: true, opacity: 0.35 })
    );
    edges.userData.isEdgeOverlay = true;
    obj.add(edges);
  });
}
