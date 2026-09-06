using UnityEngine;
using UnityEngine.UI;

namespace Slainte.Bartending
{
    // 바텐딩 월드 카메라의 렌더텍스처를 UI RawImage로 출력하는 뷰포트이자, 화면-월드 좌표
    // 변환의 단일 진입점(Active 정적 인스턴스). 포인터 클릭·드래그를 월드 좌표로 매핑하는
    // TryGetPointerWorldPosition 등 static 헬퍼들이 항상 Active를 통해 라우팅되므로,
    // 씬에 활성 뷰포트가 둘 이상 있으면 안 된다(OnEnable에서 마지막 것이 Active를 차지).
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RawImage))]
    public sealed class BartendingViewport : MonoBehaviour
    {
        public static BartendingViewport Active { get; private set; }

        [SerializeField] private Camera worldCamera;
        [SerializeField] private RawImage outputImage;
        [SerializeField] private Vector2Int renderSize = new Vector2Int(1700, 650);
        [SerializeField] private bool registerForInput = true;
        [SerializeField] private RectTransform bottomExtensionRect;

        private RenderTexture targetTexture;
        private float baseCameraOrthographicSize;
        private Vector3 baseCameraLocalPosition;
        private bool cameraFramingCaptured;
        private bool updatingViewportGeometry;
        private bool inputSuspended;

        public void SetInputSuspended(bool suspended)
        {
            inputSuspended = suspended;
        }

        public void Initialize(
            Camera camera,
            Vector2Int size,
            bool enableInputRegistration = true,
            RectTransform extendToBottom = null)
        {
            if (worldCamera != camera)
                cameraFramingCaptured = false;
            worldCamera = camera;
            renderSize = size;
            registerForInput = enableInputRegistration;
            bottomExtensionRect = extendToBottom;
            outputImage = GetComponent<RawImage>();
            CaptureBaseCameraFraming();
            Canvas.ForceUpdateCanvases();
            ApplyBottomExtension();

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
                CaptureBaseCameraFraming();
                ApplyBottomExtension();
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

        private void CaptureBaseCameraFraming()
        {
            if (cameraFramingCaptured || worldCamera == null)
                return;

            baseCameraOrthographicSize = worldCamera.orthographicSize;
            baseCameraLocalPosition = worldCamera.transform.localPosition;
            cameraFramingCaptured = true;
        }

        // 도구장 서랍처럼 뷰포트 아래로 화면이 확장되는 UI(bottomExtensionRect)가 있으면,
        // 뷰포트의 offsetMin.y를 그 확장분만큼 늘리고 월드 카메라의 orthographicSize/위치도
        // 같은 비율로 키워 확장된 영역까지 같은 카메라로 커버되게 한다(비율이 어긋나면 렌더 결과가
        // 늘어나 보이므로 heightRatio를 카메라와 뷰포트 양쪽에 동일하게 적용).
        private void ApplyBottomExtension()
        {
            RectTransform viewportRect = transform as RectTransform;
            RectTransform parentRect = viewportRect != null
                ? viewportRect.parent as RectTransform
                : null;
            if (viewportRect == null || parentRect == null || !cameraFramingCaptured)
                return;

            float baseHeight = Mathf.Max(1f, parentRect.rect.height);
            float bottomExtension = 0f;
            if (bottomExtensionRect != null)
            {
                Bounds extensionBounds =
                    RectTransformUtility.CalculateRelativeRectTransformBounds(
                        parentRect,
                        bottomExtensionRect);
                bottomExtension = Mathf.Max(
                    0f,
                    parentRect.rect.yMin - extensionBounds.min.y);
            }

            updatingViewportGeometry = true;
            Vector2 offsetMin = viewportRect.offsetMin;
            offsetMin.y = -bottomExtension;
            viewportRect.offsetMin = offsetMin;

            float heightRatio = (baseHeight + bottomExtension) / baseHeight;
            worldCamera.orthographicSize = baseCameraOrthographicSize * heightRatio;
            Vector3 cameraPosition = baseCameraLocalPosition;
            cameraPosition.y -= baseCameraOrthographicSize * bottomExtension / baseHeight;
            worldCamera.transform.localPosition = cameraPosition;
            updatingViewportGeometry = false;
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

        public static bool TryConvertActiveCanvasPixelsToWorld(
            Vector2 canvasPixelSize,
            out Vector2 worldSize)
        {
            if (Active != null)
                return Active.TryConvertCanvasPixelsToWorld(canvasPixelSize, out worldSize);

            worldSize = default;
            return false;
        }

        public bool TryConvertCanvasPixelsToWorld(
            Vector2 canvasPixelSize,
            out Vector2 worldSize)
        {
            worldSize = default;
            if (worldCamera == null
                || canvasPixelSize.x <= Mathf.Epsilon
                || canvasPixelSize.y <= Mathf.Epsilon
                || !TryGetScreenRect(out Rect viewportScreenRect))
            {
                return false;
            }

            Canvas canvas = outputImage != null
                ? outputImage.canvas
                : GetComponentInParent<Canvas>();
            float canvasScaleFactor = canvas != null
                ? Mathf.Max(Mathf.Epsilon, canvas.scaleFactor)
                : 1f;
            Vector2 screenPixelSize = canvasPixelSize * canvasScaleFactor;
            float worldHeight = worldCamera.orthographic
                ? worldCamera.orthographicSize * 2f
                : 0f;
            float worldWidth = worldHeight * worldCamera.aspect;
            if (worldWidth <= Mathf.Epsilon || worldHeight <= Mathf.Epsilon)
                return false;

            worldSize = new Vector2(
                worldWidth * screenPixelSize.x / viewportScreenRect.width,
                worldHeight * screenPixelSize.y / viewportScreenRect.height);
            return worldSize.x > Mathf.Epsilon && worldSize.y > Mathf.Epsilon;
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

        internal bool TryMapPointerToWorldWhileSuspended(
            Vector2 screenPosition,
            out Vector3 worldPosition)
        {
            return TryMapPointerToWorld(screenPosition, out worldPosition, true);
        }

        // 화면 좌표 → 뷰포트 RectTransform 로컬 좌표 → 정규화 뷰포트 좌표 → 월드 카메라 레이 →
        // z=0 평면과의 교차점 순으로 변환한다. 카메라 리그 이동 애니메이션 중에는 이 매핑이
        // 부정확해지므로 기본적으로 억제되며(inputSuspended), 이동 콜백 자체에서만
        // ignoreInputSuspension=true로 예외적으로 허용한다.
        private bool TryMapPointerToWorld(
            Vector2 screenPosition,
            out Vector3 worldPosition,
            bool ignoreInputSuspension = false)
        {
            if (inputSuspended && !ignoreInputSuspension)
            {
                worldPosition = default;
                return false;
            }

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

        // 아이템을 좌우로 굴릴 때, 그 경계(currentLeft/Right)가 뷰포트 화면 영역(패딩 적용)을
        // 벗어나지 않도록 요청된 이동량을 잘라낸다 — 화면 밖으로 밀려나가는 것을 방지.
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
            if (!updatingViewportGeometry && isActiveAndEnabled && worldCamera != null)
            {
                ApplyBottomExtension();
                BuildTargetTexture();
            }
        }
    }

    /// <summary>
    /// Matches a bartending world item's authored sprite rectangle to the same
    /// number of logical canvas pixels in the active Business bartending view.
    /// </summary>
    public static class BartendingNativeSpriteSizer
    {
        public static bool TryMatchRootToSprite(
            Transform itemRoot,
            SpriteRenderer referenceRenderer)
        {
            if (itemRoot == null
                || referenceRenderer == null
                || referenceRenderer.sprite == null)
            {
                return false;
            }

            return TryMatchRootToCanvasPixels(
                itemRoot,
                referenceRenderer,
                referenceRenderer.sprite.rect.size);
        }

        public static bool TryMatchRootToCanvasPixels(
            Transform itemRoot,
            SpriteRenderer referenceRenderer,
            Vector2 canvasPixelSize)
        {
            if (itemRoot == null
                || referenceRenderer == null
                || referenceRenderer.sprite == null
                || canvasPixelSize.x <= Mathf.Epsilon
                || canvasPixelSize.y <= Mathf.Epsilon
                || !BartendingViewport.TryConvertActiveCanvasPixelsToWorld(
                    canvasPixelSize,
                    out Vector2 targetWorldSize))
            {
                return false;
            }

            Bounds currentBounds = referenceRenderer.bounds;
            if (currentBounds.size.x <= Mathf.Epsilon
                || currentBounds.size.y <= Mathf.Epsilon)
            {
                return false;
            }

            Vector3 scale = itemRoot.localScale;
            scale.x *= targetWorldSize.x / currentBounds.size.x;
            scale.y *= targetWorldSize.y / currentBounds.size.y;
            itemRoot.localScale = scale;
            Physics2D.SyncTransforms();
            return true;
        }

        public static SpriteRenderer FindReferenceRenderer(GameObject root)
        {
            if (root == null)
                return null;

            SpriteRenderer[] renderers =
                root.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer renderer = renderers[i];
                if (renderer != null && renderer.enabled && renderer.sprite != null)
                    return renderer;
            }

            return null;
        }
    }
}
