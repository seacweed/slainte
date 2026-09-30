"""Validate a completed controlled Player workload; never read a live report."""
import itertools
import json
import math
from pathlib import Path
import sys

attempt = Path(sys.argv[1]).resolve()
exit_result = json.loads((attempt / "exit.json").read_text(encoding="utf-8-sig"))
assert exit_result["exitCode"] == 0 and not exit_result["timedOut"]
report = json.loads((attempt / "player-performance.json").read_text(encoding="utf-8-sig"))
assert report["status"] == "COMPLETE"
assert not report["editor"] and not report["developmentBuild"]
expected = set(itertools.product(
    ["CReferenceSurface", "DImprovedSurface", "ECalibratedLiquid"],
    [100, 300, 600, 1000], [1, 4, 8, 16]))
cases = report["cases"]
assert len(cases) == len(expected) == 48
assert {(c["mode"], c["requestedParticles"], c["ingredientCount"]) for c in cases} == expected
for case in cases:
    assert case["status"] == "COMPLETE"
    assert case["requestedParticles"] == case["initialParticles"] == case["finalParticles"]
    assert case["measuredFrames"] >= 90 and case["measuredPhysicsSteps"] >= 50
    assert case["measurementSeconds"] >= 2
    assert case["verifiedSurfaceFrames"] == case["measuredFrames"]
    assert case["surfaceWidth"] == case["width"] == 1280
    assert case["surfaceHeight"] == case["height"] == 720
    assert abs(case["volumeMl"] - 500) <= .005
    assert abs(case["fillFraction"] - .625) <= .00001
    assert case["vessels"] == 2 and case["vesselCapacityMl"] == 800
    assert (attempt / case["screenshot"]).is_file()
    half_height = case["cameraOrthographicSize"]
    half_width = half_height * case["width"] / case["height"]
    for axis, half_extent in [("x", half_width), ("y", half_height)]:
        for edge in ["vesselBoundsMin", "vesselBoundsMax"]:
            coordinate = .5 + (case[edge][axis] - case["cameraCenter"][axis]) / (2 * half_extent)
            assert .05 - 1e-6 <= coordinate <= .95 + 1e-6
    for metric in ["cpuPhysicsStep", "cpuManualCameraRender", "frameInterval"]:
        timing = case[metric]
        assert timing["status"].startswith("MEASURED") and timing["samples"] > 0
        assert all(math.isfinite(timing[k]) and timing[k] >= 0 for k in ["meanMs", "p50Ms", "p95Ms", "p99Ms"])

summary = {
    "status": "PASS", "cases": len(cases),
    "conditions": report["evidence"],
    "minimum_measured_frames": min(c["measuredFrames"] for c in cases),
    "minimum_physics_steps": min(c["measuredPhysicsSteps"] for c in cases),
    "minimum_seconds": min(c["measurementSeconds"] for c in cases),
    "total_verified_surface_frames": sum(c["verifiedSurfaceFrames"] for c in cases),
    "gpu_metric_statuses": {metric: sorted({c[metric]["status"] for c in cases})
                            for metric in ["gpuFrame", "gpuSurface", "computeGpuTiming"]},
    "e_1000_particles_16_ingredients": cases[-1],
}
(attempt / "accepted-summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
print(json.dumps({key: value for key, value in summary.items() if key not in ["conditions", "e_1000_particles_16_ingredients"]}, indent=2))
