using UnityEngine;
using UnityEngine.UI;

public class CraftingJudgeUI : MonoBehaviour
{
    [SerializeField] private Button goodJobButton;
    [SerializeField] private Button badJobButton;
    [SerializeField] private EpisodeRunner episodeRunner;

    private void Awake()
    {
        goodJobButton.onClick.AddListener(() => episodeRunner.NotifyCraftingCompleted(true));
        badJobButton.onClick.AddListener(() => episodeRunner.NotifyCraftingCompleted(false));
    }
}
