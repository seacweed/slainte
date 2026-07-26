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
        DayFlowManager.Instance.StartInitialEpisode("StrangeCoin_0");
    }
}
