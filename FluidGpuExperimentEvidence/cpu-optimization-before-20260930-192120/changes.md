Original CPU sources and SHA-256 hashes were preserved before optimization.

Changes under review:

- E solver metadata: measure each vessel's area once per complete Step instead of twice per substep; derive the same kernel radius from that result. Do not upload metadata arrays when every value and count is unchanged. Reset upload state when buffers are disposed. All solver iterations and substeps remain unchanged.
- Reservoir emission: reuse the exact pressure head already computed for each angular piece. Project the outline using one scale read and one sine/cosine pair per pressure-head query, preserving the component operations.
- SolidClearance: use local vertex times lossyScale directly for its zero-angle geometry, reading scale once per query. Hull containment and segment-distance calculations are unchanged.
- Cosmetics: skip creating or uploading a mesh when the cosmetic pool is empty; the first live cosmetic still creates the resources, and expiry disables the renderer.

Explicit fill readback, automatic readback cadence, material presets, reservoir clipping/integration, particle volumes, solver settings, and effects forces are unchanged.

Validation completed here: full experimental runtime compiled with Unity 6000.3.5f2's Roslyn compiler without diagnostics. Unity execution is controlled by the parent agent; no runtime performance improvement is claimed from source inspection alone. Existing pouring, effects, and calibration regression suites should be run against these changes.
