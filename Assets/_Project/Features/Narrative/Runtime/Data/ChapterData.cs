using UnityEngine;

[CreateAssetMenu(menuName = "Slainte/Chapter Data", fileName = "ChapterData_")]
public class ChapterData : ScriptableObject
{
    public string chapterId;
    public int    chapterIndex;
    public string chapterName;

    // chapterIndex가 가장 작은(첫 번째) 챕터를 반환. 새 게임 시작 시 기본 챕터를 정할 때 사용.
    public static ChapterData LoadFirst()
    {
        ChapterData[] all = Resources.LoadAll<ChapterData>("ChapterData");
        ChapterData first = null;
        foreach (ChapterData chapter in all)
        {
            if (chapter == null) continue;
            if (first == null || chapter.chapterIndex < first.chapterIndex)
                first = chapter;
        }
        return first;
    }
}
