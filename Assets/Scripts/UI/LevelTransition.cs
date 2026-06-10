using System;
using System.Collections;
using UnityEngine;

public class LevelTransition : MonoBehaviour
{
    [SerializeField] private GameObject noiseObject;
    [SerializeField] private float staticDuration = 0.2f;
    [Tooltip("Outro hold when the final RT/TC times are shown over the snow (TryTimers present).")]
    [SerializeField] private float resultHoldDuration = 2f;

    private Coroutine _running;

    // The HUD's timers (optional — null when the scene has none). The snow gates the try's
    // timing: timers stop while static is shown (intro load, outro results hold).
    private TryTimers _timers;
    private TryTimers Timers => _timers != null ? _timers : _timers = FindAnyObjectByType<TryTimers>();

    private void OnEnable()
    {
        // First frame is full snow so the scene load is hidden behind it; hold, then cut to picture.
        ShowStatic(true);
        _running = StartCoroutine(HoldThen(() => ShowStatic(false), staticDuration));
    }

    private void OnDisable()
    {
        if (_running != null) StopCoroutine(_running);
        _running = null;
    }

    // Snaps static on, holds, then runs the load callback (the outgoing scene stays on snow until swap).
    public void PlayOutroThenLoad(Action load)
    {
        if (noiseObject == null)
        {
            // No static configured: just load, preserving previous behaviour.
            load?.Invoke();
            return;
        }

        // Final times over the snow: the label lives with TryTimers in the HUD, so no
        // cross-prefab wiring here — just ask. Without timers, keep the short channel-snap.
        // Freeze BEFORE ShowStatic stops the timers, so the splash captures the touch moment.
        float hold = Timers != null && Timers.ShowFinalTimes() ? resultHoldDuration : staticDuration;

        if (_running != null) StopCoroutine(_running);
        ShowStatic(true);
        _running = StartCoroutine(HoldThen(() => load?.Invoke(), hold));
    }

    private IEnumerator HoldThen(Action done, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        _running = null;
        done?.Invoke();
    }

    private void ShowStatic(bool on)
    {
        if (noiseObject != null) noiseObject.SetActive(on);
        Timers?.SetRunning(!on); // the try's clock only runs while the picture is up
    }
}
