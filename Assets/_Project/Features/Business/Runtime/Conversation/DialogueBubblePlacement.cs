// 말풍선을 어떤 모양·위치로 띄울지. DialogueController는 화자가 누구인지·카메라가 어디를 보는지
// 모르므로, 대사를 띄울 때마다 등록된 리졸버(DialogueFocusPresenter)에게 배치를 묻는다.
public enum DialogueBubbleStyle
{
    // 무대 위 캐릭터: 윗변 고정, 위쪽 가운데 arrow_front.
    Character,
    // 주인공: 화면 가운데, 아랫변 고정, 아래쪽 가운데 arrow_back.
    Protagonist,
    // 무대에 없는 화자·내레이션: 주인공 위치에 화살표 없이.
    Offstage
}

public struct DialogueBubblePlacement
{
    public DialogueBubbleStyle Style;
    // 화면 가운데 기준 가로 위치(캔버스 픽셀). 화면 밖으로 나가지 않게 하는 제한은 DialogueController가 한다.
    public float ScreenX;

    public static DialogueBubblePlacement Centered(DialogueBubbleStyle style)
    {
        return new DialogueBubblePlacement { Style = style, ScreenX = 0f };
    }
}

public interface IDialogueBubblePlacementResolver
{
    DialogueBubblePlacement Resolve(DialogueLine line);
}
