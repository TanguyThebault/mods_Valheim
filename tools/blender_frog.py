"""Rig the frog, author its clips, export them for the mod, and render previews (headless Blender for the preview).

usage: uv run --project ~/tools/blender-env python tools/blender_frog.py assets/models/frog.tam
         [--out assets/models/frog.rig] [--preview <dir>] [--size 384]

Same method as tools/blender_owl_idle.py: everything in the .tam's own space (the game's: x side, y up, z forward),
the same maths as Unity (bones with identity bind rotations, local pose = T(offset) R(Quaternion.Euler: Z, X, Y)
S, linear blend skinning with bindpose T(-head)), so the preview is what the game shows.

  * skeleton: Root (under the belly), Body (hips), Chest, Head, Throat (the vocal sac), and per side Hind (hip), Shin
    (the knee fold at the back of the haunch), Foot (heel), Toe, Arm (shoulder), Hand;
  * skin: smooth geometric regions (haunches, feet on the ground, front legs, head, throat), 4 influences;
  * clips (30 fps): breathe (loop: the throat pumps, the flanks swell), hop (one jump: crouch, the hind legs kick
    out straight behind, the body flies up nose first with the arms reaching forward, the arms take the landing,
    the legs fold back), croak (the throat sac balloons three times, the body jerks with each call), swim (loop:
    frog kick, hind legs together, arms along the body);
  * QA: per clip, the worst edge stretch and squash and the flipped triangles;
  * preview: the clips one after another from 3 views, MP4 + contact sheets.
"""
import json
import math
import struct
import subprocess
import sys
from pathlib import Path

import numpy as np

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
tam = Path(args[0]).resolve()


def opt(name, default, cast=str):
    return cast(args[args.index(name) + 1]) if name in args else default


out_rig = Path(opt("--out", str(tam.with_suffix(".rig")))).resolve()
preview = Path(opt("--preview", "")).resolve() if "--preview" in args else None
size = opt("--size", 384, int)
FPS = 60                                   # keys per second (the landing is a few hundredths of a second)
FFMPEG = __import__("os").environ.get("UM_FFMPEG_WIN", "ffmpeg")

# ---------------------------------------------------------------- mesh (.tam, game space)
# the source is the uncut mesh when there is one (the cut one is this script's own output, written back to the .tam)
_src = tam.with_name(tam.stem + "_uncut.tam")
data = (_src if _src.exists() else tam).read_bytes()
nv, ni = struct.unpack("<ii", data[4:12])
o = 12
P = np.frombuffer(data, "<f4", nv * 3, o).reshape(-1, 3).astype(np.float64); o += nv * 24
UV = np.frombuffer(data, "<f4", nv * 2, o).reshape(-1, 2); o += nv * 8
IDX = np.frombuffer(data, "<i4", ni, o).reshape(-1, 3)
lo, hi = P.min(0), P.max(0)
SZ = hi - lo
N = (P - lo) / SZ                               # normalised: x 0..1 (0.5 = middle), y 0 ground .. 1, z 0 rear .. 1 snout
x, y, z = N[:, 0], N[:, 1], N[:, 2]
side = np.abs(x - 0.5)
left = x < 0.5
H = SZ[1]


def at(nx, ny, nz):
    return lo + SZ * np.array([nx, ny, nz])


def ss(v, a, b):
    t = np.clip((v - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


# ---------------------------------------------------------------- skeleton
BONES = [("Root", None, at(0.5, 0.0, 0.45)), ("Body", "Root", at(0.5, 0.30, 0.32)), ("Chest", "Body", at(0.5, 0.45, 0.60)),
         ("Head", "Chest", at(0.5, 0.60, 0.72)), ("Throat", "Head", at(0.5, 0.47, 0.82))]
for s, sx in (("L", -1), ("R", 1)):
    c = lambda d: 0.5 + sx * d  # noqa: E731
    BONES += [(f"Hind{s}", "Body", at(c(0.15), 0.40, 0.38)), (f"Shin{s}", f"Hind{s}", at(c(0.33), 0.24, 0.04)),
              (f"Foot{s}", f"Shin{s}", at(c(0.30), 0.08, 0.20)), (f"Toe{s}", f"Foot{s}", at(c(0.30), 0.03, 0.38)),
              (f"Arm{s}", "Chest", at(c(0.15), 0.55, 0.66)), (f"Hand{s}", f"Arm{s}", at(c(0.17), 0.10, 0.63))]
TAILS = {"Root": at(0.5, 0.30, 0.32), "Body": at(0.5, 0.45, 0.60), "Chest": at(0.5, 0.60, 0.72), "Head": at(0.5, 0.85, 0.97),
         "Throat": at(0.5, 0.40, 0.92)}
for s, sx in (("L", -1), ("R", 1)):
    c = lambda d: 0.5 + sx * d  # noqa: E731
    TAILS.update({f"Hind{s}": at(c(0.33), 0.24, 0.04), f"Shin{s}": at(c(0.30), 0.08, 0.20), f"Foot{s}": at(c(0.30), 0.03, 0.38),
                  f"Toe{s}": at(c(0.35), 0.02, 0.50), f"Arm{s}": at(c(0.17), 0.10, 0.63), f"Hand{s}": at(c(0.20), 0.03, 0.80)})
NAMES = [b[0] for b in BONES]
PARENT = [NAMES.index(b[1]) if b[1] else -1 for b in BONES]
HEAD = np.array([b[2] for b in BONES])
IX = {n: i for i, n in enumerate(NAMES)}

# ---------------------------------------------------------------- skin (smooth regions)
W = np.zeros((nv, len(NAMES)))
foot = (1 - ss(y, 0.07, 0.12)) * ss(side, 0.10, 0.15) * (1 - ss(z, 0.48, 0.55))
hind = ss(side, 0.13, 0.20) * (1 - ss(z, 0.46, 0.54)) * (1 - ss(y, 0.55, 0.62)) * (1 - foot)
arm = ss(side, 0.10, 0.15) * ss(z, 0.43, 0.50) * (1 - ss(z, 0.78, 0.84)) * (1 - ss(y, 0.34, 0.42)) * (1 - foot) * (1 - hind)
throat = ss(z, 0.70, 0.76) * ss(y, 0.28, 0.33) * (1 - ss(y, 0.58, 0.64)) * (1 - ss(side, 0.20, 0.26)) * (1 - arm)
head = ss(z, 0.64, 0.72) * ss(y, 0.42, 0.52) * (1 - throat) * (1 - arm)
body = np.clip(1 - foot - hind - arm - throat - head, 0, 1)
for s, m in (("L", left), ("R", ~left)):
    toe_share = ss(z, 0.30, 0.40)
    W[:, IX[f"Toe{s}"]] = foot * m * toe_share
    W[:, IX[f"Foot{s}"]] = foot * m * (1 - toe_share)
    knee = 1 - ss(z, 0.10, 0.22)                    # the back of the haunch rides on the shin
    W[:, IX[f"Shin{s}"]] = hind * m * knee
    W[:, IX[f"Hind{s}"]] = hind * m * (1 - knee)
    hand = 1 - ss(y, 0.10, 0.18)
    W[:, IX[f"Hand{s}"]] = arm * m * hand
    W[:, IX[f"Arm{s}"]] = arm * m * (1 - hand)
W[:, IX["Throat"]] = throat * 0.85
W[:, IX["Head"]] = head + throat * 0.15
front = ss(z, 0.42, 0.62)
W[:, IX["Chest"]] = body * front
W[:, IX["Body"]] = body * (1 - front)


def heat_weights():
    """Blender's bone heat (automatic weights) on the real joints: the limbs follow the surface of the folded legs
    instead of geometric regions (the old regions put the back of the haunch on the shin and froze the legs)."""
    import bpy
    bpy.ops.wm.read_factory_settings(use_empty=True)
    tb = lambda v: (-v[0], -v[2], v[1])  # noqa: E731
    me = bpy.data.meshes.new("frog_w")
    me.from_pydata([tb(v) for v in P], [], IDX[:, [0, 2, 1]].tolist())
    ob = bpy.data.objects.new("frog_w", me)
    bpy.context.scene.collection.objects.link(ob)
    arm = bpy.data.armatures.new("arm")
    ao = bpy.data.objects.new("arm", arm)
    bpy.context.scene.collection.objects.link(ao)
    bpy.context.view_layer.objects.active = ao
    bpy.ops.object.mode_set(mode="EDIT")
    eb = {}
    for i, n in enumerate(NAMES):
        b = arm.edit_bones.new(n)
        b.head, b.tail = tb(HEAD[i]), tb(TAILS[n])
        if PARENT[i] >= 0:
            b.parent = eb[NAMES[PARENT[i]]]
        eb[n] = b
    eb["Root"].use_deform = False
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    ao.select_set(True)
    bpy.context.view_layer.objects.active = ao
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    H_ = np.zeros((nv, len(NAMES)))
    gi = {g.index: IX[g.name] for g in ob.vertex_groups}
    for v in me.vertices:
        for g in v.groups:
            H_[v.index, gi[g.group]] = g.weight
    print("heat: vertices without weights", int((H_.sum(1) == 0).sum()))
    return H_


RADIUS = {"Body": 0.20, "Chest": 0.19, "Head": 0.17, "Throat": 0.10, "Hind": 0.13, "Shin": 0.05, "Foot": 0.04, "Toe": 0.03,
          "Arm": 0.07, "Hand": 0.04}


from scipy.sparse import coo_matrix  # noqa: E402
from scipy.sparse.csgraph import dijkstra  # noqa: E402

# the mesh is split at every UV seam (same position, separate vertices): weld by position for the surface graph
_, WELD = np.unique(np.round(P / SZ.max(), 5), axis=0, return_inverse=True)
WELD = WELD.ravel()


def surface_graph(idx):
    """Edges of the triangles plus zero-length links between the copies of a seam vertex."""
    e = np.unique(np.sort(np.r_[idx[:, [0, 1]], idx[:, [1, 2]], idx[:, [2, 0]]], 1), axis=0)
    ln = np.linalg.norm(P[e[:, 0]] - P[e[:, 1]], axis=1) + 1e-9
    order_ = np.argsort(WELD)
    same = order_[1:][WELD[order_[1:]] == WELD[order_[:-1]]]
    prev = order_[:-1][WELD[order_[1:]] == WELD[order_[:-1]]]
    a = np.r_[e[:, 0], same]
    b = np.r_[e[:, 1], prev]
    w = np.r_[ln, np.full(len(same), 1e-9)]
    return coo_matrix((np.r_[w, w], (np.r_[a, b], np.r_[b, a])), shape=(nv, nv)).tocsr()


GRAPH = surface_graph(IDX)


def envelope_weights():
    """Distance to each bone's segment (head to tail) minus the bone's thickness, in normalised model space; limbs
    only take their own side. Sharp falloff (1/d^4), so each surface follows the nearest segment: the folded haunch
    on the thigh, the strip under it on the shin, the flank and the face on the body and head."""
    L_ = SZ.max()                                   # true proportions (the normalised axes are not to scale)
    Q = P / L_
    E = np.zeros((nv, len(NAMES)))
    for i, n in enumerate(NAMES):
        if n == "Root":
            continue
        a, b = HEAD[i] / L_, TAILS[n] / L_
        ab = b - a
        u = np.clip(((Q - a) @ ab) / max(ab @ ab, 1e-9), 0, 1)
        d = np.linalg.norm(Q - (a + u[:, None] * ab), axis=1)
        r = RADIUS[n.rstrip("LR")] if n[-1] in "LR" and n[:-1] in RADIUS else RADIUS.get(n, 0.1)
        env = np.maximum(d - r, 0)
        if n[:-1] in RADIUS and n[-1] in "LR":
            # limbs: distance measured ALONG THE SURFACE from the vertices hugging the bone, so the long hind toes
            # lying under the chest stay with the foot (straight-line distance gave their tips to the hands, and
            # the unfolded leg pulled a strip of skin along the ground)
            sidem = (x < 0.5) if n[-1] == "L" else (x >= 0.5)
            cand = np.where(sidem)[0]
            seeds = cand[env[cand] <= env[cand].min() + 0.015]
            g = dijkstra(GRAPH, indices=seeds, min_only=True) / L_
            env = np.maximum(env, g - 0.01)
        w = 1.0 / (env + 0.015) ** 4
        if n[-1] == "L" and n[:-1] in RADIUS:
            w *= ss(-(x - 0.5), -0.02, 0.04)
        elif n[-1] == "R" and n[:-1] in RADIUS:
            w *= ss(x - 0.5, -0.02, 0.04)
        E[:, i] = w
    return E


WREG = W.copy()


def mix(Wr):
    W_ = Wr.copy()
    if "--regions" not in args:
        LIMBS = [IX[n] for n in NAMES if n[:-1] in ("Hind", "Shin", "Foot", "Toe", "Arm", "Hand")]
        Hh = heat_weights() if "--heat" in args else envelope_weights()
        Hh /= np.maximum(Hh.sum(1, keepdims=True), 1e-9)
        limb = Hh[:, LIMBS].sum(1)                                    # share of the limbs
        rest = W_.copy()
        rest[:, LIMBS] = 0
        rest /= np.maximum(rest.sum(1, keepdims=True), 1e-9)
        rest[rest.sum(1) == 0, IX["Body"]] = 1
        W_ = rest * (1 - limb)[:, None]
        W_[:, LIMBS] = Hh[:, LIMBS]
        # what lies flat on the ground off the midline is toes and fingers, never the body: given to the limbs
        # (these long hind toes, left on the body, dragged a second "leg" under the belly when the legs unfolded)
        ground = (y < 0.08) & (side > 0.015)
        W_[np.ix_(ground, [IX[n] for n in ("Body", "Chest", "Head", "Throat")])] = 0
        none = ground & (W_.sum(1) < 1e-6)
        W_[np.ix_(none, LIMBS)] = Hh[np.ix_(none, LIMBS)] + 1e-9
        # the fingertips meeting under the chin, on the midline: hands, not the chest (they hung as a stick when
        # the body pitched into the swim)
        chin = (y < 0.16) & (z > 0.74) & (side < 0.06)
        W_[chin] = 0
        W_[chin & left, IX["HandL"]] = 1
        W_[chin & ~left, IX["HandR"]] = 1
        # one limb per vertex: a hind toe lying against a hand follows the foot only (mixing tore the toe tips)
        for sd in "LR":
            hc = [IX[f"{c}{sd}"] for c in ("Hind", "Shin", "Foot", "Toe")]
            fc = [IX[f"{c}{sd}"] for c in ("Arm", "Hand")]
            hs, fs_ = W_[:, hc].sum(1), W_[:, fc].sum(1)
            W_[np.ix_(hs >= fs_, fc)] = 0
            W_[np.ix_(fs_ > hs, hc)] = 0
    order_ = np.argsort(-W_, axis=1)[:, :4]
    W4 = np.take_along_axis(W_, order_, 1)
    W4 /= np.maximum(W4.sum(1, keepdims=True), 1e-9)
    WF_ = np.zeros_like(W_)
    np.put_along_axis(WF_, order_, W4, 1)
    return WF_, order_


WF, order = mix(WREG)

# The generated mesh welded the tips of the long hind toes to the hands, and a hind foot to the belly, with a few
# thin triangles on the ground: once a leg unfolds they stretch into a strip of skin. Cut them (they lie under the
# frog) and weigh again on the cut surface. The cut mesh is written back to the .tam (the uncut one is kept beside).
HINDLOW = {IX[f"{c}{s}"] for c in ("Shin", "Foot", "Toe") for s in "LR"}
OTHER = {IX[n] for n in ("Body", "Chest", "ArmL", "ArmR", "HandL", "HandR")}
mb = WF.argmax(1)
tri_h = np.isin(mb[IDX], list(HINDLOW)).any(1)
tri_o = np.isin(mb[IDX], list(OTHER)).any(1)
low = N[IDX][:, :, 1].mean(1) < 0.22
HANDS = {IX["HandL"], IX["HandR"]}
CORE = {IX["Body"], IX["Chest"]}
fingers = np.isin(mb[IDX], list(HANDS)).any(1) & np.isin(mb[IDX], list(CORE)).any(1) & (N[IDX][:, :, 1].mean(1) < 0.1)
bridge = (tri_h & tri_o & low) | fingers                 # fingers welded to the chest under the chin, too
# the heel lies against the haunch (thigh to foot, skipping the shin) and the inner forearm against the chest
# at the ground: both welds open into webs when the limb moves
has = lambda names: np.isin(mb[IDX], [IX[n] for n in names]).any(1)  # noqa: E731
heel = (has(["HindL"]) & has(["FootL", "ToeL"])) | (has(["HindR"]) & has(["FootR", "ToeR"]))
pit = has(["ArmL", "ArmR"]) & has(["Chest", "Body"]) & (N[IDX][:, :, 1].mean(1) < 0.12)
# not cut (that left holes): those seams blend the two sides, so the skin stretches between them instead
SEAMBLEND = np.unique(IDX[heel | pit])
# small loose pieces (a few triangles cut off from everything) go too
from scipy.sparse.csgraph import connected_components  # noqa: E402
ncomp, lab = connected_components(surface_graph(IDX[~bridge]), directed=False)
tri_lab = lab[IDX[:, 0]]
size_ = np.bincount(tri_lab[~bridge], minlength=ncomp)
islands = (~bridge) & (size_[tri_lab] < 25)
if islands.any():
    print("drop", int(islands.sum()), "triangles in", int(len(np.unique(tri_lab[islands]))), "small islands")
bridge = bridge | islands
if bridge.any():
    print("cut", int(bridge.sum()), "bridging triangles on the ground")
    IDX = IDX[~bridge]
    ni = IDX.size
    GRAPH = surface_graph(IDX)
    WF, order = mix(WREG)
if len(SEAMBLEND):
    # each seam vertex: half its own weights, half the mean of its seam neighbours' (smooths the fold into a stretch)
    for _ in range(3):
        nb = WF.copy()
        acc = np.zeros_like(WF)
        cnt = np.zeros(nv)
        for a_, b_ in ((0, 1), (1, 2), (2, 0), (1, 0), (2, 1), (0, 2)):
            np.add.at(acc, IDX[:, a_], WF[IDX[:, b_]])
            np.add.at(cnt, IDX[:, a_], 1)
        nb[SEAMBLEND] = 0.5 * WF[SEAMBLEND] + 0.5 * acc[SEAMBLEND] / np.maximum(cnt[SEAMBLEND, None], 1)
        WF = nb
    order = np.argsort(-WF, axis=1)[:, :4]
    W4 = np.take_along_axis(WF, order, 1)
    W4 /= np.maximum(W4.sum(1, keepdims=True), 1e-9)
    WF = np.zeros_like(WF)
    np.put_along_axis(WF, order, W4, 1)
    print("blended", len(SEAMBLEND), "seam vertices (heel, armpit)")
    if not _src.exists():
        _src.write_bytes(data)
    head_ = struct.pack("<4sii", data[:4], nv, ni)
    rest_ = data[12:12 + nv * 32]                        # positions, normals, uvs
    tam.write_bytes(head_ + rest_ + IDX.astype("<i4").tobytes())
print("vertices per main bone", {n: int((WF.argmax(1) == i).sum()) for i, n in enumerate(NAMES)})


# ---------------------------------------------------------------- clips
def keys(points, dur, ease="smooth"):
    n = int(round(dur * FPS))
    t = np.arange(n) / FPS
    ts = np.array([p[0] for p in points])
    vs = np.array([p[1] for p in points], dtype=float)
    out = np.empty(n)
    for i, tt_ in enumerate(t):
        j = np.searchsorted(ts, tt_, side="right") - 1
        if j < 0:
            out[i] = vs[0]
        elif j >= len(ts) - 1:
            out[i] = vs[-1]
        else:
            u = (tt_ - ts[j]) / (ts[j + 1] - ts[j])
            if ease == "smooth":
                u = u * u * (3 - 2 * u)
            out[i] = vs[j] + (vs[j + 1] - vs[j]) * u
    return out


def euler_unity(ex, ey, ez):
    ex, ey, ez = map(math.radians, (ex, ey, ez))
    cx, sx, cy, sy, cz, sz = math.cos(ex), math.sin(ex), math.cos(ey), math.sin(ey), math.cos(ez), math.sin(ez)
    Rx = np.array([[1, 0, 0], [0, cx, -sx], [0, sx, cx]])
    Ry = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]])
    Rz = np.array([[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]])
    return Ry @ Rx @ Rz


CLIPS = {}


def clip(name, dur, loop, chans, sym=True):
    """Channels given for the left side are mirrored onto the right (same pitch, opposite yaw/roll/x)."""
    n = int(round(dur * FPS))
    full = {}
    for (b, ch), v in chans.items():
        full[(b, ch)] = np.asarray(v, float)[:n]
        if sym and b.endswith("L"):
            r = b[:-1] + "R"
            full[(r, ch)] = (-1 if ch in ("ry", "rz", "px") else 1) * np.asarray(v, float)[:n]
    CLIPS[name] = {"frames": n, "loop": loop, "keys": full}


tt = lambda dur: np.arange(int(round(dur * FPS))) / FPS  # noqa: E731

# breathe: the floor of the mouth pumps quickly (6 pumps per loop), the flanks swell once, the head follows a little
d = 2.4
t_ = tt(d)
# pumps: fast in (40 %), slow out (60 %), three then a pause of one, twice per loop
pump = np.zeros(len(t_))
for t0 in (0.0, 0.4, 0.8, 1.6, 2.0):
    pump = np.maximum(pump, keys([(0, 0), (t0, 0), (t0 + 0.13, 1), (t0 + 0.32, 0), (d, 0)], d))
clip("breathe", d, True, {
    ("Throat", "s"): 1 + 0.16 * pump, ("Throat", "py"): -0.01 * pump,
    ("Chest", "s"): 1 + 0.03 * np.sin(2 * np.pi * t_ / d),
    ("Head", "rx"): -1.5 * np.sin(2 * np.pi * t_ / d) - 1.0 * pump,
})

# hop (Body rx: negative = nose up): crouch, takeoff along the trajectory nose up, level at the apex, nose down
# into the landing on the hands, settle. The generated haunch is one folded lump: any real unfolding hangs the feet
# under the belly (checked bone by bone with --poses), so the legs stay tucked and the feet only lift off a little.
d = 0.6
# The hop clip is the frog's own motion around a fixed spot; the game adds the flight (a ballistic arc, its height and
# length set by the speed, FrogAnim) and stretches the flight segment of the clip to the flight time. Segments:
# crouch 0-0.09 s and push 0.09-0.20 (feet planted: the unfolding legs lift the body, Root "lift"), flight
# 0.20-0.50, landing 0.50-0.60 (hands first, then the rear settles).
# Ground contact: on the ground the body only tilts about what touches the ground (hind feet on the crouch and the
# takeoff, the hands on the landing): Root rotates about that pivot, so the feet and hands never slide or sink.
HOP_TAKEOFF, HOP_LAND = 0.20, 0.50
LENGTH_M_, SCALE_C_ = 0.45, 1.3                       # [Frog] Length and Scale (the game's size of the frog)
# flight segment of the clip: launch 0.20-0.32 and arrival 0.38-0.50 keep ~0.12 s each in the game, the cruise
# 0.32-0.38 is stretched; in the air the game adds a pitch following the flight path (FrogAnim / game_hop_frames)
# the landing: hands first, steeply nose down so the rear stays up while the legs fold in the air, then it drops
tilt = keys([(0, 0), (0.06, -5), (0.12, -10), (0.20, -12), (0.28, -3), (0.32, 0), (0.38, 0), (0.47, 14), (0.50, 18),
             (0.53, 26), (0.55, 26), (0.6, 0)], d)
pivot_w = keys([(0, 0), (HOP_TAKEOFF, 0), (0.40, 1.0), (d, 1.0)], d)       # 0: hind feet, 1: hands
PIV_FEET, PIV_HANDS = at(0.5, 0.0, 0.30), at(0.5, 0.0, 0.70)
rootoff = np.zeros((len(tilt), 3))
for i, (th, w) in enumerate(zip(tilt, pivot_w)):
    piv = PIV_FEET * (1 - w) + PIV_HANDS * w
    r0 = HEAD[0]
    rootoff[i] = euler_unity(th, 0, 0) @ (r0 - piv) + piv - r0
steady = keys([(0, 0), (0.18, 0), (0.22, 1), (0.26, 1), (0.34, 0), (0.6, 0)], d)
# arms: reach forward in the air, back to the rest pose (hands on the ground) for the landing
reach = keys([(0, 0), (0.18, 0.3), (0.24, 1.0), (0.32, 1.0), (0.38, 0.35), (0.42, -0.33), (0.49, 0), (0.6, 0)], d)   # forward in the air, down to the rest pose for the touchdown (planted hands)
# legs: begin to unfold on the push (feet still down), straight back in the air, folded again before the rear lands
ext = keys([(0, 0), (0.09, 0), (0.20, 1.0), (0.47, 1.0), (0.50, 0.65), (0.53, 0.15), (0.55, 0), (0.6, 0)], d)   # out behind until the hands touch down, folded (eased) while the rear is still up
air = keys([(0, 0), (0.20, 0), (0.26, 1.0), (0.44, 1.0), (0.50, 0), (0.6, 0)], d)     # feet trail in line with the shins
hindair = keys([(0, 0), (0.20, 0), (0.25, 1.0), (0.35, 1.25), (0.44, 1.0), (0.48, 0), (0.6, 0)], d)   # legs lifted in line with the body
# the cruise (stretched in long flights): a slow sway so the pose never freezes
sway = np.where((t_h := np.arange(len(ext)) / FPS) >= 0.32, 1.0, 0.0) * np.where(t_h <= 0.38, 1.0, 0.0) * np.sin(2 * np.pi * (t_h - 0.32) / 0.06)
# the shins fold first, so the feet swing in under the haunch instead of planting the shins like stilts
extS = keys([(0, 0), (0.09, 0), (0.20, 1.0), (0.47, 1.0), (0.49, 0.45), (0.52, 0), (0.6, 0)], d)
impact = keys([(0, 0), (0.50, 0), (0.53, 1.0), (0.58, 0), (0.6, 0)], d)
tuck = keys([(0, 0), (0.6, 0)], d)                    # (no knee tuck: it swung the toes down and forward)
toeflat = keys([(0, 1.0), (HOP_TAKEOFF, 1.0), (HOP_TAKEOFF + 0.06, 0.0), (d, 0.0)], d)
clip("hop", d, True, {
    ("Root", "rx"): tilt, ("Root", "py"): rootoff[:, 1], ("Root", "pz"): rootoff[:, 2],
    ("Head", "rx"): 0.15 * tilt + 4 * steady, ("Chest", "rx"): 2 * steady,
    ("HindL", "rx"): 25 * ext + 22 * hindair, ("ShinL", "rx"): 70 * extS, ("Body", "rx"): 5 * sway, ("FootL", "rx"): 50 * ext - 30 * air, ("Chest", "s"): 1 - 0.03 * impact,
    # on the push the toes stay flat on the ground (the heel lifts first), in the air they trail back
    ("ToeL", "rx"): -toeflat * (145 * ext + tilt),
    ("ArmL", "rx"): -60 * reach, ("ArmL", "rz"): -10 * reach,
    ("HandL", "rx"): 40 * reach,
})

# croak: the throat sac balloons three times (rise 4 frames, hold 3, deflate 5), pushed down and forward; the chest
# empties into it; the body jerks forward on each inflation
d = 1.5
t_ = tt(d)
puff = np.zeros(len(t_))
for t0 in (0.12, 0.6, 1.02):
    puff = np.maximum(puff, keys([(0, 0), (t0, 0), (t0 + 4 / 30, 1), (t0 + 7 / 30, 1), (t0 + 12 / 30, 0), (d, 0)], d))
jerk = puff ** 2                                   # peaks with the sac
clip("croak", d, False, {
    ("Throat", "s"): 1 + 0.85 * puff, ("Throat", "py"): -0.06 * puff, ("Throat", "pz"): 0.09 * puff,
    ("Chest", "s"): 1 - 0.03 * puff, ("Body", "rx"): -4 * jerk, ("Root", "py"): 0.01 * jerk,
    ("Head", "rx"): -4 * puff,
})

# swim (positive Body rx = nose down): the body laid out level, arms folded back along the flanks; the kick sweeps
# the feet back and out, the body surges forward, a slow recovery
d = 0.9
t_ = tt(d)
stroke = keys([(0, 0), (4 / 30, 1.0), (13 / 30, 0.15), (d, 0)], d)
glide = keys([(0, 0), (4 / 30, 1.0), (13 / 30, 0.0), (d, 0)], d)
bob = 0.01 * np.sin(2 * np.pi * t_ / d)
# the frog kick: legs drawn up (folded, as the model rests), a fast kick that unfolds them straight back and spread,
# a glide with the legs out and the arms swept back, then the slow draw-up while the arms come forward
kickx = keys([(0, 0.05), (4 / 30, 1.0), (13 / 30, 1.0), (0.78, 0.05), (d, 0.05)], d)
arms = keys([(0, -0.6), (4 / 30, 1.0), (13 / 30, 1.0), (0.7, -0.6), (d, -0.6)], d)
lead = keys([(0, 0), (1 / 30, 1.0), (4 / 30, 0), (d, 0)], d)          # the feet turn back first, never straight down
clip("swim", d, True, {
    ("Body", "rx"): 25 - 5 * glide, ("Root", "rx"): 10 + 0 * t_, ("Root", "py"): 0.05 * glide + bob, ("Head", "rx"): -14 + 0 * t_,
    ("ArmL", "rx"): 50 + 35 * arms, ("ArmL", "rz"): 12 + 0 * t_, ("HandL", "rx"): 20 + 25 * np.clip(arms, 0, 1),
    ("HindL", "rx"): 25 * kickx - 10, ("HindL", "rz"): -12 * kickx, ("ShinL", "rx"): 70 * kickx, ("FootL", "rx"): 50 * kickx + 30 * lead,
    ("ToeL", "rx"): 25 * kickx, ("ToeL", "rz"): -15 * kickx,
})


def sample(name, frame):
    c = CLIPS[name]
    return {k: v[frame % c["frames"] if c["loop"] else min(frame, c["frames"] - 1)] for k, v in c["keys"].items()}


# ---------------------------------------------------------------- skinning (Unity maths)
def skin(pose, mats=False):
    world = [None] * len(NAMES)
    for i, n in enumerate(NAMES):
        g = lambda ch, dflt=0.0: pose.get((n, ch), dflt)  # noqa: E731
        local = np.eye(4)
        par = HEAD[PARENT[i]] if PARENT[i] >= 0 else np.zeros(3)
        local[:3, 3] = HEAD[i] - par + np.array([g("px"), g("py"), g("pz")])
        local[:3, :3] = euler_unity(g("rx"), g("ry"), g("rz")) * g("s", 1.0)
        world[i] = (world[PARENT[i]] @ local) if PARENT[i] >= 0 else local
    skinm = np.stack([world[i] @ np.block([[np.eye(3), -HEAD[i][:, None]], [np.zeros((1, 3)), np.ones((1, 1))]])
                      for i in range(len(NAMES))])
    ph = np.c_[P, np.ones(nv)]
    per = np.einsum("bij,vj->vbi", skinm, ph)[:, :, :3]
    V = (per * WF[:, :, None]).sum(1)
    return (V, skinm) if mats else V


edges = np.unique(np.sort(np.r_[IDX[:, [0, 1]], IDX[:, [1, 2]], IDX[:, [2, 0]]], 1), axis=0)
rest_len = np.linalg.norm(P[edges[:, 0]] - P[edges[:, 1]], axis=1)
ok = rest_len > 1e-6


def normals(V):
    nn = np.cross(V[IDX[:, 1]] - V[IDX[:, 0]], V[IDX[:, 2]] - V[IDX[:, 0]])
    return nn / np.maximum(np.linalg.norm(nn, axis=1, keepdims=True), 1e-12)


N0 = normals(P)
MAIN = WF[IDX[:, 0]].argmax(1)
qa = {}
for name, c in CLIPS.items():
    worst_s, worst_q, flips = 1.0, 1.0, 0
    for fr in range(c["frames"]):
        V, M = skin(sample(name, fr), True)
        ref = np.einsum("tij,tj->ti", M[MAIN][:, :3, :3], N0)
        flips = max(flips, int(((normals(V) * ref).sum(1) < 0).sum()))
        r = np.linalg.norm(V[edges[ok, 0]] - V[edges[ok, 1]], axis=1) / rest_len[ok]
        worst_s, worst_q = max(worst_s, r.max()), min(worst_q, r.min())
    qa[name] = {"seconds": round(c["frames"] / FPS, 2), "max_edge_stretch": round(worst_s, 3),
                "min_edge_squash": round(worst_q, 3), "max_flipped_triangles": flips, "triangles": len(IDX)}
print("QA", json.dumps(qa))
if "--worst" in args:
    V = skin(sample("swim", 6))
    low = np.argsort(V[:, 1])[:12]
    for i in low:
        print("lowest swim6", i, NAMES[WF[i].argmax()], "rest", np.round(N[i], 3), "posed y", round(float((V[i, 1] - lo[1]) / SZ[1]), 3),
              {NAMES[b]: round(float(WF[i, b]), 2) for b in np.nonzero(WF[i])[0]})
    for name, fr in (("hop", 9), ("swim", 6)):
        V = skin(sample(name, fr))
        for bn in ("ToeL", "ToeR", "FootL", "FootR"):
            m = WF.argmax(1) == IX[bn]
            print(name, fr, bn, "rest", np.round(((P[m].mean(0) - lo) / SZ), 2), "posed", np.round(((V[m].mean(0) - lo) / SZ), 2))
    for name in ("hop", "swim"):
        best = (0, None, None)
        for fr in range(CLIPS[name]["frames"]):
            V = skin(sample(name, fr))
            r = np.linalg.norm(V[edges[ok, 0]] - V[edges[ok, 1]], axis=1) / rest_len[ok]
            if r.max() > best[0]:
                best = (r.max(), fr, r)
        r = best[2]
        e = edges[ok][np.argsort(-r)[:12]]
        print(name, "frame", best[1], "edges > 3x:", int((r > 3).sum()), "> 1.8x:", int((r > 1.8).sum()))
        for (i, j), rr in zip(e, np.sort(r)[::-1][:12]):
            print(f"  {rr:5.1f}  {NAMES[WF[i].argmax()]:7s} {np.round(N[i], 2)}  {NAMES[WF[j].argmax()]:7s} {np.round(N[j], 2)}  len {rest_len[(edges[:, 0] == i) & (edges[:, 1] == j)][0] / SZ.max():.4f}")

# the push: while the legs unfold the toes press on the ground and lift the body. Channel Root "lift" (model units):
# the height that keeps the lowest point of the frog at the ground; the game flies at max(ballistic arc, lift).
# Channel Root "push" (model units, forward): the planted toes don't slide, so as the legs sweep back the body moves
# forward (the frog pushes itself off); the game adds it to the visual position until the takeoff.
_hk = CLIPS["hop"]["keys"]
# arrival and landing: as the shin folds the foot swings down like a pendulum; raise the thigh just
# enough (Hind rx, positive: the leg swings back and up), frame by frame, so no part of the leg goes below the ground
# (a small IK), then smooth it
_legv = np.isin(WF.argmax(1), [IX[f"{c}{sd}"] for c in ("Hind", "Shin", "Foot", "Toe") for sd in "LR"])
_raise = np.zeros(CLIPS["hop"]["frames"])
for fr in range(CLIPS["hop"]["frames"]):
    if fr / FPS < 0.38:
        continue
    for dlt in range(0, 62, 2):
        pose = dict(sample("hop", fr))
        for sd in "LR":
            pose[(f"Hind{sd}", "rx")] = pose.get((f"Hind{sd}", "rx"), 0.0) + dlt
        # while the legs still move (unfolding left) the feet travel just above the ground, then set down
        clear = 0.03 * SZ[1] if ext[fr] > 0.03 else -0.004 * SZ[1]
        if skin(pose)[_legv, 1].min() >= lo[1] + clear:
            break
    _raise[fr] = dlt
_raise = np.convolve(np.r_[_raise[:1], _raise, _raise[-1:]], [0.25, 0.5, 0.25], "valid")
for sd in "LR":
    _hk[(f"Hind{sd}", "rx")] = _hk[(f"Hind{sd}", "rx")] + _raise
print("hop thigh raise (deg):", [int(v) for v in _raise])
_lift = np.zeros(CLIPS["hop"]["frames"])
_push = np.zeros(CLIPS["hop"]["frames"])
_V0 = skin(sample("hop", 0))
_hindv = np.isin(WF.argmax(1), [IX[f"{c}{sd}"] for c in ("Foot", "Toe") for sd in "LR"])
_plant = _hindv & (_V0[:, 1] < lo[1] + 0.02 * SZ[1])          # the soles on the ground at rest
for fr in range(CLIPS["hop"]["frames"]):
    if fr / FPS <= HOP_TAKEOFF + 1e-6 or fr / FPS >= 0.38:     # push, arrival and landing
        _Vh = skin(sample("hop", fr))
        if fr / FPS <= HOP_TAKEOFF + 1e-6:
            _push[fr] = -float(np.mean(_Vh[_plant, 2] - _V0[_plant, 2]))
            _lift[fr] = max(0.0, lo[1] - _Vh[:, 1].min(), -float(np.mean(_Vh[_plant, 1] - _V0[_plant, 1])))
        else:
            _lift[fr] = max(0.0, lo[1] - _Vh[:, 1].min())   # whatever hangs lowest touches down, never sinks
        if "--groundlog" in args:
            print("  lift fr", fr, "lowest", NAMES[WF[_Vh[:, 1].argmin()].argmax()], "pos", np.round((_Vh[_Vh[:, 1].argmin()] - lo) / SZ, 2))
_tk = int(round(HOP_TAKEOFF * FPS))
_lift[:_tk + 1] = np.maximum.accumulate(_lift[:_tk + 1])     # pushing never lowers the body before the takeoff
_hk[("Root", "lift")] = _lift
_hk[("Root", "push")] = _push
print("hop push (cm in game):", [round(float(v) * LENGTH_M_ * SCALE_C_ / SZ[2] * 100, 1) for v in _push])
print("hop lift (cm in game):", [round(float(v) * LENGTH_M_ * SCALE_C_ / SZ[2] * 100, 1) for v in _lift])

# ---------------------------------------------------------------- the hop as the game plays it (FrogAnim)
# Same rules as FrogAnim.cs: from the speed v, the hop length L and height h (h = HOP_K L, at most HOP_HMAX) with a
# ballistic flight time Tf = 2 sqrt(2h/g); crouch and landing keep their real durations; the clip's flight segment
# is stretched to Tf; the visual stays on its takeoff spot during the crouch, flies a parabola, then stays on its
# landing spot (the character's collider moves on at an even speed underneath: FrogAnim offsets the visual).
LENGTH_M, SCALE_C = LENGTH_M_, SCALE_C_
MPU = LENGTH_M * SCALE_C / SZ[2]                      # metres per model unit in game
HOP_K, HOP_HMAX, G = 0.35, 1.0, 9.81
T_CROUCH, T_LAND = HOP_TAKEOFF, 0.26                 # the landing: the legs fold, the rear drops (real time)


def hop_plan(v):
    L = v * 0.6
    for _ in range(40):
        h = min(HOP_K * L, HOP_HMAX)
        tf = 2 * math.sqrt(2 * h / G)
        L = v * (T_CROUCH + tf + T_LAND)
    return L, h, tf


LIFT_END = float(_lift[int(round(HOP_TAKEOFF * FPS))])      # height at the takeoff (legs straight), model units
PUSH_END = float(_push[int(round(HOP_TAKEOFF * FPS))])      # forward reach of the push at the takeoff


T_LAUNCH, T_ARRIVE = 0.12, 0.12
VFPS = 30                                             # frames per second of the game-hop renders
PITCH_K, PITCH_MAX = 0.3, 12.0


def flight_clip_t(sf, tf):
    """Clip time for sf seconds into a flight of tf: launch and arrival at their real speed, the cruise stretched."""
    ta, tr = min(T_LAUNCH, tf / 2.5), min(T_ARRIVE, tf / 2.5)
    if sf < ta:
        return HOP_TAKEOFF + 0.12 * sf / ta
    if sf < tf - tr:
        return 0.32 + 0.06 * (sf - ta) / max(tf - ta - tr, 1e-6)
    return 0.38 + 0.12 * min(1.0, (sf - tf + tr) / tr)


def flight_pitch(sf, tf, L, h, pm):
    """Pitch (Root rx, nose up negative) following the flight path, faded in on the launch and out on the arrival."""
    u = sf / tf
    ang = math.degrees(math.atan2(-LIFT_END * MPU + 4 * h * (1 - 2 * u), max(L - pm, 1e-3)))
    ta, tr = min(T_LAUNCH, tf / 2.5), min(T_ARRIVE, tf / 2.5)
    sm = lambda x_: x_ * x_ * (3 - 2 * x_)  # noqa: E731
    fade = sm(min(1.0, sf / ta)) * (1 - sm(min(1.0, max(0.0, (sf - tf + tr) / tr))))
    return float(np.clip(-PITCH_K * ang, -PITCH_MAX, PITCH_MAX)) * fade


T_FOLD = 0.06                                         # the legs fold in 0.06 s after the touchdown, then the rear drops


def land_clip_t(sl, tl):
    """Clip time for sl seconds into a landing of tl: the fold (0.50-0.55) at its own speed, the rear drop after."""
    if sl < T_FOLD:
        return HOP_LAND + 0.05 * sl / T_FOLD
    return 0.55 + 0.05 * min(1.0, (sl - T_FOLD) / max(tl - T_FOLD, 1e-6))


def game_hop_frames(v, hops=2):
    """Per frame: (clip time, height m, visual forward position m, on the ground, flight pitch). Hops in a row skip
    most of the crouch and settle (the landing becomes the next crouch)."""
    L, h, tf = hop_plan(v)
    pm = PUSH_END * MPU
    out, x0 = [], 0.0
    for n in range(hops):
        tc = 0.09 if n == 0 else 0.04                  # crouch part (the push is 0.11 s)
        tl = T_LAND if n == hops - 1 else 0.16
        t, total = 0.0, tc + 0.11 + tf + tl
        while t < total - 1e-6:
            if t < tc:
                ct, y, x, gnd, rx = 0.09 * t / tc, 0.0, 0.0, True, 0.0
            elif t < tc + 0.11:
                ct, y, x, gnd, rx = 0.09 + 0.11 * (t - tc) / 0.11, 0.0, 0.0, True, 0.0
            elif t < tc + 0.11 + tf:
                sf = t - tc - 0.11
                u = sf / tf
                ct = flight_clip_t(sf, tf)
                y = LIFT_END * MPU * (1 - u) + 4 * h * u * (1 - u)
                x, gnd, rx = pm + (L - pm) * u, False, flight_pitch(sf, tf, L, h, pm)
            else:
                ct, y, x, gnd, rx = land_clip_t(t - tc - 0.11 - tf, tl), 0.0, L, True, 0.0
            out.append((ct, y, x0 + x, gnd, rx))
            t += 1 / VFPS
        x0 += L
    return out, (L, h, tf, tc + 0.11 + tf + T_LAND)


def game_pose(ct, y_m, z_m, rx=0.0, onground=True):
    fr = min(int(round(ct * FPS)), CLIPS["hop"]["frames"] - 1)
    pose = dict(sample("hop", fr))
    pose[("Root", "rx")] = pose.get(("Root", "rx"), 0.0) + rx
    V = skin(pose)
    lift = _lift[fr] if ((onground and ct <= HOP_TAKEOFF) or ct >= 0.38) else 0.0
    push = _push[fr] if (onground and ct <= HOP_TAKEOFF) else 0.0      # in the air the flight carries it
    return V + np.array([0.0, y_m / MPU + lift, z_m / MPU + push])


GROUND = lo[1]
ground_qa = {}
for vname, v in (("walk", 1.2), ("run", 4.5)):
    frames_, (L, h, tf, period) = game_hop_frames(v)
    worst_pen, worst_slide, prev = 0.0, 0.0, None
    for fi, (ct, y_m, z_m, contact, rx_) in enumerate(frames_):
        V = game_pose(ct, y_m, z_m, rx_, contact)
        worst_pen = max(worst_pen, (GROUND - V[:, 1].min()) * MPU)
        if "--groundlog" in args and (GROUND - V[:, 1].min()) * MPU > 0.008:
            lowv = V[:, 1].argmin()
            print(f"  {vname} f{fi:02d} ct {ct:.3f} y {y_m:.2f} contact {int(contact)} sink {100 * (GROUND - V[lowv, 1]) * MPU:5.1f} cm "
                  f"lowest {NAMES[WF[lowv].argmax()]}")
        low = V[:, 1] < GROUND + 0.01 / MPU
        if prev is not None and contact and prev[2]:
            both = low & prev[1]
            if both.any():
                d = np.linalg.norm((V[both] - prev[0][both])[:, [0, 2]], axis=1) * MPU * VFPS
                worst_slide = max(worst_slide, float(np.percentile(d, 90)))
                if "--groundlog" in args and vname == "walk" and np.percentile(d, 90) > 0.2:
                    print(f"  slide f{fi} ct {ct:.3f} n {int(both.sum())} p90 {np.percentile(d, 90):.2f} m/s max-bone "
                          f"{NAMES[WF[np.nonzero(both)[0][d.argmax()]].argmax()]}")
        prev = (V, low, contact)
    ground_qa[vname] = {"speed_mps": v, "hop_m": round(L, 2), "height_m": round(h, 2), "flight_s": round(tf, 2),
                        "period_s": round(period, 2), "max_sink_cm": round(100 * worst_pen, 1),
                        "contact_slide_mps_p90": round(worst_slide, 3)}
print("GROUND", json.dumps(ground_qa))

# ---------------------------------------------------------------- export
lines = ["RIG1", f"vertices {nv}", "phases 0", "tip 0", f"bones {len(NAMES)}"]
for i, n in enumerate(NAMES):
    h = HEAD[i]
    lines.append(f"{n} {NAMES[PARENT[i]] if PARENT[i] >= 0 else '-'} {h[0]:.5f} {h[1]:.5f} {h[2]:.5f}")
lines.append(f"weights {nv}")
for v in range(nv):
    lines.append(" ".join(f"{bi} {WF[v, bi]:.4f}" for bi in order[v] if WF[v, bi] > 0) or "0 1.0000")
for name, c in CLIPS.items():
    lines.append(f"clip {name} {c['frames']} {FPS} {1 if c['loop'] else 0}")
    for (b, ch), v in c["keys"].items():
        lines.append(f"key {b} {ch} " + " ".join(f"{val:.4f}" for val in v))
    lines.append("endclip")
out_rig.write_text("\n".join(lines) + "\n")
(out_rig.parent / (out_rig.stem + "_qa.json")).write_text(json.dumps(qa, indent=1))
print("rig written", out_rig)

if "--poses" in args:
    # quick check of single channels: --poses "HindL:rx:30;ShinL:rx:30" (each pose: comma-separated bone:channel:value)
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.collections import PolyCollection
    poses = [pp for pp in opt("--poses", "").split(";") if pp]
    fig, axs = plt.subplots(2, len(poses) + 1, figsize=(2.6 * (len(poses) + 1), 5))
    for k, spec in enumerate([""] + poses):
        pose = {}
        if spec.startswith("clip="):                 # a frame of a clip, then overrides: clip=hop@8,HandL:rx:-30
            first, _, spec = spec.partition(",")
            cn, cf = first[5:].split("@")
            pose = dict(sample(cn, int(cf)))
            for item in filter(None, spec.split(",")):
                b_, c_, v_ = item.split(":")
                pose[(b_, c_)] = float(v_)
                if b_.endswith("L"):
                    pose[(b_[:-1] + "R", c_)] = (-1 if c_ in ("ry", "rz", "px") else 1) * float(v_)
            spec = ""
        for item in filter(None, spec.split(",")):
            b_, c_, v_ = item.split(":")
            pose[(b_, c_)] = float(v_)
        V = skin(pose)
        for row, (a, b, dd) in enumerate([(2, 1, 0), (0, 1, 2)]):
            ax = axs[row, k]
            ax.scatter(V[:, a], V[:, b], s=0.5, c=[[0.2, 0.3, 0.2]])
            sel = WF.argmax(1)
            for bn, colr in (("HindL", "red"), ("ShinL", "orange"), ("FootL", "yellow"), ("ToeL", "cyan"), ("ArmL", "blue"), ("HandL", "magenta"),
                             ("HindR", "darkred"), ("ShinR", "chocolate"), ("FootR", "gold"), ("ToeR", "teal"), ("ArmR", "navy"), ("HandR", "purple"),
                             ("Body", "lightgray"), ("Chest", "silver")):
                m = sel == IX[bn]
                ax.scatter(V[m, a], V[m, b], s=1.5, c=colr)
            ax.set_aspect("equal"); ax.set_xticks([]); ax.set_yticks([])
            if row == 0: ax.set_title(spec or "rest", fontsize=7)
    fig.tight_layout()
    fig.savefig(opt("--posefile", "poses.png"), dpi=opt("--posedpi", 70, int))
    sys.exit(0)

if "--mpl" in args:
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.collections import PolyCollection
    from PIL import Image
    tex = np.asarray(Image.open(tam.with_suffix(".png")).convert("RGB"), dtype=np.float32) / 255
    th, tw = tex.shape[:2]
    cuv = UV[IDX].mean(1)
    col = tex[np.clip(((1 - cuv[:, 1]) * (th - 1)).astype(int), 0, th - 1), np.clip((cuv[:, 0] * (tw - 1)).astype(int), 0, tw - 1)]
    out = Path(opt("--mpl", "frog_mpl"))
    out.mkdir(parents=True, exist_ok=True)
    for name in CLIPS:
        frames = list(range(0, CLIPS[name]["frames"], max(1, CLIPS[name]["frames"] // 10)))[:10]
        fig, axs = plt.subplots(2, len(frames), figsize=(2.2 * len(frames), 4.6))
        for k, fr in enumerate(frames):
            V = skin(sample(name, fr))
            tri = V[IDX]
            for row, (a, b, dd) in enumerate([(2, 1, 0), (0, 1, 2)]):
                ax = axs[row, k]
                nn = normals(V)
                order_ = np.argsort(-tri[:, :, dd].mean(1) if row == 0 else tri[:, :, dd].mean(1))
                shade = 0.45 + 0.55 * np.abs(nn[:, dd])
                ax.add_collection(PolyCollection(tri[order_][:, :, [a, b]], facecolors=np.clip(col[order_] * shade[order_, None], 0, 1), edgecolors="none"))
                ax.set_xlim(lo[a] - 0.3, hi[a] + 0.3); ax.set_ylim(lo[1] - 0.05, hi[1] + 0.55)
                ax.set_aspect("equal"); ax.set_xticks([]); ax.set_yticks([]); ax.set_facecolor((0.55, 0.62, 0.55))
                if row == 0: ax.set_title(f"{name} {fr}", fontsize=8)
        fig.tight_layout()
        fig.savefig(out / f"frog_{name}.png", dpi=70)
        plt.close(fig)
    print("mpl previews", out)

if not preview:
    sys.exit(0)

# ---------------------------------------------------------------- preview (Blender)
import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

preview.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)


def to_b(v):
    return np.c_[-v[:, 0], -v[:, 2], v[:, 1]]


me = bpy.data.meshes.new("frog")
me.from_pydata(to_b(P).tolist(), [], IDX[:, [0, 2, 1]].tolist())
uvl = me.uv_layers.new()
loops = np.zeros(len(me.loops), int)
me.loops.foreach_get("vertex_index", loops)
uvl.data.foreach_set("uv", UV[loops].astype(np.float32).ravel())
for p in me.polygons:
    p.use_smooth = True
ob = bpy.data.objects.new("frog", me)
bpy.context.scene.collection.objects.link(ob)
mat = bpy.data.materials.new("m")
mat.use_nodes = True
tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
tex.image = bpy.data.images.load(str(tam.with_suffix(".png")))
mat.node_tree.links.new(tex.outputs["Color"], mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
me.materials.append(mat)

scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.render.resolution_x = scene.render.resolution_y = size
sh = scene.display.shading
sh.light, sh.color_type = "STUDIO", "TEXTURE"
sh.background_type = "VIEWPORT"
sh.background_color = (0.55, 0.62, 0.55)
scene.world = bpy.data.worlds.new("w")
cam_data = bpy.data.cameras.new("cam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = max(SZ) * 2.2
cam = bpy.data.objects.new("cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
centre = Vector((0, -(lo[2] + hi[2]) / 2, (lo[1] + hi[1]) / 2 + H * 0.35))

views = {"side": (90, 5), "front34": (35, 10), "back34": (215, 15)}
for name in ([] if "--gameonly" in args else CLIPS):
    frames = list(range(CLIPS[name]["frames"]))
    step = (FPS // 30) * (1 if len(frames) <= 24 * FPS // 30 else 2)
    frames = frames[::step]
    for vname, (az, el) in views.items():
        a, e = math.radians(az), math.radians(el)
        dvec = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
        cam.location = centre + dvec * 5
        cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
        vdir = preview / f"frog_{name}_{vname}"
        vdir.mkdir(exist_ok=True)
        for k, fr in enumerate(frames):
            V = to_b(skin(sample(name, fr)))
            me.vertices.foreach_set("co", V.astype(np.float32).ravel())
            me.update()
            scene.render.filepath = str(vdir / f"f{k:03d}.png")
            bpy.ops.render.render(write_still=True)
        cols = min(len(frames), 9)
        rows = (len(frames) + cols - 1) // cols
        subprocess.run([FFMPEG, "-y", "-loglevel", "error", "-i", str(vdir / "f%03d.png"),
                        "-vf", f"scale=200:-1,tile={cols}x{rows}", "-frames:v", "1", str(preview / f"sheet_{name}_{vname}.png")], check=False)
        subprocess.run([FFMPEG, "-y", "-loglevel", "error", "-framerate", str(FPS // step), "-i", str(vdir / "f%03d.png"),
                        "-c:v", "libx264", "-pix_fmt", "yuv420p", str(preview / f"frog_{name}_{vname}.mp4")], check=False)
    print("preview", name)

# the game hops over a chequered ground: a camera following the frog (side, 3/4 front, low 3/4 back at ground level)
# and a wide side view of the whole run
n_ch = 16
img = bpy.data.images.new("ground", 512, 512)
cy, cx = np.mgrid[0:512, 0:512]
chk = (((cx // 32) + (cy // 32)) % 2).astype(np.float32)
px = np.stack([0.30 + 0.12 * chk, 0.36 + 0.12 * chk, 0.26 + 0.10 * chk, np.ones_like(chk)], -1)
img.pixels.foreach_set(px.ravel())
gm = bpy.data.materials.new("g")
gm.use_nodes = True
gt = gm.node_tree.nodes.new("ShaderNodeTexImage")
gt.image = img
gm.node_tree.links.new(gt.outputs["Color"], gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
half = 40.0 / MPU
gme = bpy.data.meshes.new("ground")
hw = 1.5 * max(SZ)                                    # a strip as wide as a few frogs: seen edge-on from the side
gme.from_pydata([(-hw, half, GROUND), (hw, half, GROUND), (hw, -half * 3, GROUND), (-hw, -half * 3, GROUND)], [], [(0, 1, 2, 3)])
guv = gme.uv_layers.new()
rep_ = 2 * half * MPU / 0.5                            # one checker tile = 0.5 m... 16 tiles per texture
for li, (uu, vv) in enumerate([(0, 0), (1, 0), (1, 2), (0, 2)]):
    guv.data[li].uv = (uu * rep_ / n_ch, vv * rep_ / n_ch)
gme.materials.append(gm)
gob = bpy.data.objects.new("ground", gme)
scene.collection.objects.link(gob)
cam_data.ortho_scale = max(SZ) * 3.0
sh.background_color = (0.62, 0.72, 0.85)              # sky
for vname, v in (("walk", 1.2), ("run", 4.5)):
    frames_, (L, h, tf, period) = game_hop_frames(v)
    poses = [game_pose(ct, y_m, z_m, rx_, g_) for ct, y_m, z_m, g_, rx_ in frames_]
    for view, (az, el) in {"side": (90, 4), "front34": (35, 10), "back34low": (215, 4)}.items():
        a_, e_ = math.radians(az), math.radians(el)
        dvec = Vector((math.sin(a_) * math.cos(e_), -math.cos(a_) * math.cos(e_), math.sin(e_)))
        vdir = preview / f"frog_hop{vname}_{view}"
        vdir.mkdir(exist_ok=True)
        for k_, V in enumerate(poses):
            me.vertices.foreach_set("co", to_b(V).astype(np.float32).ravel())
            me.update()
            zc, yc = V[:, 2].mean(), V[:, 1].mean()
            # frame both the frog and the ground under it (high hops: a wider view)
            cam_data.ortho_scale = max(3.0 * max(SZ), 1.3 * (yc - GROUND) + 2.2 * max(SZ))
            c_ = Vector((0, -zc, (GROUND + yc) / 2 + 0.25 * H))
            cam.location = c_ + dvec * 8
            cam.rotation_euler = (c_ - cam.location).to_track_quat("-Z", "Y").to_euler()
            scene.render.filepath = str(vdir / f"f{k_:03d}.png")
            bpy.ops.render.render(write_still=True)
        subprocess.run([FFMPEG, "-y", "-loglevel", "error", "-framerate", str(VFPS), "-i", str(vdir / "f%03d.png"),
                        "-c:v", "libx264", "-pix_fmt", "yuv420p", str(preview / f"frog_hop{vname}_{view}.mp4")], check=False)
        subprocess.run([FFMPEG, "-y", "-loglevel", "error", "-i", str(vdir / "f%03d.png"),
                        "-vf", f"select=not(mod(n\,2)),scale=220:-1,tile=8x{(len(poses) + 15) // 16}", "-frames:v", "1",
                        str(preview / f"sheet_hop{vname}_{view}.png")], check=False)
    # wide fixed side view of the two hops
    span = 2 * L / MPU
    old = cam_data.ortho_scale
    cam_data.ortho_scale = span + max(SZ) * 3
    vdir = preview / f"frog_hop{vname}_wide"
    vdir.mkdir(exist_ok=True)
    c_ = Vector((0, -(span / 2 + (lo[2] + hi[2]) / 2), GROUND + span * 0.15))
    cam.location = c_ + Vector((1, 0, 0.02)) * 8 * span
    cam_data.clip_end = 20 * span + 100
    cam.rotation_euler = (c_ - cam.location).to_track_quat("-Z", "Y").to_euler()
    for k_, V in enumerate(poses):
        me.vertices.foreach_set("co", to_b(V).astype(np.float32).ravel())
        me.update()
        scene.render.filepath = str(vdir / f"f{k_:03d}.png")
        bpy.ops.render.render(write_still=True)
    subprocess.run([FFMPEG, "-y", "-loglevel", "error", "-framerate", str(VFPS), "-i", str(vdir / "f%03d.png"),
                    "-c:v", "libx264", "-pix_fmt", "yuv420p", str(preview / f"frog_hop{vname}_wide.mp4")], check=False)
    cam_data.ortho_scale = old
    print("preview game hop", vname, round(L, 2), "m")
print("done")
