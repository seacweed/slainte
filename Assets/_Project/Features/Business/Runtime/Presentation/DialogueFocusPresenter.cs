using UnityEngine;

// 대사가 나올 때마다 화자를 찾아 카메라 포커스와 말풍선 배치를 함께 정하는 연결부.
// - 무대 위 캐릭터: 그 슬롯 중심으로 확대·이동하고, 말풍선은 이동이 끝난 뒤 화면에서 본 슬롯 중심에 둔다.
// - 주인공: 카메라는 직전 포커스를 유지하고, 말풍선은 화면 가운데(arrow_back).
// - 무대에 없는 화자·내레이션: 카메라 유지, 화면 가운데에 화살표 없이.
// 확대는 제조에 들어갈 때만 푼다 — 대사창이 닫히거나 주문 사이·인카운터 종료·하루 끝 정산으로 넘어갈 때는
// 확대를 유지하는 편이 자연스럽다. 제조 진입 때는 말풍선을 먼저 페이드 아웃한 뒤 카메라를 축소하고,
// 그동안(제조 중 포함)에는 대사가 나와도 확대하지 않는다.
// DialogueController가 카메라·무대를 직접 알지 않도록 배치 계산은 모두 여기서 한다.
[DisallowMultipleComponent]
public class DialogueFocusPresenter : MonoBehaviour, IDialogueBubblePlacementResolver, ICraftingEntryTransition
{
    [Header("References (empty = found in the scene)")]
    [SerializeField] private DialogueController dialogue;
    [SerializeField] private CharacterStage characterStage;
    [SerializeField] private FrontCameraRig cameraRig;
    [SerializeField] private GameModeManager modeManager;

    [Header("Protagonist")]
    [Tooltip("CharacterData key of the first-person protagonist. Matched case-insensitively.")]
    [SerializeField] private string protagonistKey = "shaun";

    // 슬롯은 리그(FrontWorld) 아래에서 움직이지 않으므로, 리그 로컬 X를 한 번만 계산해 둔다.
    // 확대·이동 중에 다시 계산하지 않아 목표가 흔들리지 않고, 매 대사마다 변환 비용도 들지 않는다.
    private float[] _slotCenterX;
    private bool[] _slotCenterValid;

    private void Awake()
    {
        if (dialogue == null) dialogue = FindFirstObjectByType<DialogueController>();
        if (characterStage == null) characterStage = FindFirstObjectByType<CharacterStage>();
        if (cameraRig == null) cameraRig = FindFirstObjectByType<FrontCameraRig>();
        if (modeManager == null) modeManager = FindFirstObjectByType<GameModeManager>();

        dialogue?.SetPlacementResolver(this);
        modeManager?.SetCraftingEntryTransition(this);
    }

    private void OnDestroy()
    {
        dialogue?.SetPlacementResolver(null);
        modeManager?.SetCraftingEntryTransition(null);
    }

    // 제조 진입 연출: 말풍선 페이드 아웃 → 카메라 QHD 전체로 축소 → (GameModeManager가) 제조 모드 전환.
    public void PlayBeforeCrafting(System.Action onComplete)
    {
        if (dialogue != null)
            dialogue.FadeOutAndHide(() => ResetCamera(onComplete));
        else
            ResetCamera(onComplete);
    }

    private void ResetCamera(System.Action onComplete)
    {
        if (cameraRig != null)
            cameraRig.ResetView(onComplete);
        else
            onComplete?.Invoke();
    }

    public DialogueBubblePlacement Resolve(DialogueLine line)
    {
        bool canFocus = CanFocus();

        if (IsProtagonist(line))
        {
            if (canFocus) cameraRig.KeepFocus();
            return DialogueBubblePlacement.Centered(DialogueBubbleStyle.Protagonist);
        }

        if (TryResolveSlot(line, out int slotIndex) && TryGetSlotCenterX(slotIndex, out float slotX))
        {
            if (canFocus) cameraRig.FocusOn(slotX);
            float screenX = cameraRig != null ? cameraRig.ProjectToScreenAtTarget(slotX) : slotX;
            return new DialogueBubblePlacement { Style = DialogueBubbleStyle.Character, ScreenX = screenX };
        }

        if (canFocus) cameraRig.KeepFocus();
        return DialogueBubblePlacement.Centered(DialogueBubbleStyle.Offstage);
    }

    private bool CanFocus()
    {
        return cameraRig != null
            && (modeManager == null
                || (modeManager.CurrentMode != GameMode.CraftingMode && !modeManager.IsCraftingRequestPending));
    }

    private bool IsProtagonist(DialogueLine line)
    {
        if (line == null || string.IsNullOrWhiteSpace(protagonistKey))
            return false;

        if (!string.IsNullOrWhiteSpace(line.speakerKey))
            return string.Equals(line.speakerKey.Trim(), protagonistKey, System.StringComparison.OrdinalIgnoreCase);

        // 키 없는 기존 대사 에셋은 주인공 표시 이름으로 판별한다.
        return characterStage != null
            && !string.IsNullOrWhiteSpace(line.speakerName)
            && characterStage.TryGetDialogueIdentity(protagonistKey, out string protagonistName, out _)
            && string.Equals(protagonistName, line.speakerName, System.StringComparison.Ordinal);
    }

    private bool TryResolveSlot(DialogueLine line, out int slotIndex)
    {
        slotIndex = -1;
        if (line == null || characterStage == null)
            return false;

        if (!string.IsNullOrWhiteSpace(line.speakerKey)
            && characterStage.TryGetSlotIndex(line.speakerKey.Trim(), out slotIndex))
        {
            return true;
        }

        return characterStage.TryGetSlotIndexByDisplayName(line.speakerName, out slotIndex);
    }

    private bool TryGetSlotCenterX(int slotIndex, out float localX)
    {
        localX = 0f;
        if (characterStage == null || cameraRig == null || slotIndex < 0)
            return false;

        if (_slotCenterX == null || _slotCenterX.Length != characterStage.SlotCount)
        {
            int count = characterStage.SlotCount;
            _slotCenterX = new float[count];
            _slotCenterValid = new bool[count];
            for (int i = 0; i < count; i++)
                _slotCenterValid[i] = characterStage.TryGetSlotCenterX(i, cameraRig.FrontWorld, out _slotCenterX[i]);
        }

        if (slotIndex >= _slotCenterX.Length || !_slotCenterValid[slotIndex])
            return false;

        localX = _slotCenterX[slotIndex];
        return true;
    }
}
