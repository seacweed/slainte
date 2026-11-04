# Isolation fixed; local rotation refinement still failed

The new code held global substeps at 2. Remote drag and rotation comparisons both had position/velocity RMS exactly 0, matching the A/A repeat. Positive contact moved an airborne particle to x=2.165; a sealed vessel retained volume through fast translation and 855-degree rotation.

The run nevertheless exited 1: conservative contact advancement exhausted its fixed 128-refinement budget during the multi-turn contact case. Finite positions and retained volume were not treated as full success.

The next revision scales only the contacted particle/edge refinement budget with the edge's unwrapped angular travel (bounded at 2048), leaving the global fluid step count, pressure passes, and delta time fixed. It also records the maximum refinement count and reports exhaustion counts explicitly.
