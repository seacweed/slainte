using UnityEngine;

public class DialogueSkip : MonoBehaviour
{
    [SerializeField] private DialogueController dialogue;

    void Update()
    {
        if(Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space)) dialogue.Advance();
    }
}
