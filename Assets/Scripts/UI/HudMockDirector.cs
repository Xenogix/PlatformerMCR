using UnityEngine;
using UnityEngine.InputSystem;

// Interactive stand-in for the Director, to test the HUD by hand before the clone system exists.
// Drives the same public LevelHud API the real Director will use. Keyboard (new Input System):
//   1 Play   2 Pause   3 Rewind   4 Record
//   C  add a clone lane (+ "CLONE CREATED" toast)
//   S  "SAVE LOADED" toast
//   E  drop an event tick on the active lane at the playhead
//   ←/→ scrub the playhead (also grows the active P1 lane)
//   Backspace  reset the timeline
//
// Use EITHER this OR HudDemoDriver (auto-animation), not both — disable one. Remove both once
// the real Director drives LevelHud.
[RequireComponent(typeof(LevelHud))]
public class HudMockDirector : MonoBehaviour
{
    [SerializeField] private LevelHud hud;
    [Tooltip("Playhead units (0..1) per second while scrubbing with the arrow keys.")]
    [SerializeField] private float scrubSpeed = 0.35f;
    [Tooltip("Draw the keyboard legend overlay (IMGUI, not through the CRT).")]
    [SerializeField] private bool showLegend = true;

    private static readonly Color PlayerColor = new(0.55f, 0.9f, 1f);
    private static readonly Color CloneColor = new(1f, 0.78f, 0.45f);

    private int _activeLane = -1;
    private int _cloneCount;
    private float _playhead;

    private void Awake()
    {
        if (hud == null) hud = GetComponent<LevelHud>();
    }

    private void Start()
    {
        if (hud == null) return;
        hud.SetTransport(TransportState.Pause);
        if (hud.Timeline != null)
            _activeLane = hud.Timeline.AddLane("P1", PlayerColor);
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || hud == null) return;

        if (kb.digit1Key.wasPressedThisFrame) hud.SetTransport(TransportState.Play);
        if (kb.digit2Key.wasPressedThisFrame) hud.SetTransport(TransportState.Pause);
        if (kb.digit3Key.wasPressedThisFrame) hud.SetTransport(TransportState.Rewind);
        if (kb.digit4Key.wasPressedThisFrame) hud.SetTransport(TransportState.Record);

        if (kb.sKey.wasPressedThisFrame) hud.ShowToast("SAVE LOADED");
        if (kb.cKey.wasPressedThisFrame) AddClone();

        var timeline = hud.Timeline;
        if (timeline == null) return;

        if (kb.eKey.wasPressedThisFrame && _activeLane >= 0) timeline.AddEvent(_activeLane, _playhead);
        if (kb.backspaceKey.wasPressedThisFrame) ResetTimeline();

        float dir = (kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.leftArrowKey.isPressed ? 1f : 0f);
        if (dir != 0f)
        {
            _playhead = Mathf.Clamp01(_playhead + dir * scrubSpeed * Time.unscaledDeltaTime);
            timeline.SetPlayhead(_playhead);
            if (_activeLane >= 0) timeline.SetLaneProgress(_activeLane, _playhead);
        }
    }

    private void AddClone()
    {
        var timeline = hud.Timeline;
        if (timeline == null) return;

        _cloneCount++;
        int lane = timeline.AddLane($"C{_cloneCount}", CloneColor);
        timeline.SetLaneProgress(lane, 1f); // a finished recording
        hud.ShowToast("CLONE CREATED");
    }

    private void ResetTimeline()
    {
        var timeline = hud.Timeline;
        if (timeline == null) return;

        timeline.ClearLanes();
        _cloneCount = 0;
        _playhead = 0f;
        timeline.SetPlayhead(0f);
        _activeLane = timeline.AddLane("P1", PlayerColor);
    }

    private void OnGUI()
    {
        if (!showLegend) return;
        GUI.Label(new Rect(10, 10, 640, 80),
            "HUD MOCK  —  1 Play   2 Pause   3 Rewind   4 Record\n" +
            "C clone (+toast)   S save toast   E event tick\n" +
            "←/→ scrub playhead   Backspace reset timeline");
    }
}
