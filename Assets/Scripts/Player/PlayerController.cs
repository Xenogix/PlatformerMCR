using System;
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
    [Tooltip("Gravity multiplier while ascending with jump held. >1 caps the jump height even when held; ~1.5 gives a generous held jump.")]
    [SerializeField] private float ascentGravityMultiplier = 1.5f;
    [Tooltip("Gravity multiplier while ascending after jump was released (cuts the jump short).")]
    [SerializeField] private float lowJumpGravityMultiplier = 3f;
    [SerializeField] private float fallGravityMultiplier = 2.5f;

    [Header("Ground check")]
    [Tooltip("Layers considered ground. ~0 (Everything) works because the player's own collider is filtered out explicitly.")]
    [SerializeField] private LayerMask groundLayer = ~0;
    [Tooltip("Footprint of the ground-check box. Default matches a 1-unit cube; widen for more slope tolerance, narrow to avoid catching adjacent walls.")]
    [SerializeField] private Vector2 groundCheckSize = new(1f, 0.1f);
    [Tooltip("How far below the collider's bottom edge to look for ground.")]
    [SerializeField] private float groundCheckDistance = 0.05f;
    [Tooltip("Surfaces steeper than this (degrees from horizontal) are treated as walls, not ground. Prevents getting stuck against vertical edges between platforms.")]
    [SerializeField, Range(0f, 89f)] private float maxSlopeAngle = 60f;

    private Rigidbody2D rb;
    private Collider2D col;

    private Vector2 direction;
    private bool jumpHeld;
    private bool wasGrounded;
    private float baseGravityScale = 1f;
    private Vector2 groundNormal = Vector2.up;

    // Time-based timers (Time.time) were replaced by fixed-tick counters so the
    // controller is rewind-safe and replay-stable: Time.time keeps marching forward
    // across a rewind, but these counters move with the clock, and a clone replaying
    // the same commands reproduces the same jumps. The two seconds-durations are
    // converted to tick counts once in Awake (fixed timestep is constant).
    private const float PostJumpGroundedSuppressSeconds = 0.1f;
    private int jumpBufferTicks;
    private int groundSuppressTicks;
    private int jumpBufferCounter;     // >0 => a jump is buffered; counts down each tick
    private int groundSuppressCounter; // >0 => ground detection suppressed just after a jump

    public event Action OnJumped;
    public event Action OnLanded;

    public Vector2 Direction => direction;
    public bool IsOnGround { get; private set; }
    public bool IsJumping => !IsOnGround && rb.linearVelocity.y > 0f;
    public Vector2 Velocity => rb != null ? rb.linearVelocity : Vector2.zero;
    public float MoveSpeed => moveSpeed;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        rb.freezeRotation = true;
        if (rb.gravityScale <= 0f) rb.gravityScale = baseGravityScale;
        baseGravityScale = rb.gravityScale;

        float dt = Time.fixedDeltaTime;
        jumpBufferTicks = Mathf.Max(1, Mathf.RoundToInt(jumpBufferTime / dt));
        groundSuppressTicks = Mathf.Max(1, Mathf.RoundToInt(PostJumpGroundedSuppressSeconds / dt));
    }

    public void SetDirection(Vector2 newDirection) => direction = newDirection;

    public void RequestJump() => jumpBufferCounter = jumpBufferTicks;

    public void SetJumpHeld(bool held)
    {
        jumpHeld = held;
        // Releasing the jump button cancels any pending buffered jump request, so a
        // tap doesn't fire a late jump if the player happens to touch ground during
        // the buffer window.
        if (!held) jumpBufferCounter = 0;
    }

    /// <summary>
    /// Advance one fixed step. Driven by the player's PlayerCommandInvoker (live) or a
    /// ClonePlayback (replay) via GameClock — NOT by Unity's FixedUpdate — so the live
    /// player and every clone step on the exact same deterministic tick timeline.
    /// </summary>
    public void Tick(int tick, float dt)
    {
        if (groundSuppressCounter > 0) groundSuppressCounter--;

        IsOnGround = CheckGrounded();
        ApplyHorizontalMovement(dt);
        ApplyVariableGravity();
        TryJump();
        FireLandEvent();

        // Expire the jump buffer AFTER this tick's TryJump, so a press latched the same tick
        // (RequestJump runs in the invoker before controller.Tick) still gets its full window
        // — even at jumpBufferTicks == 1.
        if (jumpBufferCounter > 0) jumpBufferCounter--;
    }

    private static readonly RaycastHit2D[] _groundHits = new RaycastHit2D[8];
    private static readonly ContactFilter2D _groundFilter = new ContactFilter2D { useTriggers = false };

    private bool CheckGrounded()
    {
        if (col == null) return false;
        // Briefly suppress grounded-detection right after a jump, so the next tick
        // doesn't re-pin the player to the surface and clobber the jump's vertical
        // velocity in ApplyHorizontalMovement.
        if (groundSuppressCounter > 0) return false;

        var filter = _groundFilter;
        filter.useLayerMask = true;
        filter.SetLayerMask(groundLayer);

        // BoxCast straight down from just inside the collider's bottom edge.
        // The cube-wide footprint catches slopes that touch the player's side, and
        // the cast gives us a surface normal in the same call.
        Bounds b = col.bounds;
        Vector2 origin = new(b.center.x, b.min.y + 0.01f);
        int count = Physics2D.BoxCast(origin, groundCheckSize, 0f, Vector2.down, filter, _groundHits, groundCheckDistance + 0.01f);

        for (int i = 0; i < count; i++)
        {
            if (_groundHits[i].collider.transform.IsChildOf(transform)) continue;
            // Reject surfaces steeper than maxSlopeAngle — those are walls, not floors.
            // Without this, the BoxCast can pick up a vertical platform edge between
            // gaps and hand back a (nearly) horizontal normal, whose tangent is
            // vertical and pins the player in place.
            if (Vector2.Angle(_groundHits[i].normal, Vector2.up) > maxSlopeAngle) continue;
            groundNormal = _groundHits[i].normal;
            return true;
        }
        groundNormal = Vector2.up;
        return false;
    }

    private void ApplyHorizontalMovement(float dt)
    {
        float targetSpeed = direction.x * moveSpeed;
        float rate = Mathf.Abs(direction.x) > 0f ? acceleration : deceleration;

        // SlopeTangent points "right along the surface": for a flat floor this
        // is (1, 0); for a left-rising slope (normal tilted right) it points
        // down-right. Multiplying by signed speed walks the player parallel to
        // the surface, so the velocity carries the player up/down the slope
        // instead of jamming into its corner.
        Vector2 tangent = new(groundNormal.y, -groundNormal.x);

        if (IsOnGround && !IsJumping)
        {
            // Project current velocity onto the tangent to get current speed
            // along the slope, then accelerate/decelerate toward target.
            float currentSpeed = Vector2.Dot(rb.linearVelocity, tangent);
            float newSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rate * dt);
            rb.linearVelocity = tangent * newSpeed;
        }
        else
        {
            // Airborne: just blend horizontal velocity, preserve Y for jump/fall.
            float newX = Mathf.MoveTowards(rb.linearVelocity.x, targetSpeed, rate * dt);
            rb.linearVelocity = new Vector2(newX, rb.linearVelocity.y);
        }
    }

    private void ApplyVariableGravity()
    {
        if (IsOnGround)
        {
            rb.gravityScale = baseGravityScale;
            return;
        }

        if (rb.linearVelocity.y > 0f)
        {
            // Ascending. Even with jump held, gravity is heavier than base so
            // the jump's height is bounded. Releasing jump applies a stronger
            // multiplier to cut the jump short (variable jump height).
            float mult = jumpHeld ? ascentGravityMultiplier : lowJumpGravityMultiplier;
            rb.gravityScale = baseGravityScale * mult;
        }
        else if (rb.linearVelocity.y < 0f)
        {
            rb.gravityScale = baseGravityScale * fallGravityMultiplier;
        }
        else
        {
            rb.gravityScale = baseGravityScale;
        }
    }

    private void TryJump()
    {
        if (jumpBufferCounter > 0 && IsOnGround)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            jumpBufferCounter = 0;
            groundSuppressCounter = groundSuppressTicks;
            // Immediately mark airborne so this tick's FireLandEvent and the next
            // tick's CheckGrounded behave consistently.
            IsOnGround = false;
            OnJumped?.Invoke();
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
        // Draw the slope tangent we'd walk along, for debugging.
        if (Application.isPlaying && IsOnGround)
        {
            Gizmos.color = Color.cyan;
            Vector2 t = new(groundNormal.y, -groundNormal.x);
            Gizmos.DrawLine(b.center, (Vector2)b.center + t);
        }
    }
}
