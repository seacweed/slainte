# Isolated development clients

These sources live outside `Assets` and are not compiled into the game or runtime prefab.
Copy them into `Assets/DevelopmentClient` of an isolated validation project when needed.
They use the current cohesive-liquid sandbox and contain no retired comparison solvers.

`FluidExperimentDevelopmentScenarios` owns automatic rest, pour, tilt, stir, sealed-shake and volume fixtures. Add it beside the sandbox component and call `StartScenario` or `StartVolumeCheck`. `Manual` restores the authored sandbox. These are controlled numerical fixtures; they do not prove normal play quality.

`FluidExperimentDevelopmentBenchmark` is an opt-in standalone-player workload activated by `-fluid-benchmark`. It samples current cohesive liquid only, using the runtime's opt-in timing event, two sealed vessels, a dedicated render target and one explicit camera render per frame. The exports retain the original separation of CPU submission, camera wall time, GPU surface timing and unsupported metrics. It is not a gameplay frame-rate claim.

Historical runs and source snapshots remain under `FluidGpuExperimentEvidence`; cleanup did not rewrite them.
