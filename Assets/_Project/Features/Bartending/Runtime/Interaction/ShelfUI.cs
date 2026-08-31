using UnityEngine;

public class ShelfUI : MonoBehaviour
{
    [SerializeField] Transform content;
    [SerializeField] UIItemDraggable itemPrefab;
    [SerializeField] ItemDef[] items;

    void Start()
    {
        foreach (var it in items)
        {
            var ui = Instantiate(itemPrefab, content);
            ui.Bind(it);

            // Bottle은 실제 이동
            ui.spawnMode = it.dragMovesObject ? DragSpawnMode.Move : DragSpawnMode.Copy;
        }
    }
}
