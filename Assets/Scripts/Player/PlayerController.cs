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
    private bool jumpRequested;
    private bool jumpHeld;
    private float lastJumpRequestTime;
    private float lastJumpedTime = -1f;
    private bool wasGrounded;
    private float baseGravityScale = 1f;
    private Vector2 groundNormal = Vector2.up;
    private const float postJumpGroundedSuppressTime = 0.1f;

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
    }

    public void SetDirection(Vector2 newDirection) => direction = newDirection;
    public void RequestJump()
    {
        lastJumpRequestTime = Time.time;
        jumpRequested = true;
    }
    public void SetJumpHeld(bool held)
    {
        jumpHeld = held;
        // Releasing the jump button cancels any pending buffered jump request.
        // Without this, a request set on press stays alive for jumpBufferTime,
        // and if the player happens to be grounded during that window it fires.
        if (!held) jumpRequested = false;
    }

    private void FixedUpdate()
    {
        IsOnGround = CheckGrounded();
        ApplyHorizontalMovement();
        ApplyVariableGravity();
        TryJump();
        FireLandEvent();
    }

    private static readonly RaycastHit2D[] _groundHits = new RaycastHit2D[8];
    private static readonly ContactFilter2D _groundFilter = new ContactFilter2D { useTriggers = false };

    private bool CheckGrounded()
    {
        if (col == null) return false;
        // Briefly suppress grounded-detection right after a jump, so the next
        // FixedUpdate doesn't re-pin the player to the surface and clobber
        // the jump's vertical velocity in ApplyHorizontalMovement.
        if (Time.time - lastJumpedTime < postJumpGroundedSuppressTime) return false;

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

    private void ApplyHorizontalMovement()
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
            float newSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rate * Time.fixedDeltaTime);
            rb.linearVelocity = tangent * newSpeed;
        }
        else
        {
            // Airborne: just blend horizontal velocity, preserve Y for jump/fall.
            float newX = Mathf.MoveTowards(rb.linearVelocity.x, targetSpeed, rate * Time.fixedDeltaTime);
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
        if (jumpRequested && Time.time - lastJumpRequestTime > jumpBufferTime)
            jumpRequested = false;

        if (jumpRequested && IsOnGround)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            jumpRequested = false;
            lastJumpedTime = Time.time;
            // Immediately mark airborne so this FixedUpdate's FireLandEvent and
            // the next FixedUpdate's CheckGrounded behave consistently.
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
