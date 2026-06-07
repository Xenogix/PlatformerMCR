using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Drives the timeline-scrub clone flow and orchestrates echoes. While playing, the
/// Timeline input opens the timeline and PAUSES the game (GameClock + timeScale 0). The
/// player then scrubs a playhead with Move-X: each frame the Caretaker PREVIEWS the world
/// at the scrub tick (non-destructive restore) so the level visibly winds back and forth.
/// Pressing Jump CONFIRMS: the Caretaker commits the rewind to the chosen tick T and the
/// live player's recorded command stream is split there — the frozen [T, now] slice drives
/// a freshly spawned echo (Command replay) while the live player re-records forward from T.
/// Pressing Timeline again cancels (previews the present) and resumes.
///
/// The echo is a copy of the just-restored player, seeded at the player's exact state@T,
/// and made weightless (low mass) so the player can shove it around; collisions with the
/// player/other echoes it spawned inside are ignored until they separate, to avoid the
/// physics pop. The echo carries a RewindableEntity, so a later rewind snaps it back.
/// </summary>
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

    private PlayerCommandInvoker livePlayer;
    private LevelHud hud;

    private InputAction timelineAction; // open, then commit the rewind to the scrub point
    private InputAction moveAction;     // X axis scrubs the playhead
    private InputAction jumpAction;     // commits the rewind AND spawns a clone
    private InputAction cancelAction;   // aborts the scrub, returning to the present

    private Mode mode = Mode.Playing;
    private float scrubTickF;   // fractional accumulator for a smooth scrub
    private int scrubTick;
    private float scrubHeldTime; // how long the scrub direction has been held (drives acceleration)
    private int playerLane = -1; // Player's timeline lane
    private int echoSeq;          // unique id per spawned echo (diagnostics)

    // One per clone: its timeline lane and the absolute-tick window it acts over ([spawn, end]),
    // used to colour that lane's "life" slice when the timeline is opened.
    private readonly List<(int lane, int start, int end)> clones = new();

    private void Start()
    {
        livePlayer = FindAnyObjectByType<PlayerCommandInvoker>();
        hud = FindAnyObjectByType<LevelHud>();

        timelineAction = InputSystem.actions.FindAction("Timeline");
        moveAction = InputSystem.actions.FindAction("Move");
        jumpAction = InputSystem.actions.FindAction("Jump");
        cancelAction = InputSystem.actions.FindAction("Cancel"); // optional
        if (timelineAction == null)
            Debug.LogError("RewindDirector: 'Timeline' input action not found — add it to the project-wide actions.");

        hud?.SetTransport(TransportState.Play);
        hud?.SetTimelineVisible(false); // shown only while the timeline is open for scrubbing
        if (hud != null && hud.Timeline != null) playerLane = hud.Timeline.AddLane("Player", playerColor);
    }

    // Runs in real time (unscaled), so it keeps working while the game is paused for scrubbing.
    private void Update()
    {
        bool togglePressed = timelineAction != null && timelineAction.WasPressedThisFrame();

        if (mode == Mode.Playing)
        {
            if (togglePressed) EnterScrub();
            return;
        }

        // Scrubbing.
        var caretaker = RewindCaretaker.Instance;
        if (caretaker == null) { Resume(); return; }

        int now = GameClock.Instance.Tick;
        int first = caretaker.FirstCapturedTick;
        float span = Mathf.Max(1, FurthestTick(now) - first); // right edge = present OR furthest clone end

        float moveX = moveAction != null ? moveAction.ReadValue<Vector2>().x : 0f;
        // Ramp speed up the longer the direction is held: precise nudges when tapped, fast when held.
        if (Mathf.Abs(moveX) > 0.15f) scrubHeldTime += Time.unscaledDeltaTime; else scrubHeldTime = 0f;
        float ramp = scrubAccelSeconds > 0f ? Mathf.Clamp01(scrubHeldTime / scrubAccelSeconds) : 1f;
        float speed = Mathf.Lerp(scrubTicksPerSecond, scrubMaxTicksPerSecond, ramp);
        scrubTickF = Mathf.Clamp(scrubTickF + moveX * speed * Time.unscaledDeltaTime, first, now);
        scrubTick = caretaker.SnapToCapture(Mathf.RoundToInt(scrubTickF));

        caretaker.Preview(scrubTick);

        if (hud != null && hud.Timeline != null)
            hud.Timeline.SetPlayhead((scrubTick - first) / span);

        // Jump = rewind here AND leave a clone; Timeline (Tab) = rewind here with no clone;
        // Cancel (Esc) = abort and snap back to the present.
        if (jumpAction != null && jumpAction.WasPressedThisFrame()) ConfirmClone();
        else if (togglePressed) ConfirmRewind();
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
        hud?.SetTimelineVisible(true);
        hud?.SetTransport(TransportState.Rewind);
        LayoutLaneSpans();
    }

    // Colour each lane's life window over the (now-frozen) [firstCaptured, now] range: player spans
    // the whole timeline; each clone spans the [spawn, end] window it replays. Recomputed on every
    // open because `now` grows between openings, shifting where a fixed tick maps on the bar.
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

    // Right edge of the timeline in ticks: the present, or the furthest clone end if a clone still
    // has actions queued beyond the present (so its full window is visible).
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

    // Commit the rewind to the scrub point WITHOUT spawning a clone: the world stays in the past
    // and the live player resumes recording forward from there (the discarded future is gone).
    private void ConfirmRewind()
    {
        var caretaker = RewindCaretaker.Instance;
        if (caretaker == null || livePlayer == null) { CancelScrub(); return; }

        int target = caretaker.Commit(scrubTick);
        if (target < 0) { CancelScrub(); return; }

        // The live player keeps only [.., target] and re-records forward from target+1.
        livePlayer.Timeline.TruncateAfterTick(target);
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
        hud?.SetTimelineVisible(false);
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

        // Tint this echo with its clone colour (same one used for its timeline lane). Writes the
        // URP Lit albedo (_BaseColor) on the renderer's own material instance, keeping the existing
        // alpha so a translucent echo material stays translucent.
        var mr = echo.GetComponentInChildren<MeshRenderer>();
        if (mr != null)
        {
            var mat = mr.material; // instantiates a unique material for this echo
            if (mat.HasProperty("_BaseColor"))
            {
                float a = mat.GetColor("_BaseColor").a;
                mat.SetColor("_BaseColor", new Color(color.r, color.g, color.b, a));
            }
            else
            {
                mat.color = new Color(color.r, color.g, color.b, mat.color.a);
            }
        }

        // Seed the echo from the player's restored state@target: position, rotation, and velocity
        // (its carried kinematic velocity). Characters never collide with each other, so a spawn
        // overlapping the player is harmless — no de-penetration handling needed.
        var echoRb = echo.GetComponent<Rigidbody2D>();
        if (srcRb != null && echoRb != null)
        {
            echoRb.position = seedPos;
            echoRb.rotation = seedRot;
            echoRb.linearVelocity = srcRb.linearVelocity;
            echoRb.angularVelocity = srcRb.angularVelocity;
        }

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

        SetupSpawnOverlap(echo);
    }

    // The echo spawns ON TOP of the player (and possibly other echoes). Characters are solid, so
    // mutually pass through each overlapping character until they separate — otherwise the fresh
    // echo would be stuck against whatever it spawned inside.
    private void SetupSpawnOverlap(GameObject echo)
    {
        var echoCol = echo.GetComponent<Collider2D>();
        var echoPc = echo.GetComponent<PlayerController>();
        if (echoCol == null || echoPc == null) return;

        if (livePlayer != null) IgnoreEachOther(echoPc, echoCol, livePlayer.gameObject);
        foreach (var other in FindObjectsByType<ClonePlayback>())
            if (other.gameObject != echo) IgnoreEachOther(echoPc, echoCol, other.gameObject);
    }

    private static void IgnoreEachOther(PlayerController echoPc, Collider2D echoCol, GameObject peer)
    {
        var peerCol = peer.GetComponent<Collider2D>();
        var peerPc = peer.GetComponent<PlayerController>();
        if (peerCol != null) echoPc.IgnorePeerUntilClear(peerCol);
        if (peerPc != null) peerPc.IgnorePeerUntilClear(echoCol);
    }
}
