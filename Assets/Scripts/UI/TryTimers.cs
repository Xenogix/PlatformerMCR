using TMPro;
using UnityEngine;

/// <summary>
/// The two CCTV-style try counters. RT (real time) is the wall clock of the level try —
/// it never stops, including while the timeline is open (thinking time costs real time).
/// TC (tape timecode) is game time derived from <see cref="GameClock.Tick"/>, so pausing
/// freezes it and a rewind winds it back — for free. Two distinct speedrun targets:
/// RT rewards planning, TC rewards an optimal traversal.
/// </summary>
public class TryTimers : MonoBehaviour
{
    [SerializeField] private TMP_Text rtLabel;
    [SerializeField] private TMP_Text tcLabel;

    [Header("Final times")]
    [Tooltip("Shown over the outro snow with the final RT/TC. A child of the noise object, " +
             "inactive by default so the intro snow stays clean. Optional.")]
    [SerializeField] private TMP_Text finalTimesLabel;

    private float _start;
    private float _elapsed;     // RT accumulated across running stretches (stopwatch)
    private bool _running = true;
    private int _tcStartTick = -1; // tick when play began (first resume) — TC counts from there
    private int _lastCs = -1;   // change-guards: only reformat + dirty a TMP label when its
    private int _lastTick = -1; // value moved (TC freezes entirely while the timeline is open)

    /// <summary>Formatted RT, e.g. "RT 00:01:23.45".</summary>
    public string RtText { get; private set; }

    /// <summary>Formatted TC, e.g. "TC 00:00:58:12" (last field = tick within the second).</summary>
    public string TcText { get; private set; }

    // Awake, not OnEnable: LevelTransition's OnEnable may call SetRunning(false) before
    // our own OnEnable would have run, and the stopwatch math needs a valid _start by then.
    private void Awake() => _start = Time.realtimeSinceStartup;

    private void OnEnable() { _lastCs = _lastTick = -1; }

    /// <summary>
    /// Stopwatch control, driven by <see cref="LevelTransition"/>: the try doesn't accrue
    /// time behind the transition snow (intro load static, outro results hold). RT stops
    /// accumulating; both displays freeze. TC starts counting from the first resume's tick.
    /// </summary>
    public void SetRunning(bool running)
    {
        if (running == _running) return;
        float now = Time.realtimeSinceStartup;
        if (running)
        {
            _start = now;
            if (_tcStartTick < 0 && GameClock.HasInstance) _tcStartTick = GameClock.Instance.Tick;
        }
        else
        {
            _elapsed += now - _start;
        }
        _running = running;
    }

    private void Update()
    {
        if (_running) RefreshTexts();
    }

    private void RefreshTexts()
    {
        float seconds = _elapsed + (_running ? Time.realtimeSinceStartup - _start : 0f);
        int cs = (int)(seconds * 100f);
        if (cs != _lastCs)
        {
            _lastCs = cs;
            RtText = "RT " + FormatRt(cs);
            if (rtLabel != null) rtLabel.text = RtText;
        }

        int tick = GameClock.HasInstance ? GameClock.Instance.Tick : 0;
        tick = Mathf.Max(0, tick - Mathf.Max(0, _tcStartTick)); // ticks since play began
        if (tick != _lastTick)
        {
            _lastTick = tick;
            TcText = "TC " + FormatTc(tick);
            if (tcLabel != null) tcLabel.text = TcText;
        }
    }

    /// <summary>
    /// Freeze the current times onto the final-times label (over the outro snow).
    /// Returns false when no label is wired, so the caller can keep its short outro.
    /// </summary>
    public bool ShowFinalTimes()
    {
        if (finalTimesLabel == null) return false;
        // Recompute at freeze time: this is called from a physics callback (FinishFlag's
        // trigger), which runs BEFORE this frame's Update — the cached strings are a frame old.
        RefreshTexts();
        finalTimesLabel.text = RtText + "\n" + TcText;
        finalTimesLabel.gameObject.SetActive(true);
        return true;
    }

    // HH:MM:SS.cc — centiseconds, so RT is the same width as TC and the two lines
    // stay column-aligned when right-aligned in the HUD. All fields derive from integer
    // centiseconds: float display-rounding would show 59.996s as "60.00".
    private static string FormatRt(int cs)
    {
        int h = cs / 360000;
        int m = cs / 6000 % 60;
        int s = cs / 100 % 60;
        int c = cs % 100;
        return $"{h:00}:{m:00}:{s:00}.{c:00}";
    }

    // Authentic CCTV timecode HH:MM:SS:FF — FF is the tick within the second, so the
    // readout is exactly tick-precise and visibly winds back during a rewind.
    private static string FormatTc(int tick)
    {
        int tps = GameClock.TicksPerSecond;
        int ff = tick % tps;
        int totalSeconds = tick / tps;
        int h = totalSeconds / 3600;
        int m = totalSeconds / 60 % 60;
        int s = totalSeconds % 60;
        return $"{h:00}:{m:00}:{s:00}:{ff:00}";
    }
}
