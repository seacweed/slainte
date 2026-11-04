This attempt failed (exit 1) and is not successful GPU validation.

The surface test observed zero rendered pixels because the new closed-vessel HLSL code used the reserved keyword `point` as a variable. Editor.log preserves the shader compiler errors at lines 544 and 554, followed by invalid kernel errors. No native crash dump was produced.

The next attempt renames the variable and validates every compute kernel before allocating or dispatching GPU buffers, so a compiler failure is reported at initialization rather than as a later rendering failure. All original logs and screenshots from this failed attempt are retained.
