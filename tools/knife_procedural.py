"""The Fox Fang as a clean low-poly model, built from code (the generated one could not be brought down to the
vanilla knife's budget, 2 x 232 triangles, without losing its blade): a curved, single-edged fang blade with a
spine and a bevelled edge, a small bronze guard, a haft wrapped in russet fox fur with darker bands, and a white
fur tuft at the pommel. Painted texture atlas (copper-sheened steel brighter towards the edge, bronze, fur).
Frame as the other weapons: length along +y from -0.5 (pommel) to 0.5 (tip), blade width along x, thickness z.
"""
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
import trimesh

TEX = 256
# atlas regions (u0, v0, u1, v1) in 0..1, v up
R_BLADE = (0.0, 0.0, 0.5, 1.0)
R_GUARD = (0.5, 0.75, 1.0, 1.0)
R_FUR = (0.5, 0.25, 1.0, 0.75)
R_TUFT = (0.5, 0.0, 1.0, 0.25)


def atlas(rng):
    img = Image.new("RGB", (TEX, TEX))
    px = np.zeros((TEX, TEX, 3), np.float32)
    v = np.linspace(1, 0, TEX)[:, None] * np.ones((1, TEX))      # image rows go down; v up
    u = np.ones((TEX, 1)) * np.linspace(0, 1, TEX)[None, :]
    noise = rng.normal(0, 1, (TEX, TEX)).astype(np.float32)
    # blade: u across (0 spine .. 0.5 edge), copper-sheened steel, brighter at the edge, faint lengthwise grain
    t = np.clip(u / 0.5, 0, 1)
    steel = np.stack([0.50 + 0.25 * t, 0.40 + 0.22 * t, 0.36 + 0.2 * t], -1)
    grain = np.repeat(rng.normal(0, 0.03, (1, TEX)), TEX, 0)
    blade = steel + grain[..., None] + 0.02 * noise[..., None]
    # fur: russet with darker wrapping bands along v, fine noise
    bands = 0.82 + 0.18 * np.sign(np.sin(v * np.pi * 14))
    fur = np.stack([0.78, 0.36, 0.12], -1) * (bands[..., None] * (1 + 0.15 * noise[..., None]))
    bronze = np.array([0.62, 0.45, 0.22]) * (1 + 0.08 * noise[..., None])
    tuft = np.array([0.93, 0.9, 0.85]) * (1 + 0.06 * noise[..., None])
    left = u < 0.5
    px[left] = blade[left]
    right = ~left
    px[right & (v >= 0.75)] = bronze[right & (v >= 0.75)]
    m = right & (v >= 0.25) & (v < 0.75)
    px[m] = fur[m]
    m = right & (v < 0.25)
    px[m] = tuft[m]
    img = Image.fromarray((np.clip(px, 0, 1) * 255).astype("uint8")).filter(ImageFilter.GaussianBlur(0.6))
    return img


def uv_in(region, a, b):
    u0, v0, u1, v1 = region
    return [u0 + (u1 - u0) * a, v0 + (v1 - v0) * b]


def build(rng=None):
    rng = rng or np.random.default_rng(3)
    verts, uvs, faces = [], [], []

    def tri(p, q, r, tp, tq, tr):
        i = len(verts)
        verts.extend([p, q, r])
        uvs.extend([tp, tq, tr])
        faces.append([i, i + 1, i + 2])

    def quad(p, q, r, s, tp, tq, tr, ts):
        tri(p, q, r, tp, tq, tr)
        tri(p, r, s, tp, tr, ts)

    # --- blade: y from -0.06 to 0.5; spine on +x (the back), edge on -x; curving towards the edge at the tip
    ys = np.linspace(-0.06, 0.5, 11)
    secs = []
    for k, y in enumerate(ys):
        s = (y + 0.06) / 0.56
        width = 0.085 * (1 - s ** 1.6) + 0.004
        curve = -0.035 * s ** 2.2                             # the fang bends towards the edge
        spine_x = 0.03 + curve
        edge_x = spine_x - width
        thick = 0.012 * (1 - s) + 0.002
        spine_f = np.array([spine_x, y, thick])
        spine_b = np.array([spine_x, y, -thick])
        bevel_f = np.array([spine_x - width * 0.55, y, thick * 0.6])
        bevel_b = np.array([spine_x - width * 0.55, y, -thick * 0.6])
        edge = np.array([edge_x, y, 0.0])
        secs.append((spine_f, bevel_f, edge, bevel_b, spine_b, s))
    for a, b in zip(secs[:-1], secs[1:]):
        sa, sb = a[5], b[5]
        rings = [(0, 1, 0.0, 0.25), (1, 2, 0.25, 0.5), (2, 3, 0.5, 0.25), (3, 4, 0.25, 0.0), (4, 0, 0.0, 0.0)]
        for i0, i1, u0, u1 in rings:
            quad(a[i0], a[i1], b[i1], b[i0],
                 uv_in(R_BLADE, u0 * 2, sa), uv_in(R_BLADE, u1 * 2, sa), uv_in(R_BLADE, u1 * 2, sb), uv_in(R_BLADE, u0 * 2, sb))
    # --- guard: a bronze bar across the blade root, y -0.085..-0.06
    gx0, gx1, gz, gy0, gy1 = -0.09, 0.075, 0.03, -0.085, -0.06
    box = [np.array(p) for p in [(gx0, gy0, -gz), (gx1, gy0, -gz), (gx1, gy1, -gz), (gx0, gy1, -gz),
                                  (gx0, gy0, gz), (gx1, gy0, gz), (gx1, gy1, gz), (gx0, gy1, gz)]]
    for f in [(0, 1, 2, 3), (5, 4, 7, 6), (4, 0, 3, 7), (1, 5, 6, 2), (3, 2, 6, 7), (4, 5, 1, 0)]:
        quad(*[box[i] for i in f], *[uv_in(R_GUARD, a, b) for a, b in ((0, 0), (1, 0), (1, 1), (0, 1))])
    # --- haft: an octagonal fur wrap, y -0.45..-0.085, slightly bulging in the middle
    n = 8
    hy = np.linspace(-0.45, -0.085, 6)
    rad = lambda y: 0.034 + 0.008 * np.sin(np.pi * (y + 0.45) / 0.365)
    ring = lambda y: [np.array([np.cos(a) * rad(y) - 0.01, y, np.sin(a) * rad(y)]) for a in np.linspace(0, 2 * np.pi, n, endpoint=False)]
    rings = [ring(y) for y in hy]
    for r, (ra, rb) in enumerate(zip(rings[:-1], rings[1:])):
        for i in range(n):
            j = (i + 1) % n
            quad(ra[i], ra[j], rb[j], rb[i], uv_in(R_FUR, i / n, r / 5), uv_in(R_FUR, (i + 1) / n, r / 5),
                 uv_in(R_FUR, (i + 1) / n, (r + 1) / 5), uv_in(R_FUR, i / n, (r + 1) / 5))
    # --- pommel: a white tuft narrowing to a soft point at y -0.5
    tip = np.array([-0.01, -0.5, 0.0])
    low = rings[0]
    for i in range(n):
        j = (i + 1) % n
        tri(low[j], low[i], tip, uv_in(R_TUFT, (i + 1) / n, 1), uv_in(R_TUFT, i / n, 1), uv_in(R_TUFT, 0.5, 0))
    pos = np.array(verts, np.float32)
    f = np.array(faces, np.int32)
    # both faces of every triangle (about 400 in all, under the 460 budget): no winding to get wrong, and each side
    # gets its own flat normal
    uv = np.array(uvs, np.float32)
    f = np.vstack([f, f[:, [0, 2, 1]] + len(pos)])
    pos = np.vstack([pos, pos])
    uvs = np.vstack([uv, uv])
    mesh = trimesh.Trimesh(pos, f, process=False, visual=trimesh.visual.TextureVisuals(uv=uvs, image=atlas(rng)))
    return mesh


if __name__ == "__main__":
    m = build()
    print(len(m.faces), "tris", m.bounds)
