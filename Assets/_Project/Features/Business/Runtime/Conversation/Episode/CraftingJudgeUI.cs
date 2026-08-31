using UnityEngine;
using UnityEngine.UI;

public class CraftingJudgeUI : MonoBehaviour
{
    [SerializeField] private Button goodJobButton;
    [SerializeField] private Button midIceButton;
    [SerializeField] private Button midGlassButton;
    [SerializeField] private Button midIceGlassButton;
    [SerializeField] private Button midWrongMenuButton;
    [SerializeField] private Button badJobButton;
    [SerializeField] private EpisodeRunner episodeRunner;

    private void Awake()
    {
        goodJobButton.onClick.AddListener(() => episodeRunner.NotifyCraftingCompleted(CraftingJobResult.Good));
        midIceButton.onClick.AddListener(() => episodeRunner.NotifyCraftingCompleted(CraftingJobResult.MidIce));
        midGlassButton.onClick.AddListener(() => episodeRunner.NotifyCraftingCompleted(CraftingJobResult.MidGlass));
        midIceGlassButton.onClick.AddListener(() => episodeRunner.NotifyCraftingCompleted(CraftingJobResult.MidIceGlass));
        midWrongMenuButton.onClick.AddListener(() => episodeRunner.NotifyCraftingCompleted(CraftingJobResult.MidWrongMenu));
        badJobButton.onClick.AddListener(() => episodeRunner.NotifyCraftingCompleted(CraftingJobResult.Bad));
    }
}
