# Attempt 04: validation failed, exit code 1

No Unity startup crash. GPU nozzle-to-glass capture succeeded (9 ml), with emitted volume conserved. The negative rotation assertion failed after the test teleported a held bottle: the interactor retained the pre-teleport angle. BeginRotation now reads the held body's current angle before starting a new gesture, preserving externally updated poses too.

Image inspection found no visible liquid in the explicitly rendered camera captures. Those captures ran before LateUpdate, where procedural draws were queued. Rendering was changed to register per camera through beginCameraRendering. The subsequent attempt's camera captures were visually inspected and showed the liquid stream and contained particles. No automated image comparison is claimed. This was a changed rendering path, not a repeat of an unchanged failed invocation.
