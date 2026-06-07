using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float acceleration = 50f;
    [SerializeField] private float deceleration = 35f;

    [Header("Jump")]
    [SerializeField] private float jumpForce = 12f;
    [Tooltip("How long a jump press is buffered before landing, in seconds (converted to fixed ticks in Awake).")]
    [SerializeField] private float jumpBufferTime = 0.1f;
    [Tooltip("Grace period after leaving the ground during which a jump still fires (coyote time), in seconds.")]
    [SerializeField] private float coyoteTime = 0.1f;
    [Tooltip("Gravity multiplier while ascending with jump held. >1 caps the jump height even when held; ~1.5 gives a generous held jump.")]
    [SerializeField] private float ascentGravityMultiplier = 1.5f;
    [Tooltip("Gravity multiplier while ascending after jump was released (cuts the jump short).")]
    [SerializeField] private float lowJumpGravityMultiplier = 3f;
    [SerializeField] private float fallGravityMultiplier = 2.5f;

    [Header("Push")]
    [Tooltip("Force this character exerts on a PushableObstacle it deliberately presses into. Match the obstacle's pusherForce for honest cooperation calibration.")]
    [SerializeField] private float pushForce = 30f;

    [Header("Ground check")]
    [Tooltip("Layers considered solid for the ground check. ~0 (Everything) works — standing on another character is valid ground (you ride it).")]
    [SerializeField] private LayerMask groundLayer = ~0;
    [Tooltip("Footprint of the ground-check box. Default matches a 1-unit cube; widen for more slope tolerance, narrow to avoid catching adjacent walls.")]
    [SerializeField] private Vector2 groundCheckSize = new(1f, 0.1f);
    [Tooltip("How far below the collider's bottom edge to look for ground.")]
    [SerializeField] private float groundCheckDistance = 0.05f;
    [Tooltip("Surfaces steeper than this (degrees from horizontal) are treated as walls, not ground.")]
    [SerializeField, Range(0f, 89f)] private float maxSlopeAngle = 60f;
    [Tooltip("Gravity strength = this × Physics2D.gravity. Applied in code (the body's gravityScale is 0) so jump height stays tunable.")]
    [SerializeField] private float gravityScale = 4f;

    private Rigidbody2D rb;
    private Collider2D col;

    private Vector2 direction;
    private bool jumpHeld;
    private bool wasGrounded;

    // Ground state for the current tick, set by CheckGround().
    private Vector2 groundNormal = Vector2.up;
    private Vector2 groundVelocity;     // velocity of the body we stand on (a moving platform / carrier), else zero
    private Vector2 prevGroundVelocity; // last tick's groundVelocity — measure our own speed against this so a
                                        // carrier's OWN acceleration isn't absorbed into the resistance (snappy carry)

    private float gravity; // units/s², cached from gravityScale × Physics2D.gravity in Awake

    // Continuous deep-overlap suppression: while two characters overlap DEEPLY (a clone spawned or
    // rewound INTO another), their collision is ignored so the solver doesn't expel them violently;
    // it's re-enabled once they're nearly clear. Hysteresis (enter deep, exit near-zero) avoids
    // flicker and keeps the un-ignore shove gentle. The registry is every live character.
    private static readonly List<PlayerController> _characters = new();
    private float overlapIgnoreDepth;            // penetration past which we ignore the pair (set in Awake)
    private const float OverlapClearSkin = 0.05f; // penetration under which we re-solidify the pair

    // Jump buffering / ground-suppress use fixed-tick timing (not Time.time) so they're
    // rewind-safe and replay-stable: a clone replaying the same commands reproduces the same jumps.
    private const float PostJumpGroundedSuppressSeconds = 0.1f;
    private int jumpBufferTicks;
    private int groundSuppressTicks;
    private int coyoteTicks;

    // Stored as absolute tick STAMPS compared against the current tick — rewind-safe with no extra
    // channel: after a rewind the current tick moves back while a stamp stays in the future, so the
    // "happened recently" window fails (no phantom jump). A backward tick also clears them.
    private int lastJumpPressTick = int.MinValue;
    private int lastJumpedTick = int.MinValue;
    private int lastGroundedTick = int.MinValue; // last grounded tick — the coyote-time window
    private bool jumpRequested;
    private int currentTick;

    public event Action OnJumped;
    public event Action OnLanded;

    public Vector2 Direction => direction;
    public bool IsOnGround { get; private set; }
    public bool IsJumping => !IsOnGround && rb.linearVelocity.y > 0f;
    public Vector2 Velocity => rb != null ? rb.linearVelocity : Vector2.zero;
    public float MoveSpeed => moveSpeed;

    // Reusable ground-check buffer. A List (not a fixed array) so it grows to hold every hit instead
    // of silently truncating. Shared statically: queries run on the main thread, consumed synchronously.
    private static readonly List<RaycastHit2D> _groundHits = new();
    private static readonly List<ContactPoint2D> _contacts = new(); // reused per-tick contact scan for pushing
    private ContactFilter2D _groundFilter; // non-trigger, groundLayer

    // One frictionless material shared by all characters: with per-tick velocity control, contact
    // friction would only fight our control (and is unnecessary — carry is done by matching the
    // ground's velocity, not by friction). Bounciness 0 so stacks don't jitter.
    private static PhysicsMaterial2D _frictionless;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();

        // Dynamic body so the solver resolves collisions/de-penetration/stacking; we still own the
        // velocity each tick. Custom gravity (scale 0) keeps the variable-jump feel.
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous; // no tunnelling at speed
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;          // smooth render between ticks
        rb.sleepMode = RigidbodySleepMode2D.NeverSleep;                   // we set velocity every tick

        if (_frictionless == null)
            _frictionless = new PhysicsMaterial2D("CharacterFrictionless") { friction = 0f, bounciness = 0f };
        col.sharedMaterial = _frictionless;

        gravity = Mathf.Abs(Physics2D.gravity.y) * gravityScale;
        jumpBufferTicks = GameClock.SecondsToTicks(jumpBufferTime);
        groundSuppressTicks = GameClock.SecondsToTicks(PostJumpGroundedSuppressSeconds);
        coyoteTicks = GameClock.SecondsToTicks(coyoteTime);
        // Deep = overlapping by more than a quarter body: a spawn/rewind injection, never a solver
        // contact (which the dynamic solver keeps below the skin). Kept above the clear skin so the
        // hysteresis band is valid.
        overlapIgnoreDepth = Mathf.Max(2f * OverlapClearSkin, 0.25f * Mathf.Min(col.bounds.size.x, col.bounds.size.y));

        _groundFilter = new ContactFilter2D { useTriggers = false, useLayerMask = true };
        _groundFilter.SetLayerMask(groundLayer);
    }

    private void OnEnable() => _characters.Add(this);

    private void OnDisable()
    {
        _characters.Remove(this);
        // Clear any pair we were ignoring so a despawned/reclaimed clone leaves no stale ignore behind
        // (a revived character re-derives its overlaps next tick anyway).
        if (col == null) return;
        for (int i = 0; i < _characters.Count; i++)
            if (_characters[i] != null && _characters[i].col != null)
                Physics2D.IgnoreCollision(col, _characters[i].col, false);
    }

    public void SetDirection(Vector2 newDirection) => direction = newDirection;

    public void RequestJump() => jumpRequested = true;

    public void SetJumpHeld(bool held)
    {
        jumpHeld = held;
        // Releasing cancels a pending buffered jump, so a tap doesn't fire a late jump.
        if (!held) { jumpRequested = false; lastJumpPressTick = int.MinValue; }
    }

    public void Tick(int tick, float dt)
    {
        // A backward tick means the clock was rewound — drop stale jump/suppress/coyote stamps.
        bool rewound = tick < currentTick;
        if (rewound) { lastJumpPressTick = int.MinValue; lastJumpedTick = int.MinValue; lastGroundedTick = int.MinValue; }
        currentTick = tick;
        if (jumpRequested) { lastJumpPressTick = tick; jumpRequested = false; }

        ResolveCharacterOverlaps(); // toggle ignore for deep overlaps BEFORE the solver steps this tick

        IsOnGround = CheckGround();
        if (IsOnGround) lastGroundedTick = currentTick;     // coyote-time window
        if (rewound) prevGroundVelocity = groundVelocity;   // after a rewind, don't de-pollute against a pre-rewind carrier

        // Set the velocity the solver integrates this tick. It then resolves all contacts: walls,
        // ceilings, de-penetration, and keeping a stack of characters from interpenetrating.
        Vector2 velocity = rb.linearVelocity; // solver-adjusted last step; the rewindable state
        velocity = ApplyHorizontal(velocity, dt);
        velocity = ApplyGravity(velocity, dt);
        velocity = TryJump(velocity);
        rb.linearVelocity = velocity;

        PushContacts();                      // exert pushForce on any IPushable we deliberately press into
        prevGroundVelocity = groundVelocity; // remember for next tick's carry de-pollution
        FireLandEvent();
    }

    // Casts a thin box just below the feet. Records whether we're grounded, the surface normal, and the
    // velocity of whatever we stand on (a moving character/platform) so ApplyHorizontal can ride it.
    private bool CheckGround()
    {
        groundNormal = Vector2.up;
        groundVelocity = Vector2.zero;
        if (col == null) return false;
        // Suppress grounding briefly right after a jump so we don't immediately re-pin to the floor.
        if (lastJumpedTick != int.MinValue && currentTick >= lastJumpedTick
            && currentTick - lastJumpedTick < groundSuppressTicks) return false;

        Bounds b = col.bounds;
        Vector2 origin = new(b.center.x, b.min.y + 0.01f);
        int count = Physics2D.BoxCast(origin, groundCheckSize, 0f, Vector2.down, _groundFilter, _groundHits, groundCheckDistance + 0.01f);

        for (int i = 0; i < count; i++)
        {
            Collider2D c = _groundHits[i].collider;
            if (c.transform.IsChildOf(transform)) continue;                                   // not self
            if (Vector2.Angle(_groundHits[i].normal, Vector2.up) > maxSlopeAngle) continue;   // walls aren't ground
            groundNormal = _groundHits[i].normal;
            // If we stand on another body (a character/platform), ride its velocity (zero on static ground).
            Rigidbody2D groundRb = c.attachedRigidbody;
            if (groundRb != null) groundVelocity = groundRb.linearVelocity;
            return true;
        }
        return false;
    }

    private Vector2 ApplyHorizontal(Vector2 velocity, float dt)
    {
        float rate = Mathf.Abs(direction.x) > 0f ? acceleration : deceleration;

        if (IsOnGround)
        {
            // Work RELATIVE to the ground velocity, then add it back: on a moving carrier the idle
            // target is the carrier's own speed, so we're carried on both axes; on static ground it's
            // zero. The tangent follows the surface so we walk up/down slopes without sliding.
            Vector2 tangent = new(groundNormal.y, -groundNormal.x);
            // Measure our own speed against LAST tick's carrier velocity, not this tick's, so a carrier
            // that is itself accelerating isn't absorbed into the resistance → we track it rigidly
            // (snappy carry) instead of friction-lagging up to its speed. Add the CURRENT carrier vel back.
            float relCurrent = Vector2.Dot(velocity - prevGroundVelocity, tangent);
            float relTarget = direction.x * moveSpeed;
            float relNew = Mathf.MoveTowards(relCurrent, relTarget, rate * dt);
            return groundVelocity + tangent * relNew;
        }

        // Airborne: air-control the horizontal, preserve vertical for the jump/fall arc.
        float newX = Mathf.MoveTowards(velocity.x, direction.x * moveSpeed, rate * dt);
        return new Vector2(newX, velocity.y);
    }

    private Vector2 ApplyGravity(Vector2 velocity, float dt)
    {
        if (IsOnGround) return velocity; // grounded velocity is set from the surface; no gravity => no slide

        float mult = velocity.y > 0f
            ? (jumpHeld ? ascentGravityMultiplier : lowJumpGravityMultiplier) // variable jump height
            : fallGravityMultiplier;
        velocity.y -= gravity * mult * dt;
        return velocity;
    }

    private Vector2 TryJump(Vector2 velocity)
    {
        bool buffered = lastJumpPressTick != int.MinValue && currentTick >= lastJumpPressTick
                        && currentTick - lastJumpPressTick <= jumpBufferTicks;
        // Coyote time: still jumpable for a short window after leaving the ground.
        bool coyote = lastGroundedTick != int.MinValue && currentTick >= lastGroundedTick
                      && currentTick - lastGroundedTick <= coyoteTicks;
        if (buffered && (IsOnGround || coyote))
        {
            velocity.y = jumpForce + Mathf.Max(0f, groundVelocity.y); // inherit an upward carrier's boost
            lastJumpPressTick = int.MinValue; // consume the buffered press
            lastJumpedTick = currentTick;     // start the post-jump ground-suppress window
            lastGroundedTick = int.MinValue;  // consume coyote so we can't re-jump in mid-air
            IsOnGround = false;
            OnJumped?.Invoke();
        }
        return velocity;
    }

    // For every other live character, ignore the collision pair while we overlap DEEPLY (a spawn/rewind
    // injection the solver would otherwise expel) and restore it once we're nearly clear. Each pair is
    // handled once, by the lower instance id. Runs before the physics step so the solver sees the right
    // state this tick. Covers spawn, revive and rewind-reposition with one continuous rule.
    private void ResolveCharacterOverlaps()
    {
        if (col == null) return;
        int myId = GetInstanceID();
        for (int i = 0; i < _characters.Count; i++)
        {
            PlayerController other = _characters[i];
            if (other == this || other == null || other.col == null) continue;
            if (other.GetInstanceID() < myId || !other.isActiveAndEnabled) continue; // each pair once
            float depth = col.Distance(other.col).distance; // < 0 while overlapping (penetration)
            bool ignored = Physics2D.GetIgnoreCollision(col, other.col);
            if (!ignored && depth < -overlapIgnoreDepth)
                Physics2D.IgnoreCollision(col, other.col, true);   // deep injected overlap → pass through
            else if (ignored && depth > -OverlapClearSkin)
                Physics2D.IgnoreCollision(col, other.col, false);  // nearly clear → solid again
        }
    }

    // If we're actively pressing horizontally into an IPushable we're in contact with, exert pushForce
    // on it. The obstacle sums all pushers this tick and resolves in the GameClock LATE phase, so N
    // pushers cooperate; a bystander not pressing toward it contributes nothing.
    private void PushContacts()
    {
        if (col == null || Mathf.Abs(direction.x) < 0.01f) return;
        float dirSign = Mathf.Sign(direction.x);
        int n = rb.GetContacts(_contacts);
        for (int i = 0; i < n; i++)
        {
            ContactPoint2D cp = _contacts[i];
            Collider2D otherCol = (cp.collider != null && cp.collider.attachedRigidbody == rb) ? cp.otherCollider : cp.collider;
            if (otherCol == null || otherCol.transform.IsChildOf(transform)) continue;
            IPushable pushable = otherCol.GetComponentInParent<IPushable>();
            if (pushable == null) continue;
            float toOtherX = otherCol.bounds.center.x - col.bounds.center.x;   // are we pressing TOWARD it?
            if (Mathf.Abs(toOtherX) > 0.0001f && Mathf.Sign(toOtherX) == dirSign)
                pushable.ApplyPush(new Vector2(dirSign * pushForce, 0f));
        }
    }

    private void FireLandEvent()
    {
        if (IsOnGround && !wasGrounded) OnLanded?.Invoke();
        wasGrounded = IsOnGround;
    }

    private void OnDrawGizmosSelected()
    {
        if (col == null) col = GetComponent<Collider2D>();
        if (col == null) return;
        Bounds b = col.bounds;
        Vector2 boxCenter = new(b.center.x, b.min.y + 0.01f - groundCheckDistance * 0.5f);
        Gizmos.color = Application.isPlaying && IsOnGround ? Color.green : Color.red;
        Gizmos.DrawWireCube(boxCenter, new Vector3(groundCheckSize.x, groundCheckDistance + 0.01f, 0f));
    }
}
