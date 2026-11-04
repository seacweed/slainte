"""CPU mathematical evidence for E's symmetric GPU exchange (not a Unity/GPU execution test).

Run: python FluidGpuExperimentEvidence/HarnessSource/validate_conservative_mixing.py
Uses IEEE float32 stores to approximate HLSL arithmetic and checks ingredient ml, convexity,
unequal volumes, partially born particles, dense neighborhoods, and long repeated mixing.
"""
import json
import math
import random
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / "FluidGpuExperimentEvidence/conservative-ledger-static"


def f32(value):
    return struct.unpack("f", struct.pack("f", value))[0]


def exchange_matrix(volumes, durations, weights, maximum=0.5, owners=None):
    n = len(volumes)
    exchange = [[0.0] * n for _ in volumes]
    normalizers = []
    for i in range(n):
        for j in range(n):
            if i == j or min(durations[i], durations[j]) <= 0.000001 or (owners is not None and owners[i] != owners[j]):
                continue
            weight = f32(min(f32(weights[i][j] * min(durations[i], durations[j])), maximum) / 32.0)
            reduced_volume = f32(f32(volumes[i] * volumes[j]) / f32(volumes[i] + volumes[j]))
            exchange[i][j] = f32(weight * reduced_volume)
        summed = 0.0
        for j in range(n):
            summed = f32(summed + f32(exchange[i][j] / volumes[i]))
        normalizers.append(max(1.0, f32(summed / maximum)))
    for i in range(n):
        for j in range(n):
            exchange[i][j] = f32(exchange[i][j] / max(normalizers[i], normalizers[j]))
    return exchange


def mix(volumes, ratios, exchange):
    result = []
    for i, row in enumerate(ratios):
        output = []
        for ingredient, value in enumerate(row):
            change = 0.0
            for j in range(len(volumes)):
                coefficient = f32(exchange[i][j] / volumes[i])
                change = f32(change + f32(coefficient * f32(ratios[j][ingredient] - value)))
            output.append(f32(value + change))
        result.append(output)
    return result


def totals(volumes, ratios):
    return [math.fsum(v * r[k] for v, r in zip(volumes, ratios)) for k in range(len(ratios[0]))]


def main():
    rng = random.Random(3981002)
    results = []
    for label, n, frames in (("unequal_pair_partial_birth", 2, 600), ("dense_normalized", 160, 12),
                              ("long_mixture", 24, 1200), ("zero_birth_dt", 5, 20)):
        volumes = [f32(rng.choice([.017, .125, .5, 1.375])) for _ in range(n)]
        durations = [f32(rng.choice([.0004, .003, .00666667])) for _ in range(n)]
        if label == "unequal_pair_partial_birth":
            volumes, durations = [.017, 1.375], [.0004, .00666667]
        if label == "zero_birth_dt":
            durations[0] = 0.0
        ratios = [[float(i % 4 == k) for k in range(4)] for i in range(n)]
        weights = [[0.0] * n for _ in range(n)]
        for i in range(n):
            for j in range(i):
                weights[i][j] = weights[j][i] = 1e6 if label == "dense_normalized" else rng.uniform(5, 100)
        exchange = exchange_matrix(volumes, durations, weights)
        before = totals(volumes, ratios)
        worst_ratio_sum_error = 0.0
        for _ in range(frames):
            ratios = mix(volumes, ratios, exchange)
            assert all(-1e-7 <= c <= 1.000001 for r in ratios for c in r), label + ": negative/overfull ratio"
            worst_ratio_sum_error = max(worst_ratio_sum_error, max(abs(sum(r) - 1) for r in ratios))
        after = totals(volumes, ratios)
        worst_mass_error = max(abs(a - b) for a, b in zip(after, before))
        assert worst_mass_error < 0.0002, (label, before, after)
        assert worst_ratio_sum_error < 0.00002, (label, worst_ratio_sum_error)
        if label == "zero_birth_dt":
            assert ratios[0] == [1, 0, 0, 0], "zero lifetime particle must not exchange"
        results.append(dict(case=label, particles=n, steps=frames, max_ingredient_ml_error=worst_mass_error,
                            max_ratio_sum_error=worst_ratio_sum_error))

    isolated = [[1, 0], [0, 1], [1, 0]]
    exchange = exchange_matrix([.125, .375, .5], [.01] * 3, [[1000] * 3 for _ in range(3)], owners=[1, 2, 0])
    assert mix([.125, .375, .5], isolated, exchange) == isolated
    results.append(dict(case="ownership_isolation", owners=[1, 2, 0], unchanged=True))

    # Persistent per-slot generated/retired totals must survive slot overwrite. One slot, alternating
    # recipes over many lifetimes; the old snapshot-only method would retain only the final retirement.
    generated, retired = [0.0] * 4, [0.0] * 4
    for lifetime in range(10000):
        ingredient, amount = lifetime % 4, (lifetime % 7 + 1) / 8
        generated[ingredient] += amount
        retired[ingredient] += amount
    assert generated == retired and sum(retired) > 4000
    results.append(dict(case="slot_lifetime_model", lifetimes=10000, generated_ml=sum(generated), retired_ml=sum(retired)))

    # Make accidental implementation drift visible when this standalone evidence is rerun.
    shader = (ROOT / "Assets/_Project/Features/Bartending/FluidGpuExperiment/Shaders/FluidExperimentLiquid.compute").read_text()
    assert "min(_StreamParticles[i].stepDt, _StreamParticles[j].stepDt)" in shader
    assert "max(_ConservativeMixWeights[i], _ConservativeMixWeights[j])" in shader
    conservative = shader.split("void MixCompositionConservative", 1)[1].split("void CollectLiquidLedger", 1)[0]
    assert "ratioTotal" not in conservative and "max(0.0, selfRatio" not in conservative
    OUTPUT.mkdir(parents=True, exist_ok=True)
    report = dict(result="PASS", evidence_kind="CPU mathematical model; no Unity/GPU execution", cases=results)
    (OUTPUT / "cpu-mixing-results.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
