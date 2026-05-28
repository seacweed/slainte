using UnityEngine;
using UnityEngine.UI;

namespace Slainte.Bartending
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RawImage))]
    public sealed class BartendingViewport : MonoBehaviour
    {
        public static BartendingViewport Active { get; private set; }

        [SerializeField] private Camera worldCamera;
        [SerializeField] private RawImage outputImage;
        [SerializeField] private Vector2Int renderSize = new Vector2Int(1700, 650);

        private RenderTexture targetTexture;

        public void Initialize(Camera camera, Vector2Int size)
        {
            worldCamera = camera;
            renderSize = size;
            outputImage = GetComponent<RawImage>();

            if (isActiveAndEnabled)
            {
                BuildTargetTexture();
            }
        }

        private void OnEnable()
        {
            Active = this;
            if (worldCamera != null)
            {
                BuildTargetTexture();
            }
        }

        private void OnDisable()
        {
            if (Active == this)
            {
                Active = null;
            }

            ReleaseTargetTexture();
        }

        private void BuildTargetTexture()
        {
            ReleaseTargetTexture();

            int width = renderSize.x;
            int height = renderSize.y;

            RectTransform rectTransform = transform as RectTransform;
            if (rectTransform != null)
            {
                Vector3[] corners = new Vector3[4];
                rectTransform.GetWorldCorners(corners);
                Canvas canvas = rectTransform.GetComponentInParent<Canvas>();
                Camera canvasCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay 
                    ? canvas.worldCamera 
                    : null;
                Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(canvasCamera, corners[0]);
                Vector2 topRight = RectTransformUtility.WorldToScreenPoint(canvasCamera, corners[2]);
                int pixelWidth = Mathf.RoundToInt(Mathf.Abs(topRight.x - bottomLeft.x));
                int pixelHeight = Mathf.RoundToInt(Mathf.Abs(topRight.y - bottomLeft.y));

                if (pixelWidth > 16 && pixelHeight > 16)
                {
                    width = pixelWidth;
                    height = pixelHeight;
                }
            }

            width = Mathf.Max(16, width);
            height = Mathf.Max(16, height);
            targetTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "BusinessBartendingView",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            targetTexture.Create();

            if (outputImage == null)
            {
                outputImage = GetComponent<RawImage>();
            }

            outputImage.texture = targetTexture;
            worldCamera.targetTexture = targetTexture;
        }

        private void ReleaseTargetTexture()
        {
            if (worldCamera != null && worldCamera.targetTexture == targetTexture)
            {
                worldCamera.targetTexture = null;
            }

            if (outputImage != null && outputImage.texture == targetTexture)
            {
                outputImage.texture = null;
            }

            if (targetTexture == null)
            {
                return;
            }

            targetTexture.Release();
            Destroy(targetTexture);
            targetTexture = null;
        }

        public static bool TryGetPointerWorldPosition(Camera fallbackCamera, Vector2 screenPosition, out Vector3 worldPosition)
        {
            if (Active != null)
            {
                return Active.TryMapPointerToWorld(screenPosition, out worldPosition);
            }

            if (fallbackCamera == null)
            {
                worldPosition = default;
                return false;
            }

            worldPosition = fallbackCamera.ScreenToWorldPoint(screenPosition);
            worldPosition.z = 0f;
            return true;
        }

        public static Vector2 GetPointerScreenPosition(Camera fallbackCamera, Vector3 worldPosition)
        {
            if (Active != null && Active.worldCamera != null)
            {
                return Active.MapWorldToPointer(worldPosition);
            }

            return fallbackCamera != null
                ? (Vector2)fallbackCamera.WorldToScreenPoint(worldPosition)
                : Vector2.zero;
        }

        public bool TryMapRectToWorld(RectTransform sourceRect, out Vector3 center, out Vector2 size)
        {
            if (sourceRect == null)
            {
                center = default;
                size = default;
                return false;
            }

            Vector3[] corners = new Vector3[4];
            sourceRect.GetWorldCorners(corners);
            Camera sourceCamera = GetCanvasCamera(sourceRect);
            Vector2 bottomLeftScreen = RectTransformUtility.WorldToScreenPoint(sourceCamera, corners[0]);
            Vector2 topRightScreen = RectTransformUtility.WorldToScreenPoint(sourceCamera, corners[2]);

            if (!TryMapPointerToWorld(bottomLeftScreen, out Vector3 bottomLeft) ||
                !TryMapPointerToWorld(topRightScreen, out Vector3 topRight))
            {
                center = default;
                size = default;
                return false;
            }

            center = (bottomLeft + topRight) * 0.5f;
            center.z = 0f;
            size = new Vector2(
                Mathf.Abs(topRight.x - bottomLeft.x),
                Mathf.Abs(topRight.y - bottomLeft.y));
            return true;
        }

        private bool TryMapPointerToWorld(Vector2 screenPosition, out Vector3 worldPosition)
        {
            RectTransform rectTransform = transform as RectTransform;
            if (worldCamera == null || rectTransform == null)
            {
                worldPosition = default;
                return false;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rectTransform, screenPosition, GetCanvasCamera(), out Vector2 localPosition))
            {
                worldPosition = default;
                return false;
            }

            Rect rect = rectTransform.rect;
            if (!rect.Contains(localPosition))
            {
                worldPosition = default;
                return false;
            }

            Vector2 normalized = Rect.PointToNormalized(rect, localPosition);
            Ray pointerRay = worldCamera.ViewportPointToRay(normalized);
            Plane interactionPlane = new Plane(Vector3.forward, Vector3.zero);
            if (!interactionPlane.Raycast(pointerRay, out float distance))
            {
                worldPosition = default;
                return false;
            }

            worldPosition = pointerRay.GetPoint(distance);
            worldPosition.z = 0f;
            return true;
        }

        private Vector2 MapWorldToPointer(Vector3 worldPosition)
        {
            RectTransform rectTransform = (RectTransform)transform;
            Vector3 viewportPosition = worldCamera.WorldToViewportPoint(worldPosition);
            Rect rect = rectTransform.rect;
            Vector2 localPosition = new Vector2(
                Mathf.Lerp(rect.xMin, rect.xMax, viewportPosition.x),
                Mathf.Lerp(rect.yMin, rect.yMax, viewportPosition.y));

            return RectTransformUtility.WorldToScreenPoint(
                GetCanvasCamera(), rectTransform.TransformPoint(localPosition));
        }

        private static Camera GetCanvasCamera(RectTransform rectTransform)
        {
            Canvas canvas = rectTransform.GetComponentInParent<Canvas>();
            return canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera;
        }

        private Camera GetCanvasCamera()
        {
            Canvas canvas = outputImage != null ? outputImage.canvas : GetComponentInParent<Canvas>();
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            return canvas.worldCamera;
        }

        private void OnRectTransformDimensionsChange()
        {
            if (isActiveAndEnabled && worldCamera != null)
            {
                BuildTargetTexture();
            }
        }
    }
}
