using UnityEngine;

// Temporary preview driver for the HUD. Lets you see the whole OSD (identity, transport, toast,
// timeline) animate through the CRT before the clone/Director system exists. It just calls the
// same public LevelHud API the real game state will use.
//
// Remove or disable this component once the Director drives LevelHud for real.
[RequireComponent(typeof(LevelHud))]
public class HudDemoDriver : MonoBehaviour
{
    [SerializeField] private LevelHud hud;
    [Tooltip("Seconds for one full demo loop of the playhead / transport cycle.")]
    [SerializeField] private float loopDuration = 8f;

    private int _player = -1;
    private int _clone = -1;
    private float _t;
    private bool _toastShown;

    private void Start()
    {
        if (hud == null) hud = GetComponent<LevelHud>();
        if (hud == null) return;

        var timeline = hud.Timeline;
        if (timeline != null)
        {
            _clone = timeline.AddLane("C1", new Color(1f, 0.78f, 0.45f));   // a finished clone recording
            _player = timeline.AddLane("P1", new Color(0.55f, 0.9f, 1f));   // the character you control
            timeline.SetLaneProgress(_clone, 1f);
            timeline.AddEvent(_clone, 0.2f);
            timeline.AddEvent(_clone, 0.62f);
            timeline.AddEvent(_player, 0.35f);
        }
    }

    private void Update()
    {
        if (hud == null) return;

        _t += Time.unscaledDeltaTime;
        float k = loopDuration > 0f ? Mathf.Repeat(_t, loopDuration) / loopDuration : 0f;

        var timeline = hud.Timeline;
        if (timeline != null)
        {
            timeline.SetPlayhead(k);
            timeline.SetLaneProgress(_player, k); // current character records up to the playhead
        }

        // Cycle the transport indicator so all states are visible during the loop.
        TransportState state =
            k < 0.55f ? TransportState.Record :
            k < 0.70f ? TransportState.Pause :
            k < 0.88f ? TransportState.Rewind :
                        TransportState.Play;
        hud.SetTransport(state);

        // Fire a one-off toast partway through the loop.
        if (!_toastShown && k > 0.55f) { hud.ShowToast("CLONE CREATED"); _toastShown = true; }
        if (k < 0.05f) _toastShown = false; // re-arm each loop
    }
}
