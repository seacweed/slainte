"""Mathematical evidence for swept-bound conservatism, not a Unity/GPU physics test.

Run: python FluidGpuExperimentEvidence/HarnessSource/validate_swept_arc_bounds.py
The random seed, tested source hashes, numerical scope and results are saved as JSON.
For p(t)=o0+t*(o1-o0)+R(a0+t*theta)*v, |p''(t)|=|v|*theta**2.
The linear interpolation error is at most max|p''|*t*(1-t)/2 <= r*theta**2/8.
Linear translation adds no curvature. A segment is a convex combination of its
endpoints, so an AABB covering both endpoint paths also covers the whole segment.
For |theta|>1 the implementation retains the translated full-radius envelope.
Random samples supplement this argument; they do not prove shader/GPU equivalence.
"""
import hashlib
import json
import math
import random
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "FluidGpuExperimentEvidence/swept-arc-static"
SEED = 20260930
EPS = 0.0001


def f32(x):
    return struct.unpack("f", struct.pack("f", x))[0]


def rotate(v, angle):
    c, s = math.cos(angle), math.sin(angle)
    return (c * v[0] - s * v[1], s * v[0] + c * v[1])


def add(a, b):
    return (a[0] + b[0], a[1] + b[1])


def lerp(a, b, t):
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)


def bounds(a, b, o0, o1, angle, delta, end_a=None, end_b=None, group=False):
    """Reflects current CPU/GPU branches; supplied endpoints support partial births."""
    end_a = end_a or add(o1, rotate(a, angle + delta))
    end_b = end_b or add(o1, rotate(b, angle + delta))
    radius = max(math.hypot(*a), math.hypot(*b))
    if abs(delta) < 1e-6:
        travel = (o1[0] - o0[0], o1[1] - o0[1])
        points = [end_a, end_b, (end_a[0] - travel[0], end_a[1] - travel[1]),
                  (end_b[0] - travel[0], end_b[1] - travel[1])]
        padding = EPS if group else 0
        branch = "legacy_translation"
    elif abs(delta) <= 1:
        points = [end_a, end_b, add(o0, rotate(a, angle)), add(o0, rotate(b, angle))]
        padding = radius * delta * delta / 8 + EPS
        branch = "chord"
    else:
        points = [o0, o1]
        padding = radius + (EPS if group else 0)
        branch = "full_circle"
    low = tuple(min(p[i] for p in points) - padding for i in range(2))
    high = tuple(max(p[i] for p in points) + padding for i in range(2))
    return low, high, branch


def outside(point, low, high):
    return max(low[0] - point[0], low[1] - point[1], point[0] - high[0], point[1] - high[1])


def run():
    rng = random.Random(SEED)
    result = {"seed": SEED, "kind": "CPU mathematical bound evidence; not Unity/GPU execution",
              "proof": __doc__.split("For p(t)=", 1)[1].strip(), "cases": 0, "sampled_points": 0,
              "branches": {}, "max_chord_error_to_bound_for_angles_at_least_1e_4": 0.0,
              "maximum_unpadded_chord_excess_from_double_roundoff": 0.0,
              "float32_partial_birth_cases": 0, "float32_max_outside": 0.0}
    special = [0, 1e-6, -1e-6, .001, -.001, .5, -.5, 1, -1,
               1.000001, -1.000001, math.radians(855), -math.radians(855), math.tau * 5]
    # Signs and nonuniform scale are already baked into localA/B by CPU UploadGeometry.
    for case in range(10000):
        scale = (rng.uniform(-3, 3), rng.uniform(-3, 3))
        a = (rng.uniform(-4, 4) * scale[0], rng.uniform(-4, 4) * scale[1])
        b = (rng.uniform(-4, 4) * scale[0], rng.uniform(-4, 4) * scale[1])
        o0, o1 = (rng.uniform(-20, 20), rng.uniform(-20, 20)), (rng.uniform(-20, 20), rng.uniform(-20, 20))
        angle = rng.uniform(-math.tau * 3, math.tau * 3)
        delta = special[case] if case < len(special) else rng.choice([rng.uniform(-1, 1), rng.uniform(-math.tau * 5, math.tau * 5)])
        low, high, branch = bounds(a, b, o0, o1, angle, delta, group=True)
        result["branches"][branch] = result["branches"].get(branch, 0) + 1
        result["cases"] += 1
        start_a, end_a = add(o0, rotate(a, angle)), add(o1, rotate(a, angle + delta))
        radius = max(math.hypot(*a), math.hypot(*b))
        for sample in range(65):
            t = sample / 64
            pa, pb = add(lerp(o0, o1, t), rotate(a, angle + delta * t)), add(lerp(o0, o1, t), rotate(b, angle + delta * t))
            for p in (pa, pb, lerp(pa, pb, .37)):
                assert outside(p, low, high) <= 1e-10, ("group envelope", case, t, p, low, high)
                result["sampled_points"] += 1
            if 1e-6 <= abs(delta) <= 1 and radius > 0:
                chord = lerp(start_a, end_a, t)
                ratio = math.hypot(pa[0] - chord[0], pa[1] - chord[1]) / (radius * delta * delta / 8)
                if abs(delta) >= 1e-4:
                    result["max_chord_error_to_bound_for_angles_at_least_1e_4"] = max(
                        result["max_chord_error_to_bound_for_angles_at_least_1e_4"], ratio)
                result["maximum_unpadded_chord_excess_from_double_roundoff"] = max(
                    result["maximum_unpadded_chord_excess_from_double_roundoff"],
                    math.hypot(pa[0] - chord[0], pa[1] - chord[1]) - radius * delta * delta / 8)
                # Tiny angles incur subtractive roundoff; the real implementation adds EPS.
                assert math.hypot(pa[0] - chord[0], pa[1] - chord[1]) <= radius * delta * delta / 8 + 1e-10

        # Birth crops [0,1] to [fraction,1], preserving end endpoints and shortening rotation.
        fraction = rng.choice([0, .5, .999, 1, rng.random()])
        partial_o = lerp(o0, o1, fraction)
        partial_angle, partial_delta = angle + delta * fraction, delta * (1 - fraction)
        partial_low, partial_high, partial_branch = bounds(a, b, partial_o, o1, partial_angle, partial_delta,
            end_a=end_a, end_b=add(o1, rotate(b, angle + delta)))
        for sample in range(33):
            t = sample / 32
            for local in (a, b):
                p = add(lerp(partial_o, o1, t), rotate(local, partial_angle + partial_delta * t))
                # The unchanged <1e-6 translation shortcut approximates tiny rotations.
                # Do not falsely attribute its pre-existing approximation to the new arc bound.
                tolerance = radius * abs(partial_delta) + 1e-10 if partial_branch == "legacy_translation" else 1e-10
                assert outside(p, partial_low, partial_high) <= tolerance, ("partial envelope", case, t)
                result["sampled_points"] += 1

        if case < 1200:
            fa, fb, fo0, fo1 = [tuple(map(f32, v)) for v in (a, b, o0, o1)]
            fang, fdelta, frac = f32(angle), f32(delta), f32(fraction)
            end_angle = f32(fang + fdelta)
            ea, eb = tuple(map(f32, add(fo1, rotate(fa, end_angle)))), tuple(map(f32, add(fo1, rotate(fb, end_angle))))
            po = tuple(f32(fo0[i] + f32(f32(fo1[i] - fo0[i]) * frac)) for i in range(2))
            pang, pdelta = f32(fang + f32(fdelta * frac)), f32(fdelta * f32(1 - frac))
            lo, hi, which = bounds(fa, fb, po, fo1, pang, pdelta, ea, eb)
            if which == "chord":
                for sample in range(33):
                    t = f32(sample / 32)
                    for local in (fa, fb):
                        theta = f32(pang + f32(pdelta * t))
                        origin = tuple(f32(po[i] + f32(f32(fo1[i] - po[i]) * t)) for i in range(2))
                        p = tuple(map(f32, add(origin, rotate(local, theta))))
                        error = outside(p, lo, hi)
                        result["float32_max_outside"] = max(result["float32_max_outside"], error)
                        assert error <= 0, ("float32 partial chord", case, error)
                result["float32_partial_birth_cases"] += 1

    # Squaring the near-zero viscosity threshold is not bitwise equivalent at one ULP.
    bits = struct.unpack("I", struct.pack("f", 1e-12))[0]
    d2 = struct.unpack("f", struct.pack("I", bits + 1))[0]
    distance = f32(math.sqrt(d2))
    assert d2 > f32(1e-12) and not distance > f32(1e-6)
    result["viscosity_threshold_counterexample"] = {"squared_distance": d2, "distance": distance,
        "original_distance_test": False, "squared_only_test": True,
        "required_fix": "Retain distance > 1e-6 and weight > 0 after the squared-distance early cull"}
    # Check the compact-support early cull against the original float32 scalar weights,
    # including values on both sides of the rounded h*h boundary. Positive finite h
    # and finite particle values are the existing runtime's valid-state assumptions.
    tested = 0
    for case in range(50000):
        h = f32(rng.uniform(.021, 1))
        h2 = f32(h * h)
        if case % 2:
            h2bits = struct.unpack("I", struct.pack("f", h2))[0]
            squared = struct.unpack("f", struct.pack("I", h2bits + rng.randint(0, 4)))[0]
        else:
            theta, length = rng.uniform(-math.pi, math.pi), rng.uniform(1, 12) * h
            dx, dy = f32(math.cos(theta) * length), f32(math.sin(theta) * length)
            squared = f32(f32(dx * dx) + f32(dy * dy))
        if squared < h2:
            continue
        q = min(1, max(0, f32(1 - f32(squared / h2))))
        weight = min(1, max(0, f32(1 - f32(f32(math.sqrt(squared)) / h))))
        assert q == 0 and weight == 0, ("nonzero support contribution", h, squared, q, weight)
        tested += 1
    result["float32_compact_support_zero_contribution_cases"] = tested
    result["limits"] = ["Finite, game-scale coordinates and positive finite kernel radii",
        "Randomized trajectories and float32 approximation are not actual HLSL execution or collision regression tests",
        "Legacy abs(angleDelta)<1e-6 branch approximates rotation and is unchanged",
        "Large-coordinate and very large accumulated-angle floating point robustness is not proven"]
    sources = ["Runtime/FluidExperimentGpuLiquid.Boundaries.cs", "Shaders/FluidExperimentLiquid.compute", "Shaders/FluidExperimentImproved.hlsl"]
    base = ROOT / "Assets/_Project/Features/Bartending/FluidGpuExperiment"
    result["source_sha256"] = {name: hashlib.sha256((base / name).read_bytes()).hexdigest() for name in sources}
    shader = (base / "Shaders/FluidExperimentImproved.hlsl").read_text(encoding="utf-8")
    viscosity_source = shader.split("void CalculateImprovedViscosity", 1)[1]
    result["viscosity_original_threshold_retained_in_source"] = (
        "weight > 0 && distance > 1e-6" in viscosity_source
        or "weight <= 0 || distance <= 1e-6" in viscosity_source)
    assert result["viscosity_original_threshold_retained_in_source"], "Reviewed near-zero threshold must be retained"
    result["status"] = "BOUND_AND_ZERO_CONTRIBUTION_SAMPLING_PASS"
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "results.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    run()
