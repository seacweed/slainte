# Unity2DFluidSim reference

Reference: https://github.com/Zombie1111/Unity2DFluidSim

Pinned commit: `73abe0aeae35ba717146e0195f2c2c3b73663e89`.

David Westberg's MIT-licensed source informed the q-squared density, q-cubed near-density,
summed pair pressure, and approaching-pair radial viscosity in the experimental GPU solver.
The accompanying MIT notice is preserved in `Unity2DFluidSim-LICENSE.md`.

This experiment is an adaptation, not identical execution of the original CPU Burst job.
It uses per-particle GPU gather, world-unit seconds, volume weights, bounded negative pressure,
and safety limits. It does not reproduce sequential neighbor-write order or duplicated density
contributions. Existing Slainte collision geometry, ownership, ml accounting and interaction
are reused in a separate copy. Its multicolor density surface adapts the reference's overlap
and threshold idea to URP; the original hardcoded blue postprocess is not used.
