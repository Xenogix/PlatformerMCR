using System;
using System.Collections;
using UnityEngine;

public class LevelTransition : MonoBehaviour
{
    [SerializeField] private GameObject noiseObject;
    [SerializeField] private float staticDuration = 0.2f;

    private Coroutine _running;

    private void OnEnable()
    {
        // First frame is full snow so the scene load is hidden behind it; hold, then cut to picture.
        ShowStatic(true);
        _running = StartCoroutine(HoldThen(() => ShowStatic(false)));
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

        if (_running != null) StopCoroutine(_running);
        ShowStatic(true);
        _running = StartCoroutine(HoldThen(() => load?.Invoke()));
    }

    private IEnumerator HoldThen(Action done)
    {
        float t = 0f;
        while (t < staticDuration)
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
    }
}
