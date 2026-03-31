using UnityEngine;

public class DialogueSkip : MonoBehaviour
{
    [SerializeField] private DialogueController dialogue;
    [SerializeField] private EpisodeDialogueRunner episodeRunner;

    void Update()
    {
        bool pressed = Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space);
        if (!pressed) return;

        if (episodeRunner != null && episodeRunner.IsRunning)
        {
            episodeRunner.OnAdvanceInput();
            return;
        }

        if (dialogue != null)
            dialogue.Advance();
    }
}
