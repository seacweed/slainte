using System;
using UnityEngine;

public class MainMenuCharacterWalker : MonoBehaviour
{
    private RectTransform rect;
    private float startX;
    private float endX;
    private float baselineY;
    private float speed;
    private float bobbingAmplitude;
    private float bobbingFrequency;
    private float bobbingPhase;
    private Action<MainMenuCharacterWalker> onArrived;
    private bool moveRight;

    public void Initialize(
        RectTransform rectTransform,
        float startX,
        float endX,
        float baselineY,
        float speed,
        float bobbingAmplitude,
        float bobbingFrequency,
        Action<MainMenuCharacterWalker> onArrived)
    {
        rect = rectTransform;
        this.startX = startX;
        this.endX = endX;
        this.baselineY = baselineY;
        this.speed = speed;
        this.bobbingAmplitude = bobbingAmplitude;
        this.bobbingFrequency = bobbingFrequency;
        this.onArrived = onArrived;
        bobbingPhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        moveRight = endX > startX;
    }

    private void Update()
    {
        Vector2 position = rect.anchoredPosition;
        float delta = speed * Time.deltaTime;
        float newX = moveRight ? position.x + delta : position.x - delta;
        bool arrived = moveRight ? newX >= endX : newX <= endX;
        if (arrived)
            newX = endX;

        float bobbingOffset = Mathf.Sin((Time.time + bobbingPhase) * bobbingFrequency * Mathf.PI * 2f) * bobbingAmplitude;
        rect.anchoredPosition = new Vector2(newX, baselineY + bobbingOffset);

        if (arrived)
            onArrived?.Invoke(this);
    }
}
