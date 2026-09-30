"""Validate and compare completed full-Manual probes without changing their evidence.

Run from any directory; inputs default to runs 44/45. Runtime IDs are session-local,
so report their mapping separately from authored name/kind identity and do not
present these frame-time-driven runs as identical particle-trajectory replays.
"""
from __future__ import annotations

import argparse
from collections import Counter
import csv
import hashlib
import json
import math
from pathlib import Path
import sys


EVIDENCE = Path(__file__).resolve().parents[1]
CASE_NAMES = ("idle", "glass-drag", "glass-rotate", "bottle-pour")
MEASURES = {
    "gpuSubmissionCpuStepMs": "CPU time inside liquid.Step; excludes GPU completion and reservoir/world CPU work",
    "completedLiquidPhysicsAndRenderMs": "Separate diagnostic: one .02s CPU/liquid/Physics2D tick plus Camera.Render and one-pixel ReadPixels completion wait",
    "manualCameraCpuMs": "Normal-window explicit Camera.Render CPU wall time; no forced GPU completion",
    "frameIntervalMs": "Editor frame interval at a requested 60-frame cap; not gameplay FPS or a GPU timestamp",
}


def read(path: Path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def compare(before: Path, after: Path, failed: Path) -> dict:
    checks, errors = [], []

    def check(condition, label):
        checks.append({"check": label, "passed": bool(condition)})
        if not condition:
            errors.append(label)

    def finite(value):
        return isinstance(value, (int, float)) and math.isfinite(value)

    documents = [read(folder / "manual-performance.json") for folder in (before, after)]
    inventories = [doc["authoredInventory"] for doc in documents]
    names = [sorted((item["name"], item["kind"]) for item in inventory) for inventory in inventories]
    ids = [sorted(item["id"] for item in inventory) for inventory in inventories]
    name_ids = [{item["name"]: item["id"] for item in inventory} for inventory in inventories]
    check(names[0] == names[1], "Authored name/kind identities match across runs")
    check(ids[0] == ids[1] and len(set(ids[0])) == len(ids[0]), "Session ID sets match and are unique")
    exact_id_mapping = name_ids[0] == name_ids[1]
    check(all(len(inventory) == 9 for inventory in inventories), "Both runs retain all nine authored bodies")
    inputs = []
    for folder, doc, inventory in zip((before, after), documents, inventories):
        label = folder.name
        exit_record = read(folder / "exit.json")
        check(doc["status"] == "PASS" and not doc.get("error"), label + ": harness PASS with no error")
        check(exit_record["exitCode"] == 0 and not exit_record["timedOut"], label + ": process completed successfully")
        check([case["name"] for case in doc["cases"]] == list(CASE_NAMES), label + ": all four cases completed")
        counts = Counter(item["kind"] for item in inventory)
        recorded_counts = {item["kind"]: item["count"] for item in doc["authoredKindCounts"]}
        check(dict(counts) == recorded_counts and counts["Bottle"] == 3, label + ": recorded kind counts match inventory including three bottles")
        with (folder / "manual-performance.csv").open(encoding="utf-8-sig", newline="") as stream:
            rows = list(csv.DictReader(stream))
        for case in doc["cases"]:
            tag = label + "/" + case["name"]
            check(case["functionalChecksPassed"] is True and case["inventoryPreserved"] is True,
                  tag + ": runtime ledger conservation, finite particle state, source debit, held state and inventory checks passed")
            expected = {"itemCount": 9, "bottleCount": 3, "boundarySegments": 727,
                        "capacity": 4096, "substeps": 2, "surfaceWidth": 1280, "surfaceHeight": 720,
                        "automaticReadback": True, "controlsEnabled": True, "effectsEnabled": True}
            check(all(case[key] == value for key, value in expected.items()), tag + ": full-scene workload and settings preserved")
            check(case["measurementSeconds"] >= 3 and case["normalFrames"] >= 10
                  and case["normalFixedSteps"] >= 50 and case["renderedFrames"] >= case["normalFrames"]
                  and case["syncSamples"] == 20, tag + ": sufficient normal and separate completion samples")
            for field in ("generatedMl", "sourceDebitMl", "activeMl", "ownedMl", "retiredMl", "maximumSpeed"):
                check(finite(case[field]) and case[field] >= 0, tag + ": finite nonnegative " + field)
            expected_emission = 15 if case["name"] == "bottle-pour" else 0
            check(abs(case["generatedMl"] - expected_emission) < .001
                  and abs(case["sourceDebitMl"] - expected_emission) < .001,
                  tag + ": generated ml and source debit match expected measured-window emission")
            for metric in MEASURES:
                stats = case[metric]
                expected_count = case["normalFixedSteps"] if metric == "gpuSubmissionCpuStepMs" else (
                    20 if metric == "completedLiquidPhysicsAndRenderMs" else case["normalFrames"])
                check(stats["count"] == expected_count and all(finite(stats[key]) and stats[key] > 0
                      for key in ("mean", "p50", "p95", "p99")), tag + ": valid " + metric + " distribution")
            normal = [row for row in rows if row["case"] == case["name"] and row["kind"] == "normal"]
            completed = [row for row in rows if row["case"] == case["name"] and row["kind"] == "completed-tick-render"]
            check(len(normal) == case["normalFrames"] and len(completed) == 20,
                  tag + ": CSV keeps normal and forced-completion samples separate")
            for metric, field, samples in (("frameIntervalMs", "wall_frame_ms", normal),
                    ("manualCameraCpuMs", "camera_cpu_ms", normal),
                    ("completedLiquidPhysicsAndRenderMs", "completed_tick_render_ms", completed)):
                values = sorted(float(row[field]) for row in samples)
                check(bool(values) and abs(sum(values) / len(values) - case[metric]["mean"]) < .000002
                      and abs(values[math.ceil(.95 * len(values)) - 1] - case[metric]["p95"]) < .000002,
                      tag + ": CSV independently reproduces " + metric + " mean/p95")
        inputs.append({"directory": str(folder.resolve()), "reportSha256": digest(folder / "manual-performance.json"),
                       "csvSha256": digest(folder / "manual-performance.csv"), "exitSha256": digest(folder / "exit.json")})
    for key in ("unityVersion", "gpu", "platform", "protocol"):
        check(documents[0][key] == documents[1][key], "Matching environment/protocol: " + key)
    manifests = [read(folder / "source-sha256.json") for folder in (before, after)]
    feature = "Assets/_Project/Features/Bartending/FluidGpuExperiment/"
    asset_parity = {}
    for key in (feature + "Data/FluidExperimentLiquidSettings.asset", feature + "Scenes/FluidGpuComparison.unity"):
        asset_parity[key] = manifests[0].get(key) == manifests[1].get(key) and key in manifests[0]
        check(asset_parity[key], "Identical source manifest hash: " + key)
    gc_all_zero = all(case["mainThreadAllocatedBytes"][stat] == 0 for doc in documents
                      for case in doc["cases"] for stat in ("mean", "p50", "p95", "p99"))
    comparison = []
    for old, new in zip(documents[0]["cases"], documents[1]["cases"]):
        metrics = {}
        for metric in MEASURES:
            metrics[metric] = {stat: {"beforeMs": old[metric][stat], "afterMs": new[metric][stat],
                                     "reductionPercent": 100 * (1 - new[metric][stat] / old[metric][stat])}
                               for stat in ("mean", "p95")}
        comparison.append({"name": old["name"], "timings": metrics,
                           "beforeNormalFrames": old["normalFrames"], "afterNormalFrames": new["normalFrames"],
                           "beforeFixedSteps": old["normalFixedSteps"], "afterFixedSteps": new["normalFixedSteps"],
                           "completionSamplesPerRun": 20,
                           "logicalState": {field: {"before": old[field], "after": new[field]}
                                            for field in ("generatedMl", "sourceDebitMl", "activeMl", "ownedMl", "retiredMl")}})
    failed_hash = digest(failed / "manual-performance.json")
    failed_doc = read(failed / "manual-performance.json")
    check(failed_doc["status"] == "FAIL" and not failed_doc["cases"], "Run43 failed fixture is retained and excluded from comparisons")
    limitations = [
        "One Editor run per version on AMD integrated graphics; no standalone gameplay FPS, GPU timestamps, or repeated-run significance claim.",
        "Normal timing has no forced readback. The separate 20-sample completion diagnostic includes CPU work, Physics2D, liquid GPU work, rendering and a readback wait; it is not GPU-only time.",
        "Motion is driven by frame time. Different frame counts and fixed-step counts produce different sampled trajectories and final owned ml; this is not an identical-state replay or a proof of identical fluid behavior.",
        "Ledger conservation and finite particle checks are evidenced by the completed harness functionalChecksPassed flag. Raw ledger error values and full particle snapshots were not serialized into these performance reports.",
        "GC counters are zero for every case in both runs. Treat this counter as unsupported/unavailable here; do not claim zero allocation or an allocation improvement." if gc_all_zero else
        "GC counters are recorded but are not independently validated by this comparison.",
    ]
    if not exact_id_mapping:
        limitations.append("Authored names/kinds and unique ID sets match, but name-to-runtime-ID mappings differ across Editor sessions. World.Register assigns nextId++ by registration order; inventory identity is stable within each run, not across launches.")
    return {"status": "FAIL" if errors else "PASS_WITH_LIMITATIONS", "errors": errors,
            "inputs": inputs, "environment": {key: documents[0][key] for key in ("unityVersion", "gpu", "platform")},
            "identity": {"exactRuntimeIdMappingMatch": exact_id_mapping,
                         "nameKindMatch": names[0] == names[1], "uniqueRuntimeIdSetsMatch": ids[0] == ids[1],
                         "mapping": [{"name": name, "kind": kind, "beforeId": name_ids[0][name],
                                      "afterId": name_ids[1][name]} for name, kind in names[0]]},
            "assetHashParity": asset_parity,
            "changedManifestPaths": sorted(key for key in set(manifests[0]) | set(manifests[1])
                                           if manifests[0].get(key) != manifests[1].get(key)),
            "measurementLabels": MEASURES, "cases": comparison,
            "allocationMeasurement": {"status": "UNAVAILABLE_ALL_ZERO" if gc_all_zero else "UNVERIFIED",
                                      "supportedAllocationClaim": None},
            "preservedFailedFixture": {"directory": str(failed.resolve()), "status": failed_doc["status"],
                                       "reportSha256": failed_hash, "reason": "Incorrect hard-coded minimum object/bottle counts; no timing cases collected"},
            "limitations": limitations, "checksPassed": sum(item["passed"] for item in checks),
            "checksTotal": len(checks), "checks": checks}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--before", type=Path, default=EVIDENCE / "44-manual-motion-before-inventory")
    parser.add_argument("--after", type=Path, default=EVIDENCE / "45-manual-motion-after")
    parser.add_argument("--failed-fixture", type=Path, default=EVIDENCE / "43-manual-motion-before")
    parser.add_argument("--output", type=Path, default=EVIDENCE / "manual-performance-comparison.json")
    args = parser.parse_args()
    protected = [folder / "manual-performance.json" for folder in (args.before, args.after, args.failed_fixture)]
    if args.output.resolve() in [path.resolve() for path in protected]:
        parser.error("Output must not overwrite an input evidence report.")
    original_hashes = [digest(path) for path in protected]
    result = compare(args.before, args.after, args.failed_fixture)
    assert original_hashes == [digest(path) for path in protected], "Input evidence changed during comparison"
    args.output.write_text(json.dumps(result, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    print(f"{result['status']}: {result['checksPassed']}/{result['checksTotal']} checks; {args.output}")
    for case in result["cases"]:
        cpu = case["timings"]["gpuSubmissionCpuStepMs"]
        total = case["timings"]["completedLiquidPhysicsAndRenderMs"]
        def pair(metric):
            return ", ".join(f"{stat} {metric[stat]['beforeMs']:.3f}->{metric[stat]['afterMs']:.3f}ms ({metric[stat]['reductionPercent']:.1f}% lower)" for stat in ("mean", "p95"))
        print(f"{case['name']}: CPU step {pair(cpu)}; completed CPU+GPU work {pair(total)}")
    return 1 if result["errors"] else 0


if __name__ == "__main__":
    sys.exit(main())
