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
    [Tooltip("Gravity multiplier while ascending with jump held. >1 caps the jump height even when held; ~1.5 gives a generous held jump.")]
    [SerializeField] private float ascentGravityMultiplier = 1.5f;
    [Tooltip("Gravity multiplier while ascending after jump was released (cuts the jump short).")]
    [SerializeField] private float lowJumpGravityMultiplier = 3f;
    [SerializeField] private float fallGravityMultiplier = 2.5f;

    [Header("Ground check")]
    [Tooltip("Layers considered solid for movement AND ground. ~0 (Everything) works because other characters are filtered out in code.")]
    [SerializeField] private LayerMask groundLayer = ~0;
    [Tooltip("Footprint of the ground-check box. Default matches a 1-unit cube; widen for more slope tolerance, narrow to avoid catching adjacent walls.")]
    [SerializeField] private Vector2 groundCheckSize = new(1f, 0.1f);
    [Tooltip("How far below the collider's bottom edge to look for ground.")]
    [SerializeField] private float groundCheckDistance = 0.05f;
    [Tooltip("Surfaces steeper than this (degrees from horizontal) are treated as walls, not ground.")]
    [SerializeField, Range(0f, 89f)] private float maxSlopeAngle = 60f;
    [Tooltip("Gravity strength = this × Physics2D.gravity. Kept as a field since a kinematic body ignores the Rigidbody2D's own gravityScale.")]
    [SerializeField] private float gravityScale = 4f;

    private Rigidbody2D rb;
    private Collider2D col;

    private Vector2 direction;
    private bool jumpHeld;
    private bool wasGrounded;
    private Vector2 groundNormal = Vector2.up;
    private float gravity; // units/s², cached from gravityScale × Physics2D.gravity in Awake
    private float peerPassDepth; // overlap depth (units) past which we pass through a peer; set in Awake

    // Jump buffering / ground-suppress use fixed-tick timing (not Time.time) so they're
    // rewind-safe and replay-stable: a clone replaying the same commands reproduces the same jumps.
    private const float PostJumpGroundedSuppressSeconds = 0.1f;
    private int jumpBufferTicks;
    private int groundSuppressTicks;

    // Stored as absolute tick STAMPS compared against the current tick — rewind-safe with no extra
    // channel: after a rewind the current tick moves back while a stamp stays in the future, so the
    // "happened recently" window fails (no phantom jump). A backward tick also clears them.
    private int lastJumpPressTick = int.MinValue;
    private int lastJumpedTick = int.MinValue;
    private bool jumpRequested;
    private int currentTick;

    public event Action OnJumped;
    public event Action OnLanded;

    public Vector2 Direction => direction;
    public bool IsOnGround { get; private set; }
    public bool IsJumping => !IsOnGround && rb.linearVelocity.y > 0f;
    public Vector2 Velocity => rb != null ? rb.linearVelocity : Vector2.zero;
    public float MoveSpeed => moveSpeed;

    // Reusable result buffers for the physics queries. Lists (not fixed-size arrays) so they grow
    // to hold EVERY hit instead of silently truncating at a cap — overlap/sweep counts scale with
    // the number of stacked clones. Allocate only when growing, then reused. Shared statically:
    // these queries run on the main thread and consume the results synchronously (no reentrancy).
    private static readonly List<RaycastHit2D> _groundHits = new();
    private static readonly List<RaycastHit2D> _moveHits = new();
    private ContactFilter2D _solidFilter; // non-trigger, groundLayer; characters filtered in code

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        rb.bodyType = RigidbodyType2D.Kinematic; // we own the motion; no physics integration/friction
        rb.freezeRotation = true;

        gravity = Mathf.Abs(Physics2D.gravity.y) * gravityScale;
        jumpBufferTicks = GameClock.SecondsToTicks(jumpBufferTime);
        groundSuppressTicks = GameClock.SecondsToTicks(PostJumpGroundedSuppressSeconds);
        // Pass through a peer only when overlapping by more than half a body — a deep, injected
        // overlap (spawn/revive on top), never the few-mm overshoot a sweep can leave on contact.
        peerPassDepth = 0.5f * Mathf.Min(col.bounds.size.x, col.bounds.size.y);
        if (peerPassDepth <= 0f) peerPassDepth = 0.1f; // fallback if bounds aren't ready

        _solidFilter = new ContactFilter2D { useTriggers = false, useLayerMask = true };
        _solidFilter.SetLayerMask(groundLayer);
    }

    public void SetDirection(Vector2 newDirection) => direction = newDirection;

    public void RequestJump() => jumpRequested = true;

    public void SetJumpHeld(bool held)
    {
        jumpHeld = held;
        // Releasing cancels a pending buffered jump, so a tap doesn't fire a late jump.
        if (!held) { jumpRequested = false; lastJumpPressTick = int.MinValue; }
    }

    // Two characters pass through each other WHILE their colliders overlap (an echo spawned or
    // revived on top of the player/another echo), and snap back to solid the moment they separate —
    // so a fresh echo pops free instead of getting stuck against whatever it spawned inside. This is
    // computed from the live geometry every query, holding NO tracked state, so there is nothing to
    // rebuild on (re)activation and nothing for a rewind to restore. (A swept kinematic controller
    // can't de-penetrate an existing overlap on its own — the cast returns a zero-distance hit that
    // would otherwise freeze it; passing through until clear is the intended resolution.)
    private bool IsPassThroughPeer(Collider2D other)
    {
        if (other == null) return false;
        var peer = other.GetComponentInParent<PlayerController>();
        // DEEP overlap only (> half a body): a deep, injected spawn/revive-on-top overlap, never the
        // few-mm overshoot a sweep leaves on contact (gating on mere isOverlapped slid them through).
        return peer != null && peer != this && col.Distance(other).distance < -peerPassDepth;
    }

    public void Tick(int tick, float dt)
    {
        // A backward tick means the clock was rewound — drop stale jump/suppress stamps.
        if (tick < currentTick) { lastJumpPressTick = int.MinValue; lastJumpedTick = int.MinValue; }
        currentTick = tick;
        if (jumpRequested) { lastJumpPressTick = tick; jumpRequested = false; }

        IsOnGround = CheckGrounded();

        Vector2 velocity = rb.linearVelocity; // carried over (also what the rewind channel restored)
        velocity = ApplyHorizontal(velocity, dt);
        velocity = ApplyGravity(velocity, dt);
        velocity = TryJump(velocity);
        velocity = MoveAndSlide(velocity, dt); // sweep + slide; returns the realized velocity

        rb.linearVelocity = velocity; // kinematic body integrates this for the actual move
        FireLandEvent();
    }

    private bool CheckGrounded()
    {
        if (col == null) return false;
        // Suppress grounding briefly right after a jump so we don't immediately re-pin to the floor.
        if (lastJumpedTick != int.MinValue && currentTick >= lastJumpedTick
            && currentTick - lastJumpedTick < groundSuppressTicks) return false;

        Bounds b = col.bounds;
        Vector2 origin = new(b.center.x, b.min.y + 0.01f);
        int count = Physics2D.BoxCast(origin, groundCheckSize, 0f, Vector2.down, _solidFilter, _groundHits, groundCheckDistance + 0.01f);

        for (int i = 0; i < count; i++)
        {
            Collider2D c = _groundHits[i].collider;
            if (c.transform.IsChildOf(transform) || IsPassThroughPeer(c)) continue; // not self, not a peer we overlap
            if (Vector2.Angle(_groundHits[i].normal, Vector2.up) > maxSlopeAngle) continue; // walls aren't ground
            groundNormal = _groundHits[i].normal;
            return true;
        }
        groundNormal = Vector2.up;
        return false;
    }

    private Vector2 ApplyHorizontal(Vector2 velocity, float dt)
    {
        float targetSpeed = direction.x * moveSpeed;
        float rate = Mathf.Abs(direction.x) > 0f ? acceleration : deceleration;

        if (IsOnGround && !IsJumping)
        {
            Vector2 tangent = new(groundNormal.y, -groundNormal.x);
            float currentSpeed = Vector2.Dot(velocity, tangent);
            float newSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rate * dt);
            return tangent * newSpeed;
        }

        // Airborne: blend horizontal, preserve vertical for the jump/fall arc.
        float newX = Mathf.MoveTowards(velocity.x, targetSpeed, rate * dt);
        return new Vector2(newX, velocity.y);
    }

    private Vector2 ApplyGravity(Vector2 velocity, float dt)
    {
        if (IsOnGround) return velocity; // grounded velocity is fully slope-driven; no gravity => no slide

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
        if (buffered && IsOnGround)
        {
            velocity.y = jumpForce;
            lastJumpPressTick = int.MinValue; // consume the buffered press
            lastJumpedTick = currentTick;     // start the post-jump ground-suppress window
            IsOnGround = false;
            OnJumped?.Invoke();
        }
        return velocity;
    }

    private Vector2 MoveAndSlide(Vector2 velocity, float dt)
    {
        const float skin = 0.01f;
        const int maxIterations = 4;

        Vector2 delta = velocity * dt;
        Vector2 moved = Vector2.zero;

        for (int i = 0; i < maxIterations; i++)
        {
            float dist = delta.magnitude;
            if (dist < 1e-6f) break;
            Vector2 dir = delta / dist;

            RaycastHit2D hit = NearestBlocker(dir, rb.Cast(dir, _solidFilter, _moveHits, dist + skin));
            if (!hit) { moved += delta; break; } // clear path: take the whole step

            float allowed = Mathf.Max(0f, hit.distance - skin);
            moved += dir * allowed;
            // Slide: project the unused remainder along the surface (drop the into-surface part).
            Vector2 remainder = dir * (dist - allowed);
            delta = remainder - hit.normal * Vector2.Dot(remainder, hit.normal);
        }

        return dt > 0f ? moved / dt : Vector2.zero;
    }

    private RaycastHit2D NearestBlocker(Vector2 dir, int count)
    {
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D h = _moveHits[i];
            if (IsPassThroughPeer(h.collider)) continue;
            if (Vector2.Dot(dir, h.normal) >= -1e-4f) continue; // not moving into this surface
            return h;
        }
        return default;
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
