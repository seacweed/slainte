using UnityEngine;
using UnityEngine.UI;

public class MainMenuManager : MonoBehaviour
{
    [SerializeField] private Button startButton;

    private void Awake()
    {
        if (startButton != null)
            startButton.onClick.AddListener(OnStartClicked);
    }

    private void OnStartClicked()
    {
        GameProgress gp = GameProgress.Instance;
        if (gp != null && string.IsNullOrEmpty(gp.CurrentChapterId))
        {
            ChapterData firstChapter = ChapterData.LoadFirst();
            if (firstChapter != null)
                gp.SetCurrentChapter(firstChapter.chapterId);
        }

        CutsceneManager.Instance?.Play(CutsceneIds.Today, () =>
            DayFlowController.Instance?.StartFirstDay());
    }
}
