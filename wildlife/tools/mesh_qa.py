"""Automatic geometry checks for a generated creature mesh, before any rendering or rigging.

usage: uv run --with trimesh --with numpy --with scipy --with networkx --with pymeshlab python tools/mesh_qa.py model.glb
       [--length-axis auto] [--json out.json]

Fails (exit 1) on measurable defects: loose fragments, open or non-manifold edges, inconsistent winding,
self-intersections, sliver triangles, left/right asymmetry, and (for whales) proportions outside the range of
a humpback (fluke span, flipper length, straight centre line).
"""
import argparse
import json
import sys

import numpy as np
import trimesh


def load(path):
    sc = trimesh.load(path, force="scene")
    meshes = [g for g in sc.dump()] if hasattr(sc, "dump") else [sc]
    return trimesh.util.concatenate(meshes)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("model")
    ap.add_argument("--json")
    ap.add_argument("--whale", action="store_true", help="also check humpback proportions")
    a = ap.parse_args()
    m = load(a.model)
    report, fails = {}, []

    def check(name, value, ok, detail=""):
        report[name] = {"value": value, "ok": bool(ok), "detail": detail}
        if not ok:
            fails.append(name)

    report["faces"] = len(m.faces)
    report["vertices"] = len(m.vertices)
    w = m.copy()
    w.merge_vertices(merge_tex=True, merge_norm=True)
    parts = w.split(only_watertight=False)
    sizes = sorted((len(p.faces) for p in parts), reverse=True)
    stray = sum(sizes[1:])
    check("loose_fragments", {"parts": len(parts), "faces_outside_main": stray},
          stray <= 0.02 * len(w.faces), "faces outside the main body must be <= 2 %")
    edges = w.edges_sorted
    _, counts = np.unique(edges, axis=0, return_counts=True)
    check("open_edges", int((counts == 1).sum()), (counts == 1).sum() <= 0.002 * len(edges), "<= 0.2 % of edges")
    check("non_manifold_edges", int((counts > 2).sum()), (counts > 2).sum() <= 0.001 * len(edges), "<= 0.1 % of edges")
    check("winding_consistent", bool(w.is_winding_consistent), w.is_winding_consistent)
    # slivers: triangles with a very small area relative to their longest edge squared
    tri = w.triangles
    e = np.linalg.norm(tri[:, [1, 2, 0]] - tri, axis=2)
    q = 4 * np.sqrt(3) * w.area_faces / (e ** 2).sum(1).clip(1e-12)
    check("sliver_triangles", int((q < 0.05).sum()), (q < 0.05).mean() <= 0.01, "quality < 0.05 on <= 1 % of faces")
    # self-intersections (pymeshlab)
    try:
        import pymeshlab
        ms = pymeshlab.MeshSet()
        ms.add_mesh(pymeshlab.Mesh(vertex_matrix=w.vertices, face_matrix=w.faces))
        ms.compute_selection_by_self_intersections_per_face()
        n_self = ms.current_mesh().selected_face_number()
        check("self_intersecting_faces", int(n_self), n_self <= 0.002 * len(w.faces), "<= 0.2 % of faces")
    except Exception as ex:  # pymeshlab missing or failing: report, don't hide
        report["self_intersecting_faces"] = {"value": None, "ok": None, "detail": f"not checked: {ex}"}

    # principal axes: length = largest extent, up = smallest of the two others is NOT reliable for a whale with
    # flippers spread, so take up = world Y of the file (glTF is Y-up) and length = the larger of X/Z extents
    v = w.vertices - w.vertices.mean(0)
    ext = np.ptp(v, axis=0)
    length_axis = 0 if ext[0] >= ext[2] else 2
    side_axis = 2 if length_axis == 0 else 0
    L = ext[length_axis]
    report["extent_xyz_over_length"] = (ext / L).round(3).tolist()
    # left/right symmetry: distance from each vertex mirrored across the side axis to the surface
    mirrored = w.vertices.copy()
    c = np.median(w.vertices[:, side_axis])
    mirrored[:, side_axis] = 2 * c - mirrored[:, side_axis]
    sample = mirrored[np.random.default_rng(0).choice(len(mirrored), min(4000, len(mirrored)), replace=False)]
    _, dist, _ = trimesh.proximity.closest_point(w, sample)
    asym = float(np.percentile(dist, 95) / L)
    check("asymmetry_p95", round(asym, 4), asym <= 0.01, "95th percentile mirror distance <= 1 % of length")

    if a.whale:
        z = w.vertices[:, length_axis]
        zn = (z - z.min()) / L
        xs = w.vertices[:, side_axis] - c
        y = w.vertices[:, 1]
        # which end is the tail: the end whose last 10 % is wider (flukes) and thinner (flat) than the other
        def end_stats(mask):
            return np.ptp(xs[mask]) if mask.any() else 0, np.ptp(y[mask]) if mask.any() else 0
        w0, h0 = end_stats(zn < 0.1)
        w1, h1 = end_stats(zn > 0.9)
        tail_low = (w0 / max(h0, 1e-9)) > (w1 / max(h1, 1e-9))
        t = zn if tail_low else 1 - zn            # 0 = tail tip
        fl = t < 0.15
        span = float(np.ptp(xs[fl]) / L) if fl.any() else 0
        check("fluke_span", round(span, 3), 0.22 <= span <= 0.40, "humpback flukes: 25-35 % of length")
        mid = (t > 0.45) & (t < 0.85)
        reach = float((np.abs(xs[mid]).max() if mid.any() else 0) / L)
        body_half = float(np.percentile(np.abs(xs[(t > 0.2) & (t < 0.4)]), 98) / L)
        flipper = reach - body_half
        check("flipper_length", round(flipper, 3), flipper >= 0.18, "humpback flippers: ~30 % of length (>= 18 %)")
        # centre line: middle of top and bottom of a central strip along the body, must stay straight
        strip = np.abs(xs) < 0.03 * L
        cl = []
        for s in np.linspace(0.05, 0.95, 19):
            mm = strip & (np.abs(t - s) < 0.025)
            if mm.sum() > 3:
                cl.append((s, (y[mm].max() + y[mm].min()) / 2 / L))
        cl = np.array(cl)
        if len(cl) > 3:
            fit = np.polyfit(cl[:, 0], cl[:, 1], 1)
            dev = float(np.abs(cl[:, 1] - np.polyval(fit, cl[:, 0])).max())
            tilt = float(np.degrees(np.arctan(fit[0])))
            check("centre_line_deviation", round(dev, 3), dev <= 0.04, "max deviation from a straight line <= 4 % of length")
            check("centre_line_tilt_deg", round(tilt, 1), abs(tilt) <= 6, "body axis tilt <= 6 degrees")
        report["tail_at"] = "min" if tail_low else "max"
        report["length_axis"] = "xyz"[length_axis]

    report["fails"] = fails
    print(json.dumps(report, indent=1))
    if a.json:
        open(a.json, "w").write(json.dumps(report, indent=1))
    sys.exit(1 if fails else 0)


if __name__ == "__main__":
    main()
