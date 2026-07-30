using TMPro;
using UnityEngine;

// 좌측 라벨 + 우측 값으로 구성된 정산 화면 한 줄.
// 음료 판매 줄(ScrollRect 안, 동적 생성)과 총 소득/보유 자산 줄(모니터 하단, 고정 배치)에서 공통으로 사용한다.
public class SettlementLineView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI labelText;
    [SerializeField] private TextMeshProUGUI valueText;

    public void SetLine(string label, string value)
    {
        if (labelText != null) labelText.text = label;
        if (valueText != null) valueText.text = value;
    }
}
