using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class RewindDirector : MonoBehaviour
{
    private enum Mode { Playing, Scrubbing }

    [Tooltip("Echo prefab (required) — a Player variant carrying ClonePlayback + RigidbodyChannel + RewindableEntity and NO PlayerCommandInvoker (translucent).")]
    [SerializeField] private GameObject echoPrefab;

    [Tooltip("Initial scrub speed (game ticks per real second) when you start moving the playhead — kept low for precision.")]
    [SerializeField, Min(1f)] private float scrubTicksPerSecond = 50f;

    [Tooltip("Scrub speed reached after holding the direction (ticks per real second) — for fast long rewinds.")]
    [SerializeField, Min(1f)] private float scrubMaxTicksPerSecond = 300f;

    [Tooltip("Seconds of holding the direction to ramp from the initial to the max scrub speed.")]
    [SerializeField, Min(0f)] private float scrubAccelSeconds = 1.2f;

    [Tooltip("Saturation/Value of the auto-generated per-clone colours (hue is spread by the golden " +
             "ratio so each clone is visually distinct). Used for both its timeline lane and its echo albedo.")]
    [SerializeField, Range(0f, 1f)] private float cloneColorSaturation = 0.65f;
    [SerializeField, Range(0f, 1f)] private float cloneColorValue = 1f;

    [Tooltip("Lane colour for the live player's own lane.")]
    [SerializeField] private Color playerColor = new(0.55f, 0.9f, 1f);

    [Tooltip("How long (seconds) the one-shot gameplay controls reminder toast stays up at level start.")]
    [SerializeField, Min(0f)] private float startupHintSeconds = 3.5f;

    private PlayerCommandInvoker livePlayer;
    private LevelHud hud;

    private InputActionMap playerMap;   // gameplay (Move/Jump); disabled while scrubbing
    private InputActionMap timelineMap; // scrub controls; enabled only while scrubbing

    private InputAction openAction;          // (Player map) opens the timeline
    private InputAction scrubAction;         // (Timeline map) X axis scrubs the playhead
    private InputAction confirmCloneAction;  // (Timeline map) commits the rewind AND spawns a clone
    private InputAction confirmRewindAction; // (Timeline map) commits the rewind with no clone
    private InputAction cancelAction;        // (Timeline map) aborts the scrub, returning to the present

    private Mode mode = Mode.Playing;
    private float scrubTickF;       // fractional accumulator for a smooth scrub
    private int scrubTick;
    private float scrubHeldTime;    // how long the scrub direction has been held (drives acceleration)
    private int playerLane = -1;    // Player's timeline lane
    private int echoSeq;

    // One per clone: its timeline lane and the absolute-tick window it acts over ([spawn, end]),
    // used to colour that lane's "life" slice when the timeline is opened.
    private readonly List<(int lane, int start, int end)> clones = new();

    private void Start()
    {
        livePlayer = FindAnyObjectByType<PlayerCommandInvoker>();
        hud = FindAnyObjectByType<LevelHud>();

        var actions = InputSystem.actions;
        playerMap = actions != null ? actions.FindActionMap("Player") : null;
        timelineMap = actions != null ? actions.FindActionMap("Timeline") : null;

        // The open key lives in the gameplay map (so it works during play); everything else lives
        // in the Timeline map (so it works only while scrubbing, with the gameplay map disabled).
        openAction = actions != null ? actions.FindAction("Player/Timeline") : null;
        scrubAction = timelineMap?.FindAction("Scrub");
        confirmCloneAction = timelineMap?.FindAction("ConfirmClone");
        confirmRewindAction = timelineMap?.FindAction("ConfirmRewind");
        cancelAction = timelineMap?.FindAction("Cancel");
        if (openAction == null)
            Debug.LogError("RewindDirector: 'Player/Timeline' input action not found — add it to the project-wide actions.");
        if (timelineMap == null)
            Debug.LogError("RewindDirector: 'Timeline' action map not found — add it to the project-wide actions.");

        timelineMap?.Disable(); // scrub controls are inert during normal play; enabled on EnterScrub

        hud?.SetTransport(TransportState.Play);
        hud?.SetTimelineVisible(false); // shown only while the timeline is open for scrubbing
        hud?.Hint?.ShowGameplay();      // gameplay controls during normal play
        if (hud != null && hud.Timeline != null)
        {
            playerLane = hud.Timeline.AddLane("Player", playerColor);
        }
    }

    // Runs in real time (unscaled), so it keeps working while the game is paused for scrubbing.
    private void Update()
    {
        if (mode == Mode.Playing)
        {
            if (openAction != null && openAction.WasPressedThisFrame()) EnterScrub();
            return;
        }

        // Scrubbing.
        var caretaker = RewindCaretaker.Instance;
        if (caretaker == null) { Resume(); return; }

        int now = GameClock.Instance.Tick;
        int first = caretaker.FirstCapturedTick;
        float span = Mathf.Max(1, FurthestTick(now) - first); // right edge = present OR furthest clone end

        float moveX = scrubAction != null ? scrubAction.ReadValue<float>() : 0f;
        // Ramp speed up the longer the direction is held: precise nudges when tapped, fast when held.
        if (Mathf.Abs(moveX) > 0.15f) scrubHeldTime += Time.unscaledDeltaTime; else scrubHeldTime = 0f;
        float ramp = scrubAccelSeconds > 0f ? Mathf.Clamp01(scrubHeldTime / scrubAccelSeconds) : 1f;
        float speed = Mathf.Lerp(scrubTicksPerSecond, scrubMaxTicksPerSecond, ramp);
        scrubTickF = Mathf.Clamp(scrubTickF + moveX * speed * Time.unscaledDeltaTime, first, now);
        scrubTick = caretaker.SnapToCapture(Mathf.RoundToInt(scrubTickF));

        caretaker.Preview(scrubTick);

        // Reflect the scrub direction in the transport indicator: holding left = Rewind, right =
        // FastForward, idle = Pause. Only push on a change (SetTransport swaps sprites / resets alpha).
        TransportState desired = moveX < -0.15f ? TransportState.Rewind
                               : moveX > 0.15f ? TransportState.FastForward
                               : TransportState.Pause;
        if (hud != null && hud.Transport != desired) hud.SetTransport(desired);

        if (hud != null && hud.Timeline != null)
            hud.Timeline.SetPlayhead((scrubTick - first) / span);

        // ConfirmClone (Space) = rewind here AND leave a clone; ConfirmRewind (Tab) = rewind here
        // with no clone; Cancel (Esc) = abort and snap back to the present.
        if (confirmCloneAction != null && confirmCloneAction.WasPressedThisFrame()) ConfirmClone();
        else if (confirmRewindAction != null && confirmRewindAction.WasPressedThisFrame()) ConfirmRewind();
        else if (cancelAction != null && cancelAction.WasPressedThisFrame()) CancelScrub();
    }

    private void EnterScrub()
    {
        var caretaker = RewindCaretaker.Instance;
        if (livePlayer == null) livePlayer = FindAnyObjectByType<PlayerCommandInvoker>();
        if (caretaker == null || !caretaker.HasCaptured || livePlayer == null) return;

        mode = Mode.Scrubbing;
        scrubTickF = scrubTick = GameClock.Instance.Tick;
        scrubHeldTime = 0f;
        GameClock.Instance.SetPaused(true);
        // Hand input to the timeline: gameplay (Move/Jump) goes inert so the live player can't act
        // while scrubbing, and the scrub controls come alive. Swapped back in Resume().
        playerMap?.Disable();
        timelineMap?.Enable();
        hud?.SetTimelineVisible(true);
        hud?.Hint?.ShowTimeline(); // scrub controls while the timeline is open
        hud?.SetTransport(TransportState.Pause); // opens paused; Update flips to Rewind/FastForward as you scrub
        LayoutLaneSpans();
    }

    private void LayoutLaneSpans()
    {
        var caretaker = RewindCaretaker.Instance;
        if (hud == null || hud.Timeline == null || caretaker == null) return;

        int now = GameClock.Instance.Tick;
        int first = caretaker.FirstCapturedTick;
        float span = Mathf.Max(1, FurthestTick(now) - first);

        // Player is alive only up to the present; clones span their full [spawn, end] window, which can
        // reach past the present (the clock hasn't replayed that far yet) — shown to the right.
        if (playerLane >= 0) hud.Timeline.SetLaneSegment(playerLane, 0f, (now - first) / span);
        foreach (var c in clones)
            hud.Timeline.SetLaneSegment(c.lane, (c.start - first) / span, (c.end - first) / span);
    }

    private int FurthestTick(int now)
    {
        int end = now;
        for (int i = 0; i < clones.Count; i++)
            if (clones[i].end > end) end = clones[i].end;
        return end;
    }

    private void CancelScrub()
    {
        RewindCaretaker.Instance?.Preview(GameClock.Instance.Tick); // restore the present
        Resume();
    }

    private void ConfirmRewind()
    {
        var caretaker = RewindCaretaker.Instance;
        if (caretaker == null || livePlayer == null) { CancelScrub(); return; }

        int target = caretaker.Commit(scrubTick);
        if (target < 0) { CancelScrub(); return; }

        // Keep commands [.., target-1]; the clock resumed AT `target`, so the player re-runs that tick
        // and re-records it. Truncating to `target` would keep the old change-frame at `target` AND let
        // the re-record append a second one — two frames at one tick, which the timeline's binary search
        // then reads inconsistently (the source of stray clone inputs). Mirror of ConfirmClone's split.
        livePlayer.Timeline.TruncateAfterTick(target - 1);
        Resume();
    }

    private void ConfirmClone()
    {
        var caretaker = RewindCaretaker.Instance;
        if (caretaker == null || livePlayer == null) { CancelScrub(); return; }

        int target = caretaker.Commit(scrubTick);
        if (target < 0) { CancelScrub(); return; }

        // Command-side split, addressed by ABSOLUTE tick (mirror of the caretaker's state
        // DiscardAfter, opposite retention): the echo keeps a frozen [target, now] copy; the
        // live player keeps [.., target-1] and re-records forward from target.
        CommandTimeline echoScript = livePlayer.Timeline.SliceFromTick(target);
        livePlayer.Timeline.TruncateAfterTick(target - 1);

        // One colour per clone, shared by its timeline lane and its echo's albedo.
        Color color = CloneColorFor(clones.Count);
        SpawnEcho(echoScript, target, color);

        // Remember the clone's life window [target, present] so its lane is coloured over exactly
        // that slice next time the timeline opens.
        if (hud != null && hud.Timeline != null)
        {
            int lane = hud.Timeline.AddLane("Clone", color);
            if (lane >= 0) clones.Add((lane, target, echoScript.LastTick));
        }
        hud?.ShowToast("CLONE CREATED");
        Resume();
    }

    private void Resume()
    {
        mode = Mode.Playing;
        // Give input back to gameplay and silence the scrub controls (inverse of EnterScrub).
        timelineMap?.Disable();
        playerMap?.Enable();
        hud?.SetTimelineVisible(false);
        hud?.Hint?.ShowGameplay(); // back to gameplay controls
        hud?.SetTransport(TransportState.Play);
        GameClock.Instance.SetPaused(false);
    }

    // Distinct colour per clone: spread hues by the golden ratio so successive clones never clash.
    private Color CloneColorFor(int index)
    {
        float hue = (index * 0.61803398875f) % 1f;
        return Color.HSVToRGB(hue, cloneColorSaturation, cloneColorValue);
    }

    private void SpawnEcho(CommandTimeline script, int spawnTick, Color color)
    {
        if (echoPrefab == null)
        {
            Debug.LogError("RewindDirector: echoPrefab is not assigned — cannot spawn an echo.");
            return;
        }

        GameObject src = livePlayer.gameObject;
        var srcRb = src.GetComponent<Rigidbody2D>();

        // Seed from the Rigidbody2D, NOT the Transform: right after a rewind the rigidbody holds
        // the restored state@target, but the transform doesn't sync until the next physics tick.
        Vector2 seedPos = srcRb != null ? srcRb.position : (Vector2)src.transform.position;
        float seedRot = srcRb != null ? srcRb.rotation : src.transform.eulerAngles.z;

        GameObject echo = Instantiate(echoPrefab,
            new Vector3(seedPos.x, seedPos.y, src.transform.position.z),
            Quaternion.Euler(0f, 0f, seedRot));
        echo.name = $"Echo#{++echoSeq}";

        // Tint this echo with its clone colour (same one used for its timeline lane) via a
        // MaterialPropertyBlock — a per-renderer override, so NO unique material instance is
        // created (mr.material would, and that instance leaks when the echo is later reclaimed).
        // Keep the shared material's alpha so a translucent echo stays translucent.
        var mr = echo.GetComponentInChildren<MeshRenderer>();
        if (mr != null)
        {
            Material shared = mr.sharedMaterial;
            bool urp = shared != null && shared.HasProperty("_BaseColor");
            float alpha = shared == null ? 1f : (urp ? shared.GetColor("_BaseColor").a : shared.color.a);

            var mpb = new MaterialPropertyBlock();
            mr.GetPropertyBlock(mpb);
            mpb.SetColor(urp ? "_BaseColor" : "_Color", new Color(color.r, color.g, color.b, alpha));
            mr.SetPropertyBlock(mpb);
        }

        // Seed the echo from the player's restored state@target: position, rotation, and velocity.
        var echoRb = echo.GetComponent<Rigidbody2D>();
        if (srcRb != null && echoRb != null)
        {
            echoRb.position = seedPos;
            echoRb.rotation = seedRot;
            echoRb.linearVelocity = srcRb.linearVelocity;
            echoRb.angularVelocity = srcRb.angularVelocity;
        }

        // The echo spawns deep inside the player (and maybe other echoes). Sync transforms so the
        // seeded pose is visible to physics queries; PlayerController.ResolveCharacterOverlaps then
        // suppresses those deep overlaps each tick (before the solver steps) until they separate.
        Physics2D.SyncTransforms();

        echo.GetComponent<ClonePlayback>().Play(script);

        // Register + capture NOW at spawnTick (a capture-cadence tick) so the echo has an
        // alive record from the moment it exists — otherwise an immediate second rewind to
        // spawnTick would find no record and deactivate the fresh echo.
        var echoEntity = echo.GetComponent<RewindableEntity>();
        if (echoEntity != null)
        {
            RewindCaretaker.Instance.Register(echoEntity);
            echoEntity.Capture(spawnTick);
        }
    }
}
