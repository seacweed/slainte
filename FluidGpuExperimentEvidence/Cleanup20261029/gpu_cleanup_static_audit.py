"""Check retained F algorithms and kernel/default wiring against the cleanup snapshot."""
from pathlib import Path
import json
import re
import zipfile

BASE = Path(__file__).resolve().parents[2]
FEATURE_REL = "Assets/_Project/Features/Bartending/FluidGpuExperiment/"
FEATURE = BASE / FEATURE_REL
CHECKS = []


def check(name, ok, details=None):
    CHECKS.append(dict(name=name, passed=bool(ok), details=details))


def canonical(source):
    source = re.sub(r"/\*.*?\*/", "", source, flags=re.S)
    source = re.sub(r"//[^\n]*", "", source)
    return re.sub(r"\s+", "", source)


def function(source, name):
    match = re.search(r"\b" + re.escape(name) + r"\([^;]*?\)\s*(?:=>[^;]+;|\{)", source)
    assert match, name
    if "=>" in match.group():
        return match.group()
    opening = source.index("{", match.start())
    depth, cursor = 1, opening + 1
    while depth:
        if source[cursor] == "{":
            depth += 1
        elif source[cursor] == "}":
            depth -= 1
        cursor += 1
    return source[match.start():cursor]


def defaults(source):
    return {m.group(1): m.group(2).strip()
            for m in re.finditer(r"public\s+\w+\s+(\w+)\s*(=[^;]+)?;", source)
            if m.group(2)}


def fields(source):
    return set(re.findall(r"public\s+\w+\s+(\w+)\s*(?:=[^;]+)?;", source))


def read(path):
    return (FEATURE / path).read_text(encoding="utf-8-sig")


with zipfile.ZipFile(Path(__file__).with_name("before.zip")) as archive:
    def before(path):
        return archive.read(FEATURE_REL + path).decode("utf-8-sig").replace("\r\n", "\n")

    for path in ["Shaders/FluidExperimentCohesive.hlsl",
                 "Runtime/FluidExperimentGpuLiquid.Stirring.cs",
                 "Runtime/FluidExperimentGpuLiquid.Accounting.cs",
                 "Runtime/FluidExperimentGpuLiquid.Cocktails.cs",
                 "Runtime/FluidExperimentGpuLiquid.Shards.cs",
                 "Runtime/FluidExperimentGpuLiquid.Boundaries.cs",
                 "Shaders/FluidExperimentCohesiveContacts.hlsl"]:
        check("preserved algorithm " + path, canonical(before(path)) == canonical(read(path)))

    path = "Runtime/FluidExperimentGpuLiquid.Cohesive.cs"
    check("preserved cohesive simulation dispatch", canonical(function(before(path), "StepCohesiveFluid"))
          == canonical(function(read(path), "StepCohesiveFluid")))

    for path in ["Runtime/FluidExperimentGpuLiquid.Garnish.cs",
                 "Runtime/FluidExperimentGpuLiquid.IceFlow.cs",
                 "Runtime/FluidExperimentGpuLiquid.Splash.cs"]:
        expected = before(path).replace("!useCohesivePhysics || ", "")
        expected = expected.replace("            if (!useCohesivePhysics) return;\n", "")
        check("preserved F-specialized algorithm " + path, canonical(expected) == canonical(read(path)))

    path = "Runtime/FluidExperimentGpuLiquid.CalibratedFill.cs"
    for name in ["AreaPerMl"]:
        old_prefix = before(path).split("        public float ImprovedKernelRadius(")[0]
        new_prefix = read(path).rsplit("    }", 1)[0].rstrip()
        for line in ["        private readonly List<Vector2> calibratedFillSites = new List<Vector2>();\n",
                     "        private readonly List<Vector2> calibratedOccupiedSites = new List<Vector2>();\n",
                     "        private readonly List<Vector2> calibratedHeightSamples = new List<Vector2>();\n"]:
            old_prefix = old_prefix.replace(line, "")
        old_prefix = old_prefix.replace("using System.Collections.Generic;\n", "")
        # Compare both overload bodies rather than class/file terminators.
        old_bodies = old_prefix[old_prefix.index("        public float AreaPerMl"):]
        new_bodies = read(path)[read(path).index("        public float AreaPerMl"):].rsplit("    }", 1)[0]
        check("preserved both AreaPerMl overloads", canonical(old_bodies) == canonical(new_bodies))

    for path, names in [("Shaders/FluidExperimentReference.hlsl", ["ReferenceParticleActive", "ReferenceMass"]),
                        ("Shaders/FluidExperimentStreamSurface.hlsl", ["MergeStreamContacts", "StreamBlocked"])]:
        for name in names:
            check("preserved shader helper " + name, canonical(function(before(path), name))
                  == canonical(function(read(path), name)))

    settings_path = "Runtime/FluidExperimentLiquidSettings.cs"
    old_defaults, new_defaults = defaults(before(settings_path)), defaults(read(settings_path))
    changed_defaults = {k: [old_defaults.get(k), v] for k, v in new_defaults.items()
                        if old_defaults.get(k) != v}
    check("retained liquid defaults unchanged", not changed_defaults, changed_defaults)
    removed_fields = sorted(fields(before(settings_path)) - fields(read(settings_path)))

    compute_path = "Shaders/FluidExperimentLiquid.compute"
    old_kernels = set(re.findall(r"^#pragma kernel (\w+)", before(compute_path), re.M))
    kernels = set(re.findall(r"^#pragma kernel (\w+)", read(compute_path), re.M))
    shader_source = "\n".join(p.read_text(encoding="utf-8-sig") for p in (FEATURE / "Shaders").glob("*")
                              if p.suffix in (".compute", ".hlsl"))
    missing = sorted(k for k in kernels if not re.search(r"\bvoid\s+" + k + r"\s*\(", shader_source))
    check("all retained compute entrypoints defined", not missing, missing)
    cs_source = "\n".join(p.read_text(encoding="utf-8-sig")
                         for p in (FEATURE / "Runtime").glob("FluidExperimentGpuLiquid*.cs"))
    requests = set(re.findall(r'RequireKernel\("(\w+)"\)', cs_source))
    check("all C# kernel requests exist", not (requests - kernels), sorted(requests - kernels))
    check("all retained entrypoints have clients", not (kernels - requests), sorted(kernels - requests))

    for name in ["ResetParticles", "ResetCompositionBuffers", "IntegrateParticles", "SnapshotVelocities",
                 "ClearGrid", "BuildGrid", "CarryPickupContents", "SetVesselSuspended",
                 "TranslateVesselContents", "SwapContents", "ReleaseOwner", "CollectSpoonMixture",
                 "ApplySpoonMixture", "CollectLiquidLedger", "TransferVesselContents",
                 "CalculateConservativeMixWeights", "MixCompositionConservative", "UpdateParticleColors",
                 "ApplyTechnique", "ApplyBreakSplash"]:
        check("preserved compute kernel " + name, canonical(function(before(compute_path), name))
              == canonical(function(read(compute_path), name)))

for path in FEATURE.rglob("*"):
    if path.suffix not in (".cs", ".compute", ".hlsl", ".shader"):
        continue
    source = path.read_text(encoding="utf-8-sig")
    source = re.sub(r'/\*.*?\*/|//[^\n]*|"(?:\\.|[^"\\])*"', "", source, flags=re.S)
    check("balanced braces " + str(path.relative_to(FEATURE)), source.count("{") == source.count("}"))

result = dict(passed=all(c["passed"] for c in CHECKS), checks=CHECKS, settingsRemoved=removed_fields,
              kernelCounts=dict(before=len(old_kernels), after=len(kernels)),
              kernelsRemoved=sorted(old_kernels - kernels),
              scope="Static source/token and authored-default parity. GPU runtime evidence is separate.")
Path(__file__).with_name("gpu-cleanup-static-audit.json").write_text(
    json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(dict(passed=result["passed"], checks=len(CHECKS),
                      failures=[c for c in CHECKS if not c["passed"]],
                      settingsRemoved=len(removed_fields), kernels=result["kernelCounts"]), ensure_ascii=False))
