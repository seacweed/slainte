"""Audit current HEAD scope and the validation copy; preserve the older byte-baseline audit."""
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path
import argparse
import hashlib
import json
import re
import subprocess


ROOT = Path(__file__).resolve().parents[2]
FEATURE = "Assets/_Project/Features/Bartending/FluidGpuExperiment"
EVIDENCE = "FluidGpuExperimentEvidence"
SERIALIZED = {".unity", ".prefab", ".asset", ".mat"}
VALIDATED = SERIALIZED | {".cs", ".shader", ".compute", ".hlsl"}
GUID = re.compile(r"^guid: ([a-f0-9]{32})$", re.M)
REFERENCE = re.compile(r"guid: ([a-f0-9]{32})")


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT, stderr=subprocess.PIPE)


def names(raw):
    return sorted(name.decode("utf-8", errors="surrogateescape") for name in raw.split(b"\0") if name)


def scoped(name):
    return name == FEATURE + ".meta" or name.startswith(FEATURE + "/") or name.startswith(EVIDENCE + "/")


def read_guid(path):
    match = GUID.search(path.read_text(encoding="utf-8-sig", errors="replace"))
    return match.group(1) if match else None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--target", default="Library/FluidSwapHarness")
    parser.add_argument("--output", default=EVIDENCE + "/final-scope-audit.json")
    args = parser.parse_args()
    harness = (ROOT / args.target).resolve()
    output = (ROOT / args.output).resolve()
    historical_report = ROOT / EVIDENCE / "final-isolation-audit.json"
    original_manifest = ROOT / EVIDENCE / "original-tracked-sha256.json"
    if not output.is_relative_to(ROOT / EVIDENCE) or output in {historical_report, original_manifest}:
        parser.error("Output must stay within evidence and cannot overwrite a historical baseline/report.")

    # --no-renames includes both sides of moves, so a move from outside scope cannot be hidden.
    tracked = names(git("diff", "--no-ext-diff", "--no-renames", "--name-only", "-z", "HEAD", "--"))
    untracked = names(git("ls-files", "--others", "--exclude-standard", "-z"))
    outside = sorted(name for name in set(tracked + untracked) if not scoped(name))
    counts = Counter("feature" if name.startswith(FEATURE) else "evidence" if name.startswith(EVIDENCE + "/")
                     else "outside" for name in tracked + untracked)

    feature = ROOT / FEATURE
    old_fork_guids = set(json.loads((ROOT / EVIDENCE / "clone-guid-map.json").read_text(encoding="utf-8-sig")))
    all_guids = defaultdict(list)
    for meta in (ROOT / "Assets").rglob("*.meta"):
        guid = read_guid(meta)
        if guid:
            all_guids[guid].append(meta.relative_to(ROOT).as_posix())
    missing_meta, malformed_meta, duplicates, old_references, missing_copy, differing_copy = [], [], [], [], [], []
    metadata_guid_mismatches, orphan_meta = [], []
    feature_guids = set()
    parity_counts = Counter()
    shared_references = set()
    for path in [feature, *sorted(feature.rglob("*"))]:
        relative = path.relative_to(ROOT).as_posix()
        if path.suffix == ".meta":
            if not Path(str(path)[:-5]).exists():
                orphan_meta.append(relative)
            continue
        meta = Path(str(path) + ".meta")
        if not meta.is_file():
            missing_meta.append(relative)
        else:
            guid = read_guid(meta)
            if not guid:
                malformed_meta.append(meta.relative_to(ROOT).as_posix())
            else:
                feature_guids.add(guid)
                if len(all_guids[guid]) != 1 or guid in old_fork_guids:
                    duplicates.append({"asset": relative, "guid": guid, "metadata_paths": all_guids[guid]})
        if not path.is_file():
            continue
        if path.suffix in SERIALIZED:
            refs = set(REFERENCE.findall(path.read_text(encoding="utf-8-sig", errors="replace")))
            reused = sorted(refs & old_fork_guids)
            if reused:
                old_references.append({"asset": relative, "original_fork_guids": reused})
            for reference in refs:
                shared_references.update(name for name in all_guids.get(reference, []) if not name.startswith(FEATURE))
        if path.suffix in VALIDATED:
            parity_counts[path.suffix] += 1
            copy = harness / relative
            if not copy.is_file():
                missing_copy.append(relative)
            elif digest(path) != digest(copy):
                differing_copy.append(relative)
            copy_meta = Path(str(copy) + ".meta")
            if meta.is_file() and (not copy_meta.is_file() or read_guid(meta) != read_guid(copy_meta)):
                metadata_guid_mismatches.append(relative)

    # Validate the actual test C# copied into the disposable project's Editor folder too.
    harness_test_mismatches = []
    harness_tests = sorted((ROOT / EVIDENCE / "HarnessSource").glob("*.cs"))
    for path in harness_tests:
        copy = harness / "Assets/Editor" / path.name
        if not copy.is_file() or digest(path) != digest(copy):
            harness_test_mismatches.append(path.relative_to(ROOT).as_posix())

    # The preparer's manifest lists the copied, read-only source dependencies as well as the fork.
    dependency_mismatches, dependency_line_endings = [], []
    source_manifest = harness / "source-sha256.json"
    dependency_count = 0
    if source_manifest.is_file():
        for name, expected in json.loads(source_manifest.read_text(encoding="utf-8-sig")).items():
            name = name.replace("\\", "/")
            if name.startswith(FEATURE + "/"):
                continue
            dependency_count += 1
            source, copy = ROOT / name, harness / name
            if not source.is_file() or not copy.is_file():
                dependency_mismatches.append(name)
                continue
            source_bytes, copy_bytes = source.read_bytes(), copy.read_bytes()
            source_hash, copy_hash = hashlib.sha256(source_bytes).hexdigest(), hashlib.sha256(copy_bytes).hexdigest()
            if source_hash == copy_hash == expected:
                continue
            normalized = source_bytes.replace(b"\r\n", b"\n")
            text_hashes = {source_hash, hashlib.sha256(normalized).hexdigest(),
                           hashlib.sha256(normalized.replace(b"\n", b"\r\n")).hexdigest()}
            if source.suffix in VALIDATED and normalized == copy_bytes.replace(b"\r\n", b"\n") and expected in text_hashes:
                dependency_line_endings.append({"asset": name, "source_sha256": source_hash,
                                                "copy_sha256": copy_hash, "manifest_sha256": expected})
            else:
                dependency_mismatches.append(name)
    else:
        dependency_mismatches.append("Validation source-sha256.json is missing")

    # This is historical context, deliberately not a substitute for the current HEAD audit.
    prior = json.loads(historical_report.read_text(encoding="utf-8-sig"))
    historical_hash = digest(historical_report)
    original = json.loads(original_manifest.read_text(encoding="utf-8-sig"))
    changed_count = line_endings_only = 0
    for name, expected in original.items():
        source = ROOT / name
        if not source.is_file():
            changed_count += 1
            continue
        content = source.read_bytes()
        if hashlib.sha256(content).hexdigest() == expected:
            continue
        changed_count += 1
        normalized = content.replace(b"\r\n", b"\n")
        if expected in {hashlib.sha256(normalized).hexdigest(), hashlib.sha256(normalized.replace(b"\n", b"\r\n")).hexdigest()}:
            line_endings_only += 1

    failures = {"changes_outside_scope": outside, "missing_metadata": missing_meta,
                "malformed_metadata": malformed_meta, "orphan_metadata": orphan_meta,
                "duplicate_or_original_fork_guids": duplicates, "references_to_original_fork_assets": old_references,
                "missing_validation_copies": missing_copy, "different_validation_copies": differing_copy,
                "validation_metadata_guid_mismatches": metadata_guid_mismatches,
                "harness_test_source_mismatches": harness_test_mismatches,
                "shared_dependency_manifest_mismatches": dependency_mismatches}
    result = {"passed": not any(failures.values()), "audited_at_utc": datetime.now(timezone.utc).isoformat(),
              "basis": "Current working tree/index versus HEAD, plus non-ignored untracked files. Ignored Library output is excluded from change scope.",
              "head": git("rev-parse", "HEAD").decode().strip(),
              "validation_project": str(harness),
              "scope": {"tracked_changed_files": len(tracked), "non_ignored_untracked_files": len(untracked),
                        "counts_by_allowed_root": dict(counts), "allowed_roots": [FEATURE, EVIDENCE]},
              "fresh_asset_checks": {"feature_guid_count": len(feature_guids), "validated_asset_counts": dict(parity_counts),
                                     "harness_test_source_count": len(harness_tests), "shared_reference_metadata_count": len(shared_references),
                                     "copied_shared_dependency_count": dependency_count},
              "failures": failures,
              "line_ending_equivalent_shared_copies": dependency_line_endings,
              "historical_baseline": {"report": historical_report.relative_to(ROOT).as_posix(),
                                      "preserved_report_sha256": historical_hash, "prior_report_passed": prior.get("passed"),
                                      "prior_report_changed_file_count": len(prior.get("changed_original_files", [])),
                                      "saved_original_file_count": len(original), "current_differences_from_saved_hashes": changed_count,
                                      "line_endings_only_differences": line_endings_only,
                                      "other_or_missing_historical_differences": changed_count - line_endings_only,
                                      "interpretation": "The older original-tracked-sha256 baseline is distinct from current HEAD. Its failed report is retained unchanged; this audit does not claim historical original bytes are identical."}}
    if digest(historical_report) != historical_hash:
        raise RuntimeError("Historical report changed concurrently; rerun against a stable report.")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps({"passed": result["passed"], "output": str(output), "scope": result["scope"],
                      "failure_counts": {key: len(value) for key, value in failures.items()},
                      "historical_differences": changed_count, "historical_line_endings_only": line_endings_only}, indent=2))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
