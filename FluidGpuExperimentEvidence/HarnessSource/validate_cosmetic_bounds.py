"""Randomized geometry evidence for cosmetic AABB culling; not Unity execution.

Run: python FluidGpuExperimentEvidence/HarnessSource/validate_cosmetic_bounds.py
If signed distance to a solid polygon is below a nonnegative cosmetic radius,
the query is inside that polygon or within radius of an edge. Both cases lie
inside the polygon AABB expanded by radius. Rotation and nonuniform/negative
scale are applied before forming the world AABB, matching runtime geometry.
"""
import hashlib
import json
import math
import random
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RNG = random.Random(20260930)


def rotate(p, a):
    c, s = math.cos(a), math.sin(a)
    return p[0] * c - p[1] * s, p[0] * s + p[1] * c


def clearance(p, polygon):
    inside = False
    best = math.inf
    a = polygon[-1]
    for b in polygon:
        dx, dy = b[0] - a[0], b[1] - a[1]
        denominator = dx * dx + dy * dy
        t = min(1, max(0, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / max(1e-12, denominator)))
        best = min(best, math.hypot(p[0] - a[0] - dx * t, p[1] - a[1] - dy * t))
        if ((a[1] > p[1]) != (b[1] > p[1])) and p[0] < (b[0] - a[0]) * (p[1] - a[1]) / (b[1] - a[1]) + a[0]:
            inside = not inside
        a = b
    return -best if inside else best


def run():
    queries = hits = rejected = 0
    for _ in range(6000):
        count = RNG.randrange(3, 16)
        # Ordered radial points include both convex and concave finite polygons.
        polygon = [(math.cos(i * math.tau / count) * RNG.uniform(.2, 2),
                    math.sin(i * math.tau / count) * RNG.uniform(.2, 2)) for i in range(count)]
        sx, sy = RNG.uniform(-3, 3), RNG.uniform(-3, 3)
        scaled = [(p[0] * sx, p[1] * sy) for p in polygon]
        origin, angle = (RNG.uniform(-30, 30), RNG.uniform(-30, 30)), RNG.uniform(-math.tau * 3, math.tau * 3)
        world = [(origin[0] + rotate(p, angle)[0], origin[1] + rotate(p, angle)[1]) for p in scaled]
        low = tuple(min(p[i] for p in world) for i in range(2))
        high = tuple(max(p[i] for p in world) for i in range(2))
        padding = .0001 + max(map(abs, (*low, *high))) * .0000004
        for sample in range(24):
            radius = RNG.uniform(.001, .2)
            if sample < 12:
                vertex = world[sample % len(world)]
                query = (vertex[0] + RNG.uniform(-radius, radius), vertex[1] + RNG.uniform(-radius, radius))
            else:
                query = (origin[0] + RNG.uniform(-12, 12), origin[1] + RNG.uniform(-12, 12))
            local = rotate((query[0] - origin[0], query[1] - origin[1]), -angle)
            narrow_hit = clearance(local, scaled) < radius
            culled = any(query[i] < low[i] - padding - radius or query[i] > high[i] + padding + radius for i in range(2))
            assert not (narrow_hit and culled), ("false negative", query, radius, origin, angle)
            queries += 1
            hits += narrow_hit
            rejected += culled
    source = ROOT / "Assets/_Project/Features/Bartending/FluidGpuExperiment/Runtime/FluidExperimentEffects.cs"
    result = {"status": "PASS", "kind": "CPU mathematical geometry evidence, not Unity physics/render execution",
              "seed": 20260930, "profiles": 6000, "queries": queries, "narrow_hits": hits,
              "broadphase_rejections": rejected, "false_negatives": 0,
              "source_sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
              "coverage": "Nonuniform and negative scale, positive/negative rotations, translation, concave profiles and finite disk radii",
              "limitation": "Finite game-scale mathematical inputs; does not measure speed or replace runtime effects regression"}
    out = ROOT / "FluidGpuExperimentEvidence/cosmetic-bounds-static"
    out.mkdir(parents=True, exist_ok=True)
    (out / "results.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    run()
