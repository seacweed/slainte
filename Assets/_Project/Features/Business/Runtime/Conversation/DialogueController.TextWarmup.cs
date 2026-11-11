using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public partial class DialogueController
{
    private Coroutine _textWarmupRoutine;

    // Preparation is optional: display never waits for this coroutine. Missing glyphs
    // still follow TMP's normal fallback path if a line is shown before preparation ends.
    public void PrewarmLines(IReadOnlyList<DialogueLine> lines)
    {
        StopRoutine(ref _textWarmupRoutine);
        if (lines != null && lines.Count > 0 && isActiveAndEnabled)
            _textWarmupRoutine = StartCoroutine(PrewarmLineGlyphs(lines));
    }

    private IEnumerator PrewarmLineGlyphs(IReadOnlyList<DialogueLine> lines)
    {
        // Keep initial character spawning and font work on separate frames.
        yield return null;
        var visited = new HashSet<(TMP_FontAsset font, int unicode)>();
        int generatedThisFrame = 0;
        double frameStarted = Time.realtimeSinceStartupAsDouble;
        foreach (DialogueLine line in lines)
        {
            if (line == null) continue;
            // Names and body can use different primary/fallback font assets.
            for (int field = 0; field < 2; field++)
            {
                TMP_Text text = field == 0 ? nameText : bodyText;
                string content = field == 0 ? line.speakerName : line.text;
                TMP_FontAsset font = text != null ? text.font : null;
                if (font == null || string.IsNullOrEmpty(content)) continue;
                for (int i = 0; i < content.Length; i++)
                {
                    char c = content[i];
                    if (text.richText && c == '<')
                    {
                        int close = content.IndexOf('>', i);
                        if (close >= 0) { i = close; continue; }
                    }
                    if (char.IsControl(c)) continue;
                    int unicode = c;
                    if (char.IsHighSurrogate(c) && i + 1 < content.Length && char.IsLowSurrogate(content[i + 1]))
                        unicode = char.ConvertToUtf32(c, content[++i]);
                    if (!visited.Add((font, unicode))) continue;
                    // TMP attempts the dynamic primary font before its fallbacks.
                    // A glyph found only in a fallback must not skip primary preparation.
                    if (unicode <= char.MaxValue && font.HasCharacter((char)unicode, searchFallbacks: false)) continue;
                    using (DialogueDiagnostics.Measure(DialogueDiagnostics.Stage.TextWarmupGlyph))
                    {
                        if (unicode <= char.MaxValue)
                            font.HasCharacter((char)unicode, searchFallbacks: true, tryAddCharacter: true);
                        else
                            font.HasCharacters(char.ConvertFromUtf32(unicode), out uint[] missing, searchFallbacks: true, tryAddCharacter: true);
                    }
                    generatedThisFrame++;
                    // This is a cooperative budget: a single native glyph operation
                    // cannot be interrupted. Never batch the whole sentence in one frame.
                    if (generatedThisFrame >= 2 || Time.realtimeSinceStartupAsDouble - frameStarted >= .004)
                    {
                        yield return null;
                        generatedThisFrame = 0;
                        frameStarted = Time.realtimeSinceStartupAsDouble;
                    }
                }
            }
        }
        _textWarmupRoutine = null;
    }

    private void PrewarmNextIndicator()
    {
        if (nextIndicator == null) return;
        using var measurement = DialogueDiagnostics.Measure(DialogueDiagnostics.Stage.IndicatorWarmup);
        // Awake has already hidden the bubble. Exercise TMP's first-enable/font
        // feature path, then restore the indicator before any frame can be drawn.
        bool wasActive = nextIndicator.gameObject.activeSelf;
        bool wasEnabled = nextIndicator.enabled;
        nextIndicator.gameObject.SetActive(true);
        nextIndicator.ForceMeshUpdate(ignoreActiveState: true);
        nextIndicator.enabled = wasEnabled;
        nextIndicator.gameObject.SetActive(wasActive);
    }
}
