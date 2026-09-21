# Original implementation reproduces the regression

The direct Play Mode callback executed on RTX 3070 / Direct3D11. The copied runtime was the original 20826b0 implementation. Unity exited 1 because the isolation assertion failed; this is the expected baseline defect, not a passing run.

Two identical stationary runs matched exactly (position/velocity RMS 0). Moving only the distant held solid changed the remote vessels' liquid: drag position RMS 0.277184576 and velocity RMS 0.601512849; rotation position RMS 0.360767722 and velocity RMS 0.571906447. Global substeps increased from 2 to 21 (drag) and 23 (rotation).

The next invocation replaces the copied runtime/shader with the fixed fluid schedule, local swept contact, and time-normalized damping implementation. The same remote comparison and positive-contact checks remain in the harness.
