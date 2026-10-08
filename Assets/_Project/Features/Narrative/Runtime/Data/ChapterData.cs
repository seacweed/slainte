using System;
using Slainte.Content;
using UnityEngine;

[CreateAssetMenu(menuName = "Slainte/Chapter Data", fileName = "ChapterData_")]
public class ChapterData : ScriptableObject
{
    public string chapterId;
    public int    chapterIndex;
    public string chapterName;

    [Tooltip("챕터의 마지막 날. 이 날의 정산이 끝나면 엔딩 컷씬을 재생합니다. 0이면 자동으로 끝나지 않습니다.")]
    [Min(0)] public int lastDay;

    // chapterIndex가 가장 작은(첫 번째) 챕터를 반환. 새 게임 시작 시 기본 챕터를 정할 때 사용.
    public static ChapterData LoadFirst()
    {
        ChapterData first = null;
        foreach (ChapterData chapter in LoadAll())
        {
            if (chapter == null) continue;
            if (first == null || chapter.chapterIndex < first.chapterIndex)
                first = chapter;
        }
        return first;
    }

    public static ChapterData Find(string chapterId)
    {
        if (string.IsNullOrWhiteSpace(chapterId))
            return null;

        foreach (ChapterData chapter in LoadAll())
        {
            if (chapter != null
                && string.Equals(chapter.chapterId, chapterId, StringComparison.OrdinalIgnoreCase))
                return chapter;
        }
        return null;
    }

    private static ChapterData[] LoadAll()
    {
        return Resources.LoadAll<ChapterData>(ProjectResourcePaths.NarrativeChapters);
    }
}
