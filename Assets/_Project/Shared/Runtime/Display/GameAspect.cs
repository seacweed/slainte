using System;
using UnityEngine;

namespace Slainte.Shared.Display
{
    // 게임 화면은 어떤 해상도에서도 16:9(기준 2560x1440) 영역 하나로 고정되고, 남는 부분은
    // LetterboxOverlay가 검은 띠로 덮는다. 화면 끝 판정·정규화 좌표 변환처럼 "실제 게임이 그려지는
    // 영역"이 필요한 코드는 Screen.width/height 대신 이 클래스의 GameScreenRect를 기준으로 삼을 것.
    public static class GameAspect
    {
        public const float ReferenceWidth  = 2560f;
        public const float ReferenceHeight = 1440f;
        public const float TargetAspect    = ReferenceWidth / ReferenceHeight;

        // 화면 해상도(창 크기)가 바뀐 직후 한 번 발생. 픽셀 크기에 맞춰 만든 RenderTexture 등을 다시 만들 때 구독한다.
        public static event Action ScreenSizeChanged;

        // GetWorldCorners 호출마다 배열을 새로 만들지 않도록 재사용(UI 갱신은 메인 스레드에서만 일어남).
        private static readonly Vector3[] s_corners = new Vector3[4];

        // 현재 화면에서 16:9 게임 영역이 차지하는 픽셀 사각형(좌하단 원점, Screen 좌표계).
        public static Rect GameScreenRect => GetGameScreenRect(Screen.width, Screen.height);

        public static Rect GetGameScreenRect(int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
                return new Rect(0f, 0f, Mathf.Max(0, screenWidth), Mathf.Max(0, screenHeight));

            float screenAspect = (float)screenWidth / screenHeight;

            // 화면이 더 넓으면(21:9 등) 높이를 꽉 채우고 좌우에 띠, 더 좁으면(16:10·4:3) 너비를 채우고 상하에 띠.
            if (screenAspect > TargetAspect)
            {
                float width = screenHeight * TargetAspect;
                return new Rect((screenWidth - width) * 0.5f, 0f, width, screenHeight);
            }

            float height = screenWidth / TargetAspect;
            return new Rect(0f, (screenHeight - height) * 0.5f, screenWidth, height);
        }

        // 0~1 정규화 좌표를 게임 영역 기준 픽셀 사각형으로 변환(x,y = 좌하단, z,w = 크기).
        public static Rect NormalizedToScreenRect(Vector4 normalized)
        {
            Rect area = GameScreenRect;
            return new Rect(
                area.x + Mathf.Clamp01(normalized.x) * area.width,
                area.y + Mathf.Clamp01(normalized.y) * area.height,
                Mathf.Clamp01(normalized.z) * area.width,
                Mathf.Clamp01(normalized.w) * area.height);
        }

        // 주어진 UI가 속한 16:9 게임 영역 루트를 찾는다. 아직 GameAreaRoot로 감싸지 않은 캔버스는
        // 기존처럼 루트 캔버스를 돌려줘 동작이 바뀌지 않게 한다.
        public static RectTransform FindGameAreaRect(Transform from)
        {
            if (from == null)
                return null;

            GameAreaRoot area = from.GetComponentInParent<GameAreaRoot>(true);
            if (area != null)
                return (RectTransform)area.transform;

            Canvas canvas = from.GetComponentInParent<Canvas>(true);
            return canvas != null && canvas.rootCanvas != null
                ? canvas.rootCanvas.transform as RectTransform
                : null;
        }

        // Overlay 캔버스 팝업을 대상의 오른쪽에 붙이고, 게임 영역 오른쪽 끝을 넘으면 왼쪽으로 뒤집는다.
        // gap은 캔버스 단위(QHD 픽셀)라 lossyScale을 곱해 실제 픽셀로 바꾼다 — 그래야 해상도가 낮아져도
        // 팝업과의 간격 비율이 유지되고, rect.width(캔버스 단위)와 화면 픽셀을 섞어 비교하는 실수도 막는다.
        public static void PlaceBesideTarget(RectTransform popup, RectTransform target, float gap = 10f)
        {
            if (popup == null || target == null)
                return;

            target.GetWorldCorners(s_corners);
            Vector3 rightCenter = (s_corners[2] + s_corners[3]) * 0.5f;

            float scale = popup.lossyScale.x;
            float gapPixels = gap * scale;
            float popupWidthPixels = popup.rect.width * scale;

            if (rightCenter.x + gapPixels + popupWidthPixels > GameScreenRect.xMax)
            {
                Vector3 leftCenter = (s_corners[0] + s_corners[1]) * 0.5f;
                popup.pivot = new Vector2(1f, 0.5f);
                popup.position = leftCenter - new Vector3(gapPixels, 0f, 0f);
            }
            else
            {
                popup.pivot = new Vector2(0f, 0.5f);
                popup.position = rightCenter + new Vector3(gapPixels, 0f, 0f);
            }
        }

        internal static void NotifyScreenSizeChanged() => ScreenSizeChanged?.Invoke();
    }
}
