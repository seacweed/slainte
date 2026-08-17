using UnityEngine;

namespace Slainte.Bartending
{
    [DisallowMultipleComponent]
    public sealed class IceBinController : MonoBehaviour
    {
        private static Sprite fallbackSprite;

        private BusinessBartendingSettings settings;
        private Transform cubeParent;
        private Collider2D inputCollider;
        private Camera inputCamera;
        private int renderLayer;
        private float itemScale = 1f;
        private bool previewOnly;

        public static IceBinController Active { get; private set; }
        public bool IsPreviewOnly => previewOnly;

        public static IceBinController Create(
            Transform parent,
            BusinessBartendingSettings settings,
            int renderLayer,
            float itemScale,
            bool previewOnly)
        {
            if (parent == null || settings == null)
                return null;

            GameObject instance = settings.iceBinPrefab != null
                ? Instantiate(settings.iceBinPrefab, parent)
                : CreateFallbackObject(parent, settings);
            instance.name = "IceBin";
            instance.transform.localPosition = settings.iceBinPosition;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * itemScale;
            BartendingSessionBuilder.SetLayerRecursively(instance, renderLayer);

            IceBinController controller = instance.GetComponent<IceBinController>();
            if (controller == null)
                controller = instance.AddComponent<IceBinController>();
            controller.Initialize(settings, parent, renderLayer, itemScale, previewOnly);
            return controller;
        }

        public bool ContainsWorldPoint(Vector2 worldPoint)
        {
            return inputCollider != null
                && inputCollider.enabled
                && inputCollider.OverlapPoint(worldPoint);
        }

        private void Initialize(
            BusinessBartendingSettings sessionSettings,
            Transform sessionCubeParent,
            int sessionRenderLayer,
            float sessionItemScale,
            bool isPreview)
        {
            settings = sessionSettings;
            cubeParent = sessionCubeParent;
            renderLayer = sessionRenderLayer;
            itemScale = sessionItemScale;
            previewOnly = isPreview;
            inputCollider = GetComponent<Collider2D>();
            if (inputCollider == null)
            {
                BoxCollider2D box = gameObject.AddComponent<BoxCollider2D>();
                box.size = new Vector2(settings.iceBinWidth, settings.iceBinHeight);
                box.isTrigger = true;
                inputCollider = box;
            }

            inputCamera = Camera.main;
            CreateDecorativeIce();
            enabled = !previewOnly;
            if (!previewOnly && Application.isPlaying)
                Active = this;
        }

        private void Update()
        {
            if (previewOnly || settings == null)
                return;

            if (Input.GetMouseButtonDown(0)
                && TryGetPointerWorld(out Vector2 pointerDown)
                && ContainsWorldPoint(pointerDown))
            {
                IceCubeController cube = IceCubeController.Create(
                    cubeParent,
                    settings,
                    renderLayer,
                    itemScale,
                    false);
                if (cube != null)
                    cube.BeginDrag(pointerDown, true);
            }
        }

        private bool TryGetPointerWorld(out Vector2 pointer)
        {
            if (BartendingViewport.TryGetPointerWorldPosition(
                    inputCamera,
                    Input.mousePosition,
                    out Vector3 world))
            {
                pointer = world;
                return true;
            }

            pointer = default;
            return false;
        }

        private void CreateDecorativeIce()
        {
            if (transform.Find("IceDecoration_0") != null)
                return;

            float width = Mathf.Max(0.2f, settings.iceBinWidth);
            float height = Mathf.Max(0.2f, settings.iceBinHeight);
            for (int i = 0; i < 3; i++)
            {
                GameObject decoration = new GameObject("IceDecoration_" + i);
                decoration.transform.SetParent(transform, false);
                decoration.transform.localPosition = new Vector3(
                    Mathf.Lerp(-width * 0.25f, width * 0.25f, i / 2f),
                    height * 0.12f + (i % 2) * height * 0.12f,
                    -0.01f);
                decoration.transform.localRotation = Quaternion.Euler(0f, 0f, -12f + i * 12f);
                decoration.transform.localScale = Vector3.one * settings.iceCubeSize * 0.8f;
                SpriteRenderer renderer = decoration.AddComponent<SpriteRenderer>();
                renderer.sprite = GetFallbackSprite();
                renderer.color = new Color(0.72f, 0.94f, 1f, 0.9f);
                renderer.sortingOrder = 16;
                decoration.layer = renderLayer;
            }
        }

        private void OnDisable()
        {
            if (Active == this)
                Active = null;
        }

        private static GameObject CreateFallbackObject(
            Transform parent,
            BusinessBartendingSettings settings)
        {
            GameObject root = new GameObject("IceBin");
            root.transform.SetParent(parent, false);

            GameObject visual = new GameObject("BinVisual");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = new Vector3(
                settings.iceBinWidth,
                settings.iceBinHeight,
                1f);
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = GetFallbackSprite();
            renderer.color = new Color(0.1f, 0.35f, 0.45f, 0.9f);
            renderer.sortingOrder = 14;

            BoxCollider2D collider = root.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(settings.iceBinWidth, settings.iceBinHeight);
            collider.isTrigger = true;
            return root;
        }

        internal static Sprite GetFallbackSprite()
        {
            if (fallbackSprite == null)
            {
                Texture2D texture = Texture2D.whiteTexture;
                fallbackSprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    texture.width);
                fallbackSprite.name = "BartendingFallbackSquare";
            }

            return fallbackSprite;
        }
    }
}
