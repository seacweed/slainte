using UnityEngine;

public class DrawerUI : MonoBehaviour
{
    [SerializeField] Transform content;
    [SerializeField] UIItemDraggable itemPrefab;
    [SerializeField] ItemDef[] items; // Glass, Tool 넣기

    void Start()
    {
        foreach (var it in items)
        {
            var ui = Instantiate(itemPrefab, content);
            ui.Bind(it);

            ui.spawnMode = DragSpawnMode.Copy;
        }
    }
}