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

    [Tooltip("Echo mass as a fraction of the player's, so the player shoves echoes around weightlessly.")]
    [SerializeField, Range(0.01f, 1f)] private float echoMassFactor = 0.2f;

    [Tooltip("Echo prefab (required) — a Player variant carrying ClonePlayback + RigidbodyChannel + RewindableEntity and NO PlayerCommandInvoker (translucent).")]
    [SerializeField] private GameObject echoPrefab;

    [Tooltip("Playhead scrub speed, in game ticks per real second of held Move-X.")]
    [SerializeField, Min(1f)] private float scrubTicksPerSecond = 30f;

    [Tooltip("Lane colour used for each clone added to the timeline.")]
    [SerializeField] private Color cloneColor = new(1f, 0.78f, 0.45f);

    [Tooltip("Lane colour for the live player's own lane (P1).")]
    [SerializeField] private Color playerColor = new(0.55f, 0.9f, 1f);

    private PlayerCommandInvoker livePlayer;
    private LevelHud hud;

    private InputAction timelineAction; // open, then commit the rewind to the scrub point
    private InputAction moveAction;     // X axis scrubs the playhead
    private InputAction jumpAction;     // commits the rewind AND spawns a clone
    private InputAction cancelAction;   // aborts the scrub, returning to the present

    private Mode mode = Mode.Playing;
    private float scrubTickF; // fractional accumulator for a smooth scrub
    private int scrubTick;
    private int playerLane = -1; // P1's timeline lane

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
        if (hud != null && hud.Timeline != null) playerLane = hud.Timeline.AddLane("P1", playerColor);
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

        float moveX = moveAction != null ? moveAction.ReadValue<Vector2>().x : 0f;
        scrubTickF = Mathf.Clamp(scrubTickF + moveX * scrubTicksPerSecond * Time.unscaledDeltaTime, first, now);
        scrubTick = caretaker.SnapToCapture(Mathf.RoundToInt(scrubTickF));

        caretaker.Preview(scrubTick);

        if (hud != null && hud.Timeline != null)
        {
            float span = Mathf.Max(1, now - first);
            float t01 = (scrubTick - first) / span;
            hud.Timeline.SetPlayhead(t01);
            if (playerLane >= 0) hud.Timeline.SetLaneProgress(playerLane, t01);
        }

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
        GameClock.Instance.SetPaused(true);
        hud?.SetTimelineVisible(true);
        hud?.SetTransport(TransportState.Rewind);
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

        SpawnEcho(echoScript, target);

        hud?.Timeline?.AddLane("CLONE", cloneColor);
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

    private void SpawnEcho(CommandTimeline script, int spawnTick)
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
        echo.name = "Echo";

        var echoRb = echo.GetComponent<Rigidbody2D>();
        if (srcRb != null && echoRb != null)
        {
            echoRb.position = seedPos;
            echoRb.rotation = seedRot;
            echoRb.linearVelocity = srcRb.linearVelocity;
            echoRb.angularVelocity = srcRb.angularVelocity;
            echoRb.mass = srcRb.mass * echoMassFactor; // weightless-ish: the player shoves it around
        }

        echo.GetComponent<ClonePlayback>().Play(script);
        Debug.Log($"[RewindDirector] Echo spawned at tick {spawnTick}, replay window [{spawnTick}..{script.LastTick}] = {script.LastTick - spawnTick} ticks. " +
                  $"It replays your recorded actions for that window, then freezes in place (stays visible).");

        // Register + capture NOW at spawnTick (a capture-cadence tick) so the echo has an
        // alive record from the moment it exists — otherwise an immediate second rewind to
        // spawnTick would find no record and deactivate the fresh echo.
        var echoEntity = echo.GetComponent<RewindableEntity>();
        if (echoEntity != null)
        {
            RewindCaretaker.Instance.Register(echoEntity);
            echoEntity.Capture(spawnTick);
        }

        SetupSpawnOverlapIgnore(echo);
    }

    // Don't let the spawn-overlap with the player (and any echo it spawned inside) pop them
    // apart: ignore those collisions until they separate.
    private void SetupSpawnOverlapIgnore(GameObject echo)
    {
        var echoCol = echo.GetComponent<Collider2D>();
        if (echoCol == null) return;

        var peers = new List<Collider2D>();
        var playerCol = livePlayer.GetComponent<Collider2D>();
        if (playerCol != null) peers.Add(playerCol);
        foreach (var other in FindObjectsByType<ClonePlayback>(FindObjectsSortMode.None))
        {
            if (other.gameObject == echo) continue;
            var c = other.GetComponent<Collider2D>();
            if (c != null) peers.Add(c);
        }

        echo.AddComponent<IgnoreCollisionUntilClear>().IgnoreWhileOverlapping(peers);
    }
}
