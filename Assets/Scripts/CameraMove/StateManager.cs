using UnityEngine;

public enum ViewState { Front, Shelf }

public class StateManager : MonoBehaviour
{
    public static StateManager I { get; private set; }

    [SerializeField] GameObject frontPanel;
    //[SerializeField] GameObject drawerPanel;
    [SerializeField] GameObject shelfPanel;

    public ViewState Current { get; private set; } = ViewState.Front;

    void Awake()
    {
        I = this;
        Apply();
    }

    void Update()
    {
        //if (Input.GetKeyDown(KeyCode.S) && Current == ViewState.Front) Set(ViewState.Drawer);
        //if (Input.GetKeyDown(KeyCode.W) && Current == ViewState.Drawer) Set(ViewState.Front);

        if (Input.GetKeyDown(KeyCode.D) && Current == ViewState.Front) Set(ViewState.Shelf);
        if (Input.GetKeyDown(KeyCode.A) && Current == ViewState.Shelf) Set(ViewState.Front);
    }

    public void Set(ViewState s)
    {
        Current = s;
        Apply();
        // DragManager는 유지(드래그 들고 상태 전환 가능)
    }

    void Apply()
    {
        if (frontPanel) frontPanel.SetActive(Current == ViewState.Front);
        //if (drawerPanel) drawerPanel.SetActive(Current == ViewState.Drawer);
        if (shelfPanel) shelfPanel.SetActive(Current == ViewState.Shelf);
    }
}
