using UnityEngine;
using UnityEditor;
using System.IO;

public class ItemDataImporter : EditorWindow
{
    [MenuItem("Tools/Import Item Data (CSV)")]
    public static void ImportData()
    {
        // 1. 파일 선택 창 띄우기 (경로 하드코딩 제거!)
        string path = EditorUtility.OpenFilePanel("Select Item Data CSV", "Assets", "csv");

        // 취소 눌렀거나 경로가 없으면 종료
        if (string.IsNullOrEmpty(path)) return;

        // 2. 저장될 폴더 확인 (없으면 자동 생성)
        string savePath = "Assets/Resources/Items";
        if (!Directory.Exists(savePath)) 
        {
            Directory.CreateDirectory(savePath);
            AssetDatabase.Refresh(); // 폴더 만든거 인식시키기
        }

        // 3. 파일 읽기
        string[] lines = File.ReadAllLines(path);
        int successCount = 0;

        // 4. 파싱 및 생성
        // 첫 줄(헤더) 건너뛰고 i=1부터 시작
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrEmpty(line)) continue; // 빈 줄 무시

            // 콤마로 분리 (CSV)
            string[] data = line.Split(',');

            // 데이터 개수가 부족하면(빈 줄 등) 패스
            if (data.Length < 6) continue;

            // 순서: ID, Name, Category, Price, Desc, IconName
            string id = data[0].Trim();
            string itemName = data[1].Trim();
            string categoryStr = data[2].Trim();
            string priceStr = data[3].Trim();
            string desc = data[4].Trim();
            string iconName = data[5].Trim();

            // ScriptableObject 생성
            ItemData newItem = ScriptableObject.CreateInstance<ItemData>();
            newItem.itemName = itemName;
            newItem.desc = desc;

            // 숫자 파싱 (에러 방지)
            if (int.TryParse(priceStr, out int price)) newItem.price = price;

            // 카테고리 (공백 제거 후 파싱)
            categoryStr = categoryStr.Replace(" ", ""); // "Non Alcohol" -> "NonAlcohol"
            if (System.Enum.TryParse(categoryStr, true, out ItemData.ItemType categoryEnum))
            {
                newItem.category = categoryEnum;
            }
            else
            {
                Debug.LogWarning($"카테고리 불일치: {itemName} ({categoryStr}) -> 기본값 설정됨");
            }

            // 아이콘 찾기
            string[] guids = AssetDatabase.FindAssets($"{iconName} t:Sprite");
            if (guids.Length > 0)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                newItem.icon = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            }

            // 파일 저장
            string assetPathName = $"{savePath}/Item_{id}_{itemName}.asset";
            AssetDatabase.CreateAsset(newItem, assetPathName);
            successCount++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"성공! 총 {successCount}개의 아이템이 {savePath}에 생성되었습니다.");
    }
}