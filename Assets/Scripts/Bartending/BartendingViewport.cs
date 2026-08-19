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
        [SerializeField] private bool registerForInput = true;

        private RenderTexture targetTexture;

        public void Initialize(Camera camera, Vector2Int size, bool enableInputRegistration = true)
        {
            worldCamera = camera;
            renderSize = size;
            registerForInput = enableInputRegistration;
            outputImage = GetComponent<RawImage>();

            if (isActiveAndEnabled)
            {
                BuildTargetTexture();
            }
        }

        public void SetOutputVisible(bool visible)
        {
            if (outputImage == null)
                outputImage = GetComponent<RawImage>();

            if (outputImage != null)
                outputImage.enabled = visible;
        }

        private void OnEnable()
        {
            if (Application.isPlaying && registerForInput)
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
            if (Application.isPlaying)
                Destroy(targetTexture);
            else
                DestroyImmediate(targetTexture);
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

        public static bool TryGetInputScreenRect(out Rect screenRect)
        {
            if (Active != null && Active.TryGetScreenRect(out screenRect))
                return true;

            screenRect = new Rect(0f, 0f, Screen.width, Screen.height);
            return Screen.width > 0 && Screen.height > 0;
        }

        public static bool TryConvertClampedHorizontalScreenDelta(
            Camera fallbackCamera,
            Bounds worldBounds,
            float requestedScreenDeltaX,
            float screenPadding,
            out float worldDeltaX)
        {
            worldDeltaX = 0f;
            if (Mathf.Abs(requestedScreenDeltaX) <= Mathf.Epsilon)
                return false;

            if (Active != null && Active.worldCamera != null
                && Active.TryGetScreenRect(out Rect activeScreenRect))
            {
                Vector2 left = Active.MapWorldToPointer(new Vector3(
                    worldBounds.min.x,
                    worldBounds.center.y,
                    worldBounds.center.z));
                Vector2 right = Active.MapWorldToPointer(new Vector3(
                    worldBounds.max.x,
                    worldBounds.center.y,
                    worldBounds.center.z));
                float clampedDelta = ClampHorizontalScreenDelta(
                    requestedScreenDeltaX,
                    Mathf.Min(left.x, right.x),
                    Mathf.Max(left.x, right.x),
                    activeScreenRect,
                    screenPadding);
                if (Mathf.Abs(clampedDelta) <= Mathf.Epsilon)
                    return false;

                float normalizedDelta = clampedDelta / Mathf.Max(1f, activeScreenRect.width);
                if (Active.worldCamera.orthographic)
                {
                    float worldWidth = Active.worldCamera.orthographicSize
                        * 2f
                        * Active.worldCamera.aspect;
                    worldDeltaX = normalizedDelta * worldWidth;
                    return Mathf.Abs(worldDeltaX) > Mathf.Epsilon;
                }
            }

            if (fallbackCamera == null || Screen.width <= 0)
                return false;

            Vector2 fallbackLeft = fallbackCamera.WorldToScreenPoint(new Vector3(
                worldBounds.min.x,
                worldBounds.center.y,
                worldBounds.center.z));
            Vector2 fallbackRight = fallbackCamera.WorldToScreenPoint(new Vector3(
                worldBounds.max.x,
                worldBounds.center.y,
                worldBounds.center.z));
            Rect fallbackRect = new Rect(0f, 0f, Screen.width, Screen.height);
            float fallbackDelta = ClampHorizontalScreenDelta(
                requestedScreenDeltaX,
                Mathf.Min(fallbackLeft.x, fallbackRight.x),
                Mathf.Max(fallbackLeft.x, fallbackRight.x),
                fallbackRect,
                screenPadding);
            if (Mathf.Abs(fallbackDelta) <= Mathf.Epsilon)
                return false;

            Vector3 centerScreen = fallbackCamera.WorldToScreenPoint(worldBounds.center);
            Vector3 shiftedScreen = centerScreen + new Vector3(fallbackDelta, 0f, 0f);
            Vector3 centerWorld = fallbackCamera.ScreenToWorldPoint(centerScreen);
            Vector3 shiftedWorld = fallbackCamera.ScreenToWorldPoint(shiftedScreen);
            worldDeltaX = shiftedWorld.x - centerWorld.x;
            return Mathf.Abs(worldDeltaX) > Mathf.Epsilon;
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

        private bool TryGetScreenRect(out Rect screenRect)
        {
            screenRect = default;
            RectTransform rectTransform = transform as RectTransform;
            if (rectTransform == null)
                return false;

            Vector3[] corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            Vector2 first = RectTransformUtility.WorldToScreenPoint(GetCanvasCamera(), corners[0]);
            float xMin = first.x;
            float xMax = first.x;
            float yMin = first.y;
            float yMax = first.y;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(GetCanvasCamera(), corners[i]);
                xMin = Mathf.Min(xMin, point.x);
                xMax = Mathf.Max(xMax, point.x);
                yMin = Mathf.Min(yMin, point.y);
                yMax = Mathf.Max(yMax, point.y);
            }

            screenRect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            return screenRect.width > Mathf.Epsilon && screenRect.height > Mathf.Epsilon;
        }

        private static float ClampHorizontalScreenDelta(
            float requestedDelta,
            float currentLeft,
            float currentRight,
            Rect screenRect,
            float padding)
        {
            float safePadding = Mathf.Max(0f, padding);
            float minimumDelta = screenRect.xMin + safePadding - currentLeft;
            float maximumDelta = screenRect.xMax - safePadding - currentRight;
            if (minimumDelta > maximumDelta)
                return 0f;
            return Mathf.Clamp(requestedDelta, minimumDelta, maximumDelta);
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
