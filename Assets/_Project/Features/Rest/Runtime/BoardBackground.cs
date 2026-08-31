using UnityEngine;
using UnityEngine.EventSystems;

public class BoardBackground : MonoBehaviour, IPointerClickHandler
{
    public EpisodeBoardManager manager; 

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            if (manager != null) manager.ResetBoard();
        }
    }
}