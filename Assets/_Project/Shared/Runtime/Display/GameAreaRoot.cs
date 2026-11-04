using UnityEngine;

namespace Slainte.Shared.Display
{
    // 각 Canvas 바로 아래에 두는 16:9(2560x1440 캔버스 단위) 고정 루트의 표식. CanvasScaler를 Expand로
    // 두면 어떤 화면 비율에서도 이 루트가 정확히 2560x1440으로 유지되므로, 그 아래 레이아웃은 QHD 기준
    // 좌표를 그대로 쓴다. "보이는 화면 크기"가 필요한 코드는 루트 캔버스 대신 GameAspect.FindGameAreaRect로 이 루트를 찾는다.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class GameAreaRoot : MonoBehaviour
    {
        public const string DefaultName = "GameArea16x9";

        // 컴포넌트를 처음 붙일 때 중앙 고정·기준 크기로 맞춰 둔다(스트레치 앵커면 Expand 여백까지 늘어나 의미가 없어짐).
        private void Reset() => ApplyLayout((RectTransform)transform);

        public static void ApplyLayout(RectTransform rect)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(GameAspect.ReferenceWidth, GameAspect.ReferenceHeight);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }
    }
}
