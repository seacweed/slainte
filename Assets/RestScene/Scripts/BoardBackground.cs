using UnityEngine;
using UnityEngine.EventSystems;

// SituationBoard_UI 패널에 붙여주세요.
public class BoardBackground : MonoBehaviour, IPointerClickHandler
{
    public EpisodeBoardManager manager; // QuestBoardManager가 있는 부모나 본인 연결

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            // 사진이 아닌 배경을 클릭했으므로 모든 걸 초기화
            manager.ResetBoard();
        }
    }
}