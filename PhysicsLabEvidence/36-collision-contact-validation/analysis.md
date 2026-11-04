Exit 1. The shader compiler error from attempt 35 is fixed, and surface/pouring tests pass. The first actual polygon contact assertion measured a CPU signed gap of -0.00900 after GPU resolution.

Next diagnostic: record Collider2D.GetShapes() radii and the exact start/end positions. Unity's polygon collision skin is separate from the authored vertices and the GPU particle radius. Compare the resolved position to the same geometric polygon after accounting for the measured skin; do not change global physics settings or the particle radius to conceal this difference.
