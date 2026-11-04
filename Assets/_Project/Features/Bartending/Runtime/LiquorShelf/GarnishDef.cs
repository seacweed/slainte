using UnityEngine;

// 가니쉬(필 등) 정의. LiquorBottleDef를 상속해 상점·술 선택 공간·GameProgress 재고를 병과 같은
// 경로로 그대로 쓰고, 꺼냈을 때만 병 대신 가니쉬 월드 아이템(GarnishController)을 만든다.
// 재고 단위는 개수다 — unitVolume 1 = 가니쉬 1개이므로 가격·기본 소지 개수가 곧 개당 값이 된다.
// CSV 임포터가 관리하지 않으므로 임포트 폴더 밖에 두고, 임포터는 카탈로그 재구성 때 이 타입을 보존한다.
[CreateAssetMenu(menuName = "Bartending/Garnish")]
public class GarnishDef : LiquorBottleDef
{
    [Header("Garnish")]
    [Tooltip("World image placed on the crafting table. Expected artwork suffix: _garni.")]
    public Sprite garnishSprite;
    [Tooltip("Scale applied after matching the sprite's native canvas size (bottles use 0.7).")]
    [Min(0.01f)] public float worldScale = 0.7f;

    private void Reset()
    {
        unitVolume = 1f;
        useContextImages = true;
    }
}
