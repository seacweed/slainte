"""CPU geometry oracle for conservative D/E vertex-stage hull pruning, not a GPU benchmark."""
import argparse
import json
import math
from pathlib import Path
import random
import re


ROOT = Path(__file__).resolve().parents[2]
PROFILES = ROOT / "Assets/_Project/Features/Bartending/FluidGpuExperiment/Data/CollisionProfiles"


def cross(a, b):
    return a[0] * b[1] - a[1] * b[0]


def subtract(a, b):
    return a[0] - b[0], a[1] - b[1]


def intersects_bounds(low, high, bounds):
    return not (high[0] < bounds[0] or high[1] < bounds[1]
                or low[0] > bounds[2] or low[1] > bounds[3])


def bounds_of(points):
    return (min(p[0] for p in points), min(p[1] for p in points),
            max(p[0] for p in points), max(p[1] for p in points))


def original_occluded(pixel, center, points, bounds):
    low = min(pixel[0], center[0]), min(pixel[1], center[1])
    high = max(pixel[0], center[0]), max(pixel[1], center[1])
    if not intersects_bounds(low, high, bounds):
        return False
    inside = False
    a = points[-1]
    travel = subtract(pixel, center)
    for b in points:
        edge = subtract(b, a)
        denominator = cross(travel, edge)
        if abs(denominator) > .00000001:
            offset = subtract(a, center)
            t, u = cross(offset, edge) / denominator, cross(offset, travel) / denominator
            if .00001 < t <= 1 and 0 <= u <= 1:
                return True
        if (a[1] > pixel[1]) != (b[1] > pixel[1]):
            crossing = (b[0] - a[0]) * (pixel[1] - a[1]) / (b[1] - a[1]) + a[0]
            if pixel[0] < crossing:
                inside = not inside
        a = b
    return inside


def candidate(center, extent, points, bounds):
    padding = .00001 * max(1, *extent)
    low = tuple(center[i] - extent[i] - padding for i in range(2))
    high = tuple(center[i] + extent[i] + padding for i in range(2))
    if not intersects_bounds(low, high, bounds):
        return False
    inside = False
    a = points[-1]
    for b in points:
        edge_low = min(a[0], b[0]), min(a[1], b[1])
        edge_high = max(a[0], b[0]), max(a[1], b[1])
        if not (edge_high[0] < low[0] or edge_high[1] < low[1]
                or edge_low[0] > high[0] or edge_low[1] > high[1]):
            return True
        if (a[1] > center[1]) != (b[1] > center[1]):
            crossing = (b[0] - a[0]) * (center[1] - a[1]) / (b[1] - a[1]) + a[0]
            if center[0] < crossing:
                inside = not inside
        a = b
    return inside


def profile_hulls(name):
    text = (PROFILES / (name + ".asset")).read_text(encoding="utf-8-sig")
    solids = text.split("  solids:", 1)[1].split("  lid:", 1)[0]
    return [[(float(x), float(y)) for x, y in re.findall(r"\{x: ([^,]+), y: ([^}]+)\}", block)]
            for block in solids.split("  - points:")[1:]]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    rng = random.Random(20260930)
    checked = rejected = covered = splats = 0
    profiles = {}
    for name in ("Glass_highball", "Glass_hurricane", "Spoon", "Ice"):
        hulls = profile_hulls(name)
        profile_checks = profile_rejected = 0
        for points in hulls:
            if len(points) < 3:
                continue
            local_bounds = bounds_of(points)
            for sample in range(1600):
                # Authored concave shapes, rotated/transformed bodies, broad and thin splats,
                # interiors, exterior drops and samples concentrated around authored vertices.
                rotation = rng.uniform(-math.pi, math.pi)
                cosine, sine = math.cos(rotation), math.sin(rotation)
                translation = rng.uniform(-10, 10), rng.uniform(-10, 10)
                transform = lambda p: (p[0] * cosine - p[1] * sine + translation[0],
                                       p[0] * sine + p[1] * cosine + translation[1])
                world = [transform(p) for p in points]
                bounds = bounds_of(world)
                if sample % 4 == 0:
                    vertex = points[sample % len(points)]
                    local_center = vertex[0] + rng.uniform(-.12, .12), vertex[1] + rng.uniform(-.12, .12)
                else:
                    local_center = (rng.uniform(local_bounds[0] - .3, local_bounds[2] + .3),
                                    rng.uniform(local_bounds[1] - .3, local_bounds[3] + .3))
                center = transform(local_center)
                angle = rng.uniform(-math.pi, math.pi)
                direction, side = (math.cos(angle), math.sin(angle)), (-math.sin(angle), math.cos(angle))
                major, minor = 10 ** rng.uniform(-2.2, -.2), 10 ** rng.uniform(-2.2, -.2)
                extent = tuple(abs(direction[i]) * major + abs(side[i]) * minor for i in range(2))
                keep = candidate(center, extent, world, bounds)
                splats += 1
                if not keep:
                    rejected += 1
                    profile_rejected += 1
                samples = [(0, 0), (-1, -1), (-1, 1), (1, -1), (1, 1)]
                samples += [(rng.uniform(-1, 1), rng.uniform(-1, 1)) for _ in range(12)]
                for x, y in samples:
                    pixel = tuple(center[i] + direction[i] * x * major + side[i] * y * minor for i in range(2))
                    original = original_occluded(pixel, center, world, bounds)
                    optimized = keep and original
                    checked += 1
                    profile_checks += 1
                    covered += int(original)
                    if original != optimized:
                        raise AssertionError((name, sample, pixel, center, extent))
        profiles[name] = {"hulls": len(hulls), "pixel_queries": profile_checks, "excluded_splats": profile_rejected}
    result = {"status": "PASS", "kind": "CPU geometry oracle; GPU images and timings require Unity validation",
              "seed": 20260930, "splats": splats, "pixel_queries": checked,
              "original_occluded_pixels": covered, "excluded_hull_splats": rejected,
              "false_negative_masks": 0, "profiles": profiles}
    if args.output:
        args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
