using System;

// 제조 모드로 들어가기 직전에 재생할 연출(말풍선 정리, 카메라 축소 등). GameModeManager는 연출이 끝났다는
// 콜백을 받은 뒤에 모드를 바꾸며, 연출이 즉시 끝나면 콜백을 동기로 호출해도 된다.
public interface ICraftingEntryTransition
{
    void PlayBeforeCrafting(Action onComplete);
}
