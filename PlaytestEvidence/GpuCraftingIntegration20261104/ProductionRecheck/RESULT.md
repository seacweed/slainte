# Production BusinessScene recheck — 2026-11-04

Unity 6000.3.5f2, native open editor, DX12. Canonical scene: `Assets/_Project/Scenes/Production/BusinessScene.unity`.

- `playmode-automated-final-pass.txt`: final engine/GPU integration PASS. Authored customer order and feedback, fixed recipe composition for Good evaluation/payment, real GPU bottle emission/stock, stored filled-glass identity/content, RenderTexture rendering, duplicate guard, episode mapping and repeated cleanup.
- `manual-ui-pass.txt`: native mouse clicks, actual order dialogue, ingredient bottle selection, glass selection, serving-target submission, empty-glass Bad feedback and completion. No synthetic recipe fill in this mode.
- `production-good-cocktail.png`: fixed-input GPU rendering before submission. The transient pending-spawn message is from the intentional submission guard assertion; residual droplets are from the preceding bottle-emission test. This is not a visual-quality approval image.
- `after-protected-hashes.json`: Production scene, user-saved Comparison scene and latest pre-test autosave all unchanged.

Production fixes: inclusive viewport bounds, readiness after UI/supply binding, no session-completion callbacks into destroyed UI during Bootstrap teardown, game-only dispenser positioning and hidden diagnostic labels. Original GPU physics/shaders/prefabs and authored scenes preserved.

Earlier failed attempts are retained. Several were fixture timing/motion-history/default-payment assumptions; the viewport/readiness/teardown failures caused production fixes. `manual-ui-attempt01-frame-timeout.txt` and `automated-attempt08-frame-timeout.txt` were frame-count wait failures; waits now use real time.

Not verified: human pouring/stirring/shaking feel, full episode graph/day/rest flow, save-and-relaunch, other resolutions, sustained performance or player build. Original shader compiler warnings remain.

## Bottle/glass follow-up

`bottle-glass-final-pass.txt`: actual emitter/native physics/GPU transfer in Production, without `Fill` in the transfer segment. Representative catalog bottle into rock glass: 22 ml emitted, 22 ml received; ingredient identity, submission composition and stock decrement checked. Held solid-overlap policy, released contacts, overlapping item swap, filled-glass pickup and storage/restore followed by submission/payment also PASS.

`production-physical-pour.png` shows the fixed-pose physical pouring segment. It does not establish human mouse control quality or every bottle/glass combination.

The follow-up exposed a real storage defect: writing only an inactive Rigidbody pose allowed its old Transform pose to return on activation, shifting the pickup origin. Storage now synchronizes Transform before activation, including contained solids. The test now asserts restored pointer position and disables its Interactor's Update to exclude OS input. No production input/physics parameters changed.

`pour-attempt01-input-interference.txt` retains an initial hypothesis; input interference alone did not explain the failure. `pour-attempt02-restored-pickup-position.txt` retains the subsequent failure that led to the storage fix. `after-pour-protected-hashes.json` records preservation of scene/save files after the final follow-up run.
