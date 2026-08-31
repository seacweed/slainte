using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Slainte.Bartending
{
    [MovedFrom(true, "", "Assembly-CSharp", "FullScreenQuad")]
    public class FullScreenQuad : MonoBehaviour
    {
        [Header("렌더링 정렬 설정")]
        public string sortingLayerName = "Default";
        public int sortingOrder = 12;

        private Renderer myRenderer;

        void Start()
        {
            myRenderer = GetComponent<Renderer>();
            ApplySortingSettings();
            ResizeToFullScreen();
        }

        private void OnValidate()
        {
            ApplySortingSettings();
        }

        void Update()
        {
            // 해상도 등이 바뀔 수 있으므로 실시간으로 해상도에 맞춰 크기 조정
            ResizeToFullScreen();
        }

        private void ApplySortingSettings()
        {
            if (myRenderer == null) myRenderer = GetComponent<Renderer>();
            if (myRenderer != null)
            {
                myRenderer.sortingLayerName = sortingLayerName;
                myRenderer.sortingOrder = sortingOrder;
            }
        }

        private void ResizeToFullScreen()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            // 1. 카메라의 세로 크기 (Size는 절반이므로 2배)
            float height = cam.orthographicSize * 2.0f;

            // 2. 카메라의 가로 크기 (세로 * 가로세로비율)
            float width = height * cam.aspect;

            // 3. SpriteRenderer가 장착되어 있는 경우, 기본 스프라이트 크기(1x1) 대비 스케일을 정확히 조절
            if (myRenderer is SpriteRenderer spriteRenderer)
            {
                if (spriteRenderer.sprite != null)
                {
                    // 스프라이트 고유의 유닛 크기를 고려하여 정밀 스케일 계산
                    float spriteWidth = spriteRenderer.sprite.rect.width / spriteRenderer.sprite.pixelsPerUnit;
                    float spriteHeight = spriteRenderer.sprite.rect.height / spriteRenderer.sprite.pixelsPerUnit;

                    if (spriteWidth > 0 && spriteHeight > 0)
                    {
                        transform.localScale = new Vector3(width / spriteWidth, height / spriteHeight, 1.0f);
                        return;
                    }
                }
            }

            // 일반 3D Mesh Quad 등의 경우 기본 스케일 그대로 적용
            transform.localScale = new Vector3(width, height, 1.0f);
        }

    }
}
