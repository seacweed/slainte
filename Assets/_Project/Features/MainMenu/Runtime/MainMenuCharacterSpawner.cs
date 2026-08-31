using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MainMenuCharacterSpawner : MonoBehaviour
{
    [Header("Spawn Area")]
    [SerializeField] private RectTransform spawnArea;
    [SerializeField] private float baselineY = 0f;

    [Header("Character Source")]
    [SerializeField] private Sprite[] characterSprites;
    [SerializeField] private bool spriteFacesRight = true;

    [Header("Population")]
    [SerializeField] private int maxConcurrentCharacters = 4;
    [SerializeField] private Vector2 spawnIntervalRange = new Vector2(1.5f, 3.5f);

    [Header("Movement")]
    [SerializeField] private Vector2 moveSpeedRange = new Vector2(80f, 160f);
    [SerializeField] private Vector2 bobbingAmplitudeRange = new Vector2(8f, 18f);
    [SerializeField] private Vector2 bobbingFrequencyRange = new Vector2(0.8f, 1.6f);

    private readonly List<MainMenuCharacterWalker> activeWalkers = new List<MainMenuCharacterWalker>();

    private void Start()
    {
        StartCoroutine(SpawnLoop());
    }

    private IEnumerator SpawnLoop()
    {
        while (true)
        {
            if (activeWalkers.Count < maxConcurrentCharacters && characterSprites != null && characterSprites.Length > 0)
                SpawnCharacter();

            float wait = Random.Range(spawnIntervalRange.x, spawnIntervalRange.y);
            yield return new WaitForSeconds(wait);
        }
    }

    private void SpawnCharacter()
    {
        float halfWidth = spawnArea.rect.width * 0.5f;
        bool leftToRight = Random.value < 0.5f;
        float startX = leftToRight ? -halfWidth : halfWidth;
        float endX = leftToRight ? halfWidth : -halfWidth;

        GameObject go = new GameObject("MainMenuCharacter", typeof(RectTransform), typeof(Image));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(spawnArea, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(startX, baselineY);

        Image image = go.GetComponent<Image>();
        image.sprite = characterSprites[Random.Range(0, characterSprites.Length)];
        image.SetNativeSize();
        image.raycastTarget = false;

        bool facingRightMovement = endX > startX;
        bool needsFlip = facingRightMovement != spriteFacesRight;
        Vector3 scale = rect.localScale;
        scale.x = needsFlip ? -Mathf.Abs(scale.x) : Mathf.Abs(scale.x);
        rect.localScale = scale;

        float speed = Random.Range(moveSpeedRange.x, moveSpeedRange.y);
        float bobbingAmplitude = Random.Range(bobbingAmplitudeRange.x, bobbingAmplitudeRange.y);
        float bobbingFrequency = Random.Range(bobbingFrequencyRange.x, bobbingFrequencyRange.y);

        MainMenuCharacterWalker walker = go.AddComponent<MainMenuCharacterWalker>();
        walker.Initialize(rect, startX, endX, baselineY, speed, bobbingAmplitude, bobbingFrequency, OnWalkerArrived);

        activeWalkers.Add(walker);
    }

    private void OnWalkerArrived(MainMenuCharacterWalker walker)
    {
        activeWalkers.Remove(walker);
        Destroy(walker.gameObject);
    }
}
