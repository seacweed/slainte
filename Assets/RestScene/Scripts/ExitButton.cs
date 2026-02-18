using UnityEngine;
using UnityEngine.EventSystems; // 이 네임스페이스가 필수입니다.

// 필요한 인터페이스 3개를 상속받습니다.
public class ExitButton : MonoBehaviour, IPointerClickHandler
{
    // 3. 마우스를 클릭했을 때 (OnMouseDown 대체)
    public void OnPointerClick(PointerEventData eventData)
    {
        // 좌클릭만 반응하게 하고 싶다면?
        if (eventData.button != PointerEventData.InputButton.Left) return;

        Debug.Log($"Clicked: {gameObject.name}");

        if (this.transform.parent != null)
        {
            // 팝업 토글 (Toggle)
            this.transform.parent.gameObject.SetActive(false);
        }
    }
}