using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// Eases the vcam's follow damping tighter as the player outruns its own move speed, so fast slope
/// descents / conserved momentum don't let the player slide ahead of the camera. At or below moveSpeed
/// the relaxed <see cref="baseDamping"/> is used; by (moveSpeed × <see cref="fastSpeedMultiple"/>) it has
/// eased to the tight <see cref="fastDamping"/>. The damping value itself is smoothed so crossing the
/// threshold doesn't pop the camera.
/// While the timeline is open (clock paused for scrubbing) it switches the brain to unscaled time and
/// locks tight (<see cref="rewindDamping"/>) so the camera stays focused on the player through the rewind.
/// </summary>
[RequireComponent(typeof(CinemachineFollow))]
public class CameraSpeedDamping : MonoBehaviour
{
    [SerializeField] private PlayerController player;
    [Tooltip("Follow damping at or below moveSpeed (relaxed, smooth on small hops).")]
    [SerializeField] private float baseDamping = 2f;
    [Tooltip("Follow damping once fully overspeeding (tight, keeps up with a fast descent).")]
    [SerializeField] private float fastDamping = 0.3f;
    [Tooltip("Multiple of moveSpeed at which damping reaches fastDamping.")]
    [SerializeField] private float fastSpeedMultiple = 2f;
    [Tooltip("Ease time (seconds) for the damping value itself, so it doesn't snap when speed changes.")]
    [SerializeField] private float dampingBlend = 0.25f;

    [Header("Rewind")]
    [Tooltip("Follow damping while the timeline is open (scrubbing). Low = the camera locks onto the player as it rewinds.")]
    [SerializeField] private float rewindDamping = 0.1f;
    [Tooltip("The output camera's brain. Auto-found if empty. Switched to unscaled time while scrubbing so the camera keeps tracking while the clock is paused.")]
    [SerializeField] private CinemachineBrain brain;

    private CinemachineFollow follow;
    private float current;
    private float blendVelocity;

    private void Awake()
    {
        follow = GetComponent<CinemachineFollow>();
        current = baseDamping;
        if (player == null) player = FindAnyObjectByType<PlayerController>();
        if (brain == null) brain = FindAnyObjectByType<CinemachineBrain>();
    }

    private void LateUpdate()
    {
        // The clock is paused (timeScale 0) only while the timeline is open for scrubbing. Cinemachine
        // freezes at timeScale 0 unless told to ignore it, so flip the brain to unscaled time ONLY then —
        // the camera keeps tracking the player through the rewind, and normal play stays untouched.
        bool scrubbing = GameClock.HasInstance && GameClock.Instance.IsPaused;
        if (brain != null) brain.IgnoreTimeScale = scrubbing;

        float target;
        if (scrubbing)
        {
            // player.Speed is stale while paused, so don't use it — lock tight onto the player instead.
            target = rewindDamping;
        }
        else if (player != null)
        {
            // 0 at walking pace, 1 once the player's own speed reaches fastSpeedMultiple × moveSpeed.
            float t = player.MoveSpeed > 0f
                ? Mathf.InverseLerp(player.MoveSpeed, player.MoveSpeed * fastSpeedMultiple, player.Speed)
                : 0f;
            target = Mathf.Lerp(baseDamping, fastDamping, t);
        }
        else return;

        // Drive the blend on unscaled time so the damping value still eases while the clock is paused.
        current = Mathf.SmoothDamp(current, target, ref blendVelocity, dampingBlend, Mathf.Infinity, Time.unscaledDeltaTime);

        var settings = follow.TrackerSettings;
        settings.PositionDamping = new Vector3(current, current, settings.PositionDamping.z); // leave Z untouched
        follow.TrackerSettings = settings;
    }
}
