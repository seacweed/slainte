using Slainte.Shared.Lifecycle;
using UnityEngine;

// 하루 진행 순서(Rest → 영업 → 정산 → Rest)와 정산 직후 재생할 컷씬을 결정하는 컨트롤러.
// 하루는 항상 영업 하나로 이루어지고, 에피소드는 영업 슬롯 안에서 인카운터로만 실행된다.
// Rest의 시작 버튼/영업 종료 처리는 GameManager를 직접 호출하지 않고 이 클래스를 거친다.
public class DayFlowController : MonoSingleton<DayFlowController>
{
    [SerializeField] private string endingEpisodeId; // '지구로' 에피소드의 episodeId. Inspector에서 설정.

    private string _pendingSettlementCutsceneId;

    // Rest에서 "영업 시작"을 눌렀을 때 호출.
    public void StartBusinessDay()
    {
        GameProgress.Instance?.AdvanceDay();
        _pendingSettlementCutsceneId = null;
        EnterBusiness();
    }

    // MainMenu "게임 시작"에서 최초 1회 호출. Day 1은 이미 1이므로 AdvanceDay를 호출하지 않는다.
    public void StartFirstDay()
    {
        _pendingSettlementCutsceneId = CutsceneIds.FathersNote;
        EnterBusiness();
    }

    // 영업 인카운터 에피소드가 끝날 때마다 EpisodeManager가 호출.
    public void NotifyEpisodeCompleted(string episodeId)
    {
        if (!string.IsNullOrEmpty(endingEpisodeId) && episodeId == endingEpisodeId)
            _pendingSettlementCutsceneId = CutsceneIds.Ending;
    }

    // 정산 종료 시 SettlementManager가 재생할 컷씬 id를 가져가면서 비운다. 없으면 null.
    public string ConsumePendingSettlementCutscene()
    {
        string id = _pendingSettlementCutsceneId;
        _pendingSettlementCutsceneId = null;
        return id;
    }

    // 영업(손님 슬롯 전부)이 끝났을 때 호출.
    public void OnBusinessCompleted()
    {
        // 챕터 마지막 날이면 다음 챕터 기획이 생기기 전까지 정산 뒤 엔딩 컷씬 → 메인메뉴로 보낸다.
        // 엔딩은 다른 예약 컷씬보다 우선한다(엔딩 뒤로는 진행할 화면이 없으므로).
        if (IsLastDayOfCurrentChapter())
            _pendingSettlementCutsceneId = CutsceneIds.Ending;

        GoToSettlement();
    }

    public void GoToSettlement()
    {
        GameManager.Instance?.ChangeState(GameState.Settlement);
    }

    private void EnterBusiness()
    {
        GameManager.Instance?.ChangeState(GameState.Business);
    }

    private static bool IsLastDayOfCurrentChapter()
    {
        GameProgress progress = GameProgress.Instance;
        if (progress == null)
            return false;

        ChapterData chapter = ChapterData.Find(progress.CurrentChapterId);
        return chapter != null && chapter.lastDay > 0 && progress.CurrentDay >= chapter.lastDay;
    }
}
