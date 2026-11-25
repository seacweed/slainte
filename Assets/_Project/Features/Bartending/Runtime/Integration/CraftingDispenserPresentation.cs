using Slainte.Bartending.FluidGpuExperiment;
using UnityEngine;

namespace Slainte.Bartending
{
    // A docked Canvas pose lets the hose fade through dialogue with the manufacturing camera asleep.
    internal sealed class CraftingDispenserPresentation
    {
        private const float FadeDuration = .18f;
        private readonly FluidExperimentDispenser dispenser;
        private readonly Camera camera;
        private readonly RectTransform viewport;
        private readonly CanvasGroup idleView;
        private readonly UnityEngine.UI.Image idleHandle, idleHose;
        private readonly Color handleColor, hoseStartColor, hoseEndColor;
        private Vector2 hoseStart, hoseEnd, handleSize, lastViewportSize;
        private float hoseWidth, opacity;
        private bool closingPose;

        public CraftingDispenserPresentation(FluidExperimentDispenser dispenser, Camera camera, RectTransform viewport)
        {
            this.dispenser = dispenser;
            this.camera = camera;
            this.viewport = viewport;
            handleColor = dispenser.bodyRenderer.color;
            hoseStartColor = dispenser.Hose.startColor;
            hoseEndColor = dispenser.Hose.endColor;

            var root = new GameObject("DockedDispenser", typeof(RectTransform), typeof(CanvasGroup));
            var rect = (RectTransform)root.transform;
            rect.SetParent(viewport, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            idleView = root.GetComponent<CanvasGroup>();
            idleView.blocksRaycasts = false;
            idleView.interactable = false;
            idleView.alpha = 0;
            idleHose = CreateImage("Hose");
            idleHose.color = hoseStartColor;
            idleHose.rectTransform.pivot = new Vector2(0, .5f);
            idleHandle = CreateImage("Handle");
            idleHandle.color = handleColor;
            CapturePose();
        }

        public void CaptureClosingPose()
        {
            CapturePose();
            closingPose = true;
        }

        public void Update(bool visible, bool worldRendering, float deltaTime)
        {
            opacity = Mathf.MoveTowards(opacity, visible ? 1f : 0f, deltaTime / FadeDuration);
            float alpha = Mathf.SmoothStep(0, 1, opacity);
            idleView.alpha = worldRendering ? 0f : alpha;
            if (worldRendering)
            {
                var renderer = dispenser.bodyRenderer;
                renderer.color = WithOpacity(handleColor, alpha);
                dispenser.Hose.startColor = WithOpacity(hoseStartColor, alpha);
                dispenser.Hose.endColor = WithOpacity(hoseEndColor, alpha);
            }
            else
            {
                if (closingPose && opacity <= 0)
                {
                    CapturePose();
                    closingPose = false;
                }
                if (lastViewportSize != viewport.rect.size) ResizeIdlePose();
            }
        }

        private UnityEngine.UI.Image CreateImage(string name)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
            var image = child.GetComponent<UnityEngine.UI.Image>();
            image.rectTransform.SetParent(idleView.transform, false);
            image.raycastTarget = false;
            return image;
        }

        private void CapturePose()
        {
            var renderer = dispenser.bodyRenderer;
            if (renderer.sprite == null) return;
            var art = renderer.sprite.bounds;
            var bodyTransform = renderer.transform;
            var extent = Vector2.Scale(art.size, bodyTransform.lossyScale);
            float worldHeight = camera.orthographicSize * 2f;
            float worldWidth = worldHeight * camera.aspect;
            handleSize = new Vector2(extent.x / worldWidth, extent.y / worldHeight);
            idleHandle.sprite = renderer.sprite;
            idleHandle.rectTransform.anchorMin = idleHandle.rectTransform.anchorMax =
                camera.WorldToViewportPoint(bodyTransform.TransformPoint(art.center));
            idleHandle.rectTransform.localRotation = bodyTransform.rotation;
            hoseStart = camera.WorldToViewportPoint(dispenser.hoseAnchor);
            var line = dispenser.Hose;
            hoseEnd = camera.WorldToViewportPoint(line.GetPosition(line.positionCount - 1));
            hoseWidth = line.startWidth / worldHeight;
            ResizeIdlePose();
        }

        private void ResizeIdlePose()
        {
            lastViewportSize = viewport.rect.size;
            idleHandle.rectTransform.sizeDelta = Vector2.Scale(handleSize, lastViewportSize);
            var hose = idleHose.rectTransform;
            Vector2 span = Vector2.Scale(hoseEnd - hoseStart, lastViewportSize);
            hose.anchorMin = hose.anchorMax = hoseStart;
            hose.sizeDelta = new Vector2(span.magnitude, hoseWidth * lastViewportSize.y);
            hose.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg);
        }

        private static Color WithOpacity(Color color, float alpha)
        {
            color.a *= alpha;
            return color;
        }
    }
}
