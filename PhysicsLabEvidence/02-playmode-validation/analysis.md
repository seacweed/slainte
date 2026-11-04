# Validation failure, not an engine crash

Actual process exit code: 1. No new crash dump was generated.

22 assertions passed, including actual Direct3D11 compute on the RTX 3070, unlimited rotation, pose retention, dynamic throwing, sealed GPU containment, and slot-free swapping with volume preservation.

The passive tip test observed only 3.3 degrees against a >5 degree requirement. The fixture positioned the jigger by transform origin, without accounting for source sprites' off-center collision geometry. Bottle prefabs inherit normalized click/body geometry with substantial sprite padding (for example item_1006's physical box spans local Y -1.688 to 0.231). Thus world transform Y is not a reliable impact height.

Changed test setup: place the bottle using its physical bottom, choose the tallest physical bottle, and aim the jigger's physical center at the bottle's upper body. Keep the >5 degree assertion. This tests a controlled off-center impact instead of weakening the success threshold. Production/shared data is unchanged.

Separate code fixes before next run: reset no longer depends on Build Settings scene registration; release velocity samples the requested pointer pose; initialize the swap shader's local delta to eliminate a compiler warning. Results and logs for this failed run remain intact.
