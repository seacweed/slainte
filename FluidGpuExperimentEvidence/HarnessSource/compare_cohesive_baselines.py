"""Compare saved A-E GPU probes without changing either input or any Unity state.

Particle buffer slots are not identities. This fixture assigns a different ml
amount to every particle, so its unchanged amount is the correspondence key.
Tolerances match CohesiveValidation.cs and must not be inferred from the results.
"""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path


TOLERANCES = {"position": 1e-4, "velocity": 1e-3, "composition": 1e-4}
EXPECTED_MODES = {
    "ACurrent", "BReferencePhysics", "CReferenceSurface", "DImprovedSurface", "ECalibratedLiquid"
}


def read_snapshot(path: Path) -> dict:
    payload = json.loads(path.read_text(encoding="utf-8-sig"))
    modes = payload["modes"]
    if len(modes) != len(EXPECTED_MODES) or {mode["mode"] for mode in modes} != EXPECTED_MODES:
        raise ValueError(f"{path}: expected exactly one entry for each of A-E")
    for mode in modes:
        amounts = [particle["volume"] for particle in mode["particles"]]
        if len(amounts) != len(set(amounts)):
            raise ValueError(f"{path}: {mode['mode']} has no unique-volume particle correspondence")
        for particle in mode["particles"]:
            values = [particle["volume"], particle["ingredientA"], particle["ingredientB"]]
            values += [particle[key][axis] for key in ("position", "velocity") for axis in ("x", "y")]
            if not all(math.isfinite(value) for value in values) or particle["volume"] <= 0:
                raise ValueError(f"{path}: {mode['mode']} contains nonfinite or invalid particle data")
    return {mode["mode"]: mode for mode in modes}


def compare(before_path: Path, after_path: Path) -> dict:
    before, after = read_snapshot(before_path), read_snapshot(after_path)
    result = {
        "before": str(before_path.resolve()),
        "after": str(after_path.resolve()),
        "tolerances": TOLERANCES,
        "identity": "exact unique per-particle volume_ml; never GPU buffer slot",
        "modes": [],
        "interpretation": "A comparison of these two saved probes only; this does not establish determinism across all runs.",
    }
    for name, original in before.items():
        current = after[name]
        settings_match = all(original[key] == current[key] for key in ("radius", "particleMl", "improved", "improvedSurface"))
        old_particles = {particle["volume"]: particle for particle in original["particles"]}
        new_particles = {particle["volume"]: particle for particle in current["particles"]}
        count_match = len(old_particles) == len(new_particles)
        volume_match = old_particles.keys() == new_particles.keys()
        maximum = {key: 0.0 for key in TOLERANCES}
        for amount in old_particles.keys() & new_particles.keys():
            a, b = old_particles[amount], new_particles[amount]
            for key in ("position", "velocity"):
                difference = math.hypot(a[key]["x"] - b[key]["x"], a[key]["y"] - b[key]["y"])
                maximum[key] = max(maximum[key], difference)
            maximum["composition"] = max(maximum["composition"], abs(a["ingredientA"] - b["ingredientA"]),
                                         abs(a["ingredientB"] - b["ingredientB"]))
        within_tolerance = all(maximum[key] < tolerance for key, tolerance in TOLERANCES.items())
        result["modes"].append({
            "mode": name,
            "settings_and_flags_match": settings_match,
            "particle_count_match": count_match,
            "unique_particle_volumes_match": volume_match,
            "before_active_count": len(old_particles),
            "after_active_count": len(new_particles),
            "maximum_absolute_difference": maximum,
            "exact_probe_match": settings_match and count_match and volume_match and all(value == 0 for value in maximum.values()),
            "passed": settings_match and count_match and volume_match and within_tolerance,
        })
    result["passed"] = all(mode["passed"] for mode in result["modes"])
    result["exact_probe_match"] = all(mode["exact_probe_match"] for mode in result["modes"])
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("before", type=Path)
    parser.add_argument("after", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    result = compare(args.before, args.after)
    text = json.dumps(result, indent=2, allow_nan=False) + "\n"
    if args.output:
        args.output.write_text(text, encoding="utf-8")
    print(text, end="")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
