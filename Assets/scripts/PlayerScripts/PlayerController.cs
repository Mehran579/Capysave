// Requires: Unity Input System package (com.unity.inputsystem) installed and
// Project Settings > Player > Active Input Handling = "Input System Package (New)" or "Both".
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Collider2D))]
public class PlayerController : MonoBehaviour
{
    // ------------------------------------------------------------------
    // Inspector
    // ------------------------------------------------------------------
    [Header("Walk / Run")]
    [Tooltip("Base walk speed (units/second).")]
    [Min(0f)] [SerializeField] private float walkSpeed = 6f;
    [Tooltip("Run speed when Left Shift is held past the hold threshold.")]
    [Min(0f)] [SerializeField] private float runSpeed = 10f;

    [Header("Jump")]
    [Tooltip("Initial upward velocity on jump.")]
    [Min(0f)] [SerializeField] private float jumpForce = 14f;
    [Tooltip("Gravity multiplier while falling.")]
    [Min(0f)] [SerializeField] private float fallGravityMultiplier = 2.2f;
    [Tooltip("Gravity multiplier while rising but jump released early (variable jump).")]
    [Min(0f)] [SerializeField] private float lowJumpMultiplier = 2f;
    [Tooltip("Maximum fall speed cap (0 disables cap).")]
    [Min(0f)] [SerializeField] private float maxFallSpeed = 22f;
    [Tooltip("Time after leaving ground during which a jump is still allowed.")]
    [Range(0f, 0.5f)] [SerializeField] private float coyoteTime = 0.1f;
    [Tooltip("Jump input buffered this long before landing.")]
    [Range(0f, 0.5f)] [SerializeField] private float jumpBufferTime = 0.12f;

    [Header("Dash")]
    [Tooltip("Dash velocity magnitude.")]
    [Min(0f)] [SerializeField] private float dashSpeed = 22f;
    [Tooltip("Dash duration (seconds).")]
    [Range(0.02f, 1f)] [SerializeField] private float dashDuration = 0.18f;
    [Tooltip("Cooldown between dashes.")]
    [Min(0f)] [SerializeField] private float dashCooldown = 0.5f;
    [Tooltip("Hold threshold above which Left Shift becomes run instead of dash.")]
    [Range(0.05f, 0.5f)] [SerializeField] private float dashHoldThreshold = 0.15f;

    [Header("Ground Check")]
    [Tooltip("Layers considered ground.")]
    [SerializeField] private LayerMask groundLayer;
    [Tooltip("Local offset of the ground-check box from the transform.")]
    [SerializeField] private Vector2 groundCheckOffset = new Vector2(0f, -0.9f);
    [Tooltip("Size of the ground-check box.")]
    [SerializeField] private Vector2 groundCheckSize = new Vector2(0.7f, 0.12f);

    [Header("Transformed Movement")]
    [Tooltip("Horizontal roll speed while transformed.")]
    [Min(0f)] [SerializeField] private float transformedRollSpeed = 8f;

    [Header("Slow (set by enemies — read-only here)")]
    [Tooltip("Current speed multiplier (1 = normal). Only informational in the Inspector.")]
    [Range(0f, 1f)] [SerializeField] private float currentSpeedMultiplier = 1f;
    [Tooltip("Does the active slow also reduce jump force?")]
    [SerializeField] private bool slowAffectsJump = false;

    [Header("Input Bindings")]
    [SerializeField] private string moveUpBinding = "<Keyboard>/w";
    [SerializeField] private string moveDownBinding = "<Keyboard>/s";
    [SerializeField] private string moveLeftBinding = "<Keyboard>/a";
    [SerializeField] private string moveRightBinding = "<Keyboard>/d";
    [SerializeField] private string jumpBinding = "<Keyboard>/space";
    [SerializeField] private string dashBinding = "<Keyboard>/leftShift";

    // ------------------------------------------------------------------
    // Cached components
    // ------------------------------------------------------------------
    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private Collider2D col;

    // ------------------------------------------------------------------
    // Input actions (defined in code — no Input Actions asset required).
    // ------------------------------------------------------------------
    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction dashAction;

    // ------------------------------------------------------------------
    // Public state (read by PlayerCombat / PlayerMeter / PlayerHealth)
    // ------------------------------------------------------------------
    public int FacingDirection { get; private set; } = 1;
    public bool IsGrounded { get; private set; }
    public bool IsDashing { get; private set; }
    public bool IsStunned { get; private set; }
    public bool IsSlamming { get; set; }
    public bool IsTransformed { get; set; }
    public Vector2 MoveInput { get; private set; }
    public bool IsRunHeld { get; private set; }
    public bool CanAirDash { get; set; } = true;
    public Rigidbody2D Rigidbody => rb;
    public SpriteRenderer SpriteRenderer => spriteRenderer;
    public float OriginalGravity { get; private set; }

    // Any-state lock (external coroutines check this).
    public bool MovementLocked => IsStunned || IsSlamming || IsDashing;

    // ------------------------------------------------------------------
    // Internal timers / state
    // ------------------------------------------------------------------
    private float lastGroundedTime = -999f;
    private float lastJumpPressedTime = -999f;
    private float dashCooldownTimer;
    private float dashHoldTimer;
    private bool dashPressActive;
    private Coroutine dashCoroutine;
    private readonly List<Collider2D> enemyBodyColliders = new List<Collider2D>();
    private bool dashPassThrough;
    private bool slamPassThrough;
    private bool invulnerabilityPassThrough;

    // ==================================================================
    // Unity messages
    // ==================================================================
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
        OriginalGravity = rb.gravityScale;

        // Cache scene enemy body colliders. They remain solid for normal movement,
        // but are ignored temporarily during dash and Eagle Purge.
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        Collider2D[] sceneColliders = FindObjectsByType<Collider2D>(FindObjectsSortMode.None);
        foreach (Collider2D other in sceneColliders)
        {
            if (enemyLayer < 0 || other == col || other.gameObject.layer != enemyLayer || other.attachedRigidbody == rb)
                continue;
            enemyBodyColliders.Add(other);
        }

        // Build Input Actions in code so there is no asset to author.
        moveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", moveUpBinding)
            .With("Down", moveDownBinding)
            .With("Left", moveLeftBinding)
            .With("Right", moveRightBinding);

        jumpAction = new InputAction("Jump", InputActionType.Button, jumpBinding);
        dashAction = new InputAction("Dash", InputActionType.Button, dashBinding);
    }

    private void OnEnable()
    {
        moveAction.Enable();
        jumpAction.Enable();
        dashAction.Enable();
    }

    private void OnDisable()
    {
        moveAction.Disable();
        jumpAction.Disable();
        dashAction.Disable();
    }

    private void OnDestroy()
    {
        // Dispose so GC doesn't leak native handles when the player is destroyed.
        moveAction?.Dispose();
        jumpAction?.Dispose();
        dashAction?.Dispose();
    }

    // Update: input reading only.
    private void Update()
    {
        MoveInput = moveAction.ReadValue<Vector2>();

        // Facing from horizontal input (do not re-flip mid-dash / mid-stun).
        if (!IsDashing && !IsStunned && Mathf.Abs(MoveInput.x) > 0.01f)
        {
            FacingDirection = MoveInput.x > 0f ? 1 : -1;
            spriteRenderer.flipX = FacingDirection < 0;
        }

        // Jump buffering.
        if (jumpAction.WasPressedThisFrame())
            lastJumpPressedTime = Time.time;

        // Tap-vs-hold detection for dash / run.
        if (dashAction.WasPressedThisFrame())
        {
            dashHoldTimer = 0f;
            dashPressActive = true;
            if (CanDash())
                TryDash(); // fire the dash immediately on press (tap behavior)
        }

        if (dashAction.IsPressed())
        {
            dashHoldTimer += Time.deltaTime;
            IsRunHeld = dashPressActive && dashHoldTimer >= dashHoldThreshold && !IsTransformed;
        }
        else
        {
            dashPressActive = false;
            IsRunHeld = false;
        }

        // Cooldown bookkeeping.
        if (dashCooldownTimer > 0f) dashCooldownTimer -= Time.deltaTime;
    }

    // FixedUpdate: physics only.
    private void FixedUpdate()
    {
        if (dashPassThrough || slamPassThrough || invulnerabilityPassThrough)
            RefreshEnemyCollisionIgnores();

        // Ground check — overlap box at the feet.
        IsGrounded = Physics2D.OverlapBox(
            (Vector2)transform.position + groundCheckOffset,
            groundCheckSize,
            0f,
            groundLayer);

        if (IsGrounded)
        {
            lastGroundedTime = Time.time;
            CanAirDash = true;
        }

        // While locked (dash / slam / stun) the ability coroutines own velocity.
        if (MovementLocked) return;

        // Horizontal movement.
        float xVel;
        if (IsTransformed)
        {
            // Rolling only.
            xVel = MoveInput.x * transformedRollSpeed;
        }
        else
        {
            float speed = IsRunHeld ? runSpeed : walkSpeed;
            xVel = MoveInput.x * speed * currentSpeedMultiplier;
        }
        rb.linearVelocity = new Vector2(xVel, rb.linearVelocity.y);

        // Jump — buffered & coyote — disabled while transformed.
        if (!IsTransformed) TryJump();

        // Variable-jump and falling gravity tuning.
        ApplyJumpGravityModifier();

        // Fall speed cap.
        if (maxFallSpeed > 0f && rb.linearVelocity.y < -maxFallSpeed)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, -maxFallSpeed);
    }

    // ==================================================================
    // Jump
    // ==================================================================
    private void TryJump()
    {
        bool coyoteOk = (Time.time - lastGroundedTime) <= coyoteTime;
        bool bufferOk = (Time.time - lastJumpPressedTime) <= jumpBufferTime;
        if (coyoteOk && bufferOk)
        {
            float jf = slowAffectsJump ? jumpForce * currentSpeedMultiplier : jumpForce;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jf);
            lastJumpPressedTime = -999f;
            lastGroundedTime = -999f;
        }
    }

    private void ApplyJumpGravityModifier()
    {
        if (rb.linearVelocity.y < 0f)
            rb.gravityScale = OriginalGravity * fallGravityMultiplier;
        else if (rb.linearVelocity.y > 0f && !jumpAction.IsPressed())
            rb.gravityScale = OriginalGravity * lowJumpMultiplier;
        else
            rb.gravityScale = OriginalGravity;
    }

    // ==================================================================
    // Dash
    // ==================================================================
    private bool CanDash()
    {
        return !IsDashing
            && !IsStunned
            && !IsSlamming
            && !IsTransformed
            && dashCooldownTimer <= 0f
            && (IsGrounded || CanAirDash);
    }

    private void TryDash()
    {
        if (dashCoroutine != null) StopCoroutine(dashCoroutine);
        dashCoroutine = StartCoroutine(DashRoutine());
    }

    private IEnumerator DashRoutine()
    {
        IsDashing = true;
        SetDashEnemyPassThrough(true);
        dashCooldownTimer = dashCooldown;
        if (!IsGrounded) CanAirDash = false;

        float savedGravity = rb.gravityScale;
        rb.gravityScale = 0f;
        rb.linearVelocity = new Vector2(FacingDirection * dashSpeed * currentSpeedMultiplier, 0f);

        yield return new WaitForSeconds(dashDuration);

        rb.gravityScale = savedGravity;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x * 0.4f, rb.linearVelocity.y);
        IsDashing = false;
        SetDashEnemyPassThrough(false);
        dashCoroutine = null;
    }

    public void InterruptDash()
    {
        if (dashCoroutine != null)
        {
            StopCoroutine(dashCoroutine);
            dashCoroutine = null;
        }
        if (IsDashing)
        {
            rb.gravityScale = OriginalGravity;
            IsDashing = false;
        }
        SetDashEnemyPassThrough(false);
    }

    public void SetDashEnemyPassThrough(bool enabled)
    {
        dashPassThrough = enabled;
        RefreshEnemyCollisionIgnores();
    }

    public void SetSlamEnemyPassThrough(bool enabled)
    {
        slamPassThrough = enabled;
        RefreshEnemyCollisionIgnores();
    }

    public void SetInvulnerabilityEnemyPassThrough(bool enabled)
    {
        invulnerabilityPassThrough = enabled;
        RefreshEnemyCollisionIgnores();
    }

    private void RefreshEnemyCollisionIgnores()
    {
        bool ignoreEnemyBodies = dashPassThrough || slamPassThrough || invulnerabilityPassThrough;

        if (ignoreEnemyBodies)
        {
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            Collider2D[] sceneColliders = FindObjectsByType<Collider2D>(FindObjectsSortMode.None);
            foreach (Collider2D other in sceneColliders)
            {
                if (enemyLayer < 0 || other == col || other.gameObject.layer != enemyLayer || other.attachedRigidbody == rb)
                    continue;

                if (!enemyBodyColliders.Contains(other))
                    enemyBodyColliders.Add(other);
                Physics2D.IgnoreCollision(col, other, true);
            }
            return;
        }

        foreach (Collider2D enemyCollider in enemyBodyColliders)
        {
            if (enemyCollider != null && col != null)
                Physics2D.IgnoreCollision(col, enemyCollider, false);
        }
        enemyBodyColliders.RemoveAll(enemyCollider => enemyCollider == null);
    }

    // ==================================================================
    // External hooks
    // ==================================================================
    public void SetStunned(bool stunned) { IsStunned = stunned; }

    public void ApplyKnockback(Vector2 velocity)
    {
        rb.linearVelocity = velocity;
    }

    // Called by PlayerCombat when a down-attack hits a pogoable target.
    public void PogoBounce(float upwardVelocity)
    {
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, upwardVelocity);
        CanAirDash = true;
    }

    // Called by PlayerHealth on damage — cleanly end in-flight abilities.
    public void InterruptAbilities()
    {
        InterruptDash();
        var combat = GetComponent<PlayerCombat>();
        if (combat != null) combat.InterruptAll();
    }

    // ==================================================================
    // Slow (applied by enemy projectiles)
    // ==================================================================
    public bool IsSlowed => currentSpeedMultiplier < 1f;

    private Coroutine slowCoroutine;

    // Re-applying a slow refreshes the duration; it does not stack multiplicatively.
    // Ignored while transformed. Call ClearSlow externally on death or respawn.
    public void ApplySlow(float multiplier, float duration, bool affectsJump)
    {
        if (IsTransformed) return;
        if (duration <= 0f) return;
        if (slowCoroutine != null) StopCoroutine(slowCoroutine);
        slowCoroutine = StartCoroutine(SlowRoutine(Mathf.Clamp01(multiplier), duration, affectsJump));
    }

    public void ClearSlow()
    {
        if (slowCoroutine != null) { StopCoroutine(slowCoroutine); slowCoroutine = null; }
        currentSpeedMultiplier = 1f;
        slowAffectsJump = false;
    }

    private IEnumerator SlowRoutine(float multiplier, float duration, bool affectsJump)
    {
        currentSpeedMultiplier = multiplier;
        slowAffectsJump = affectsJump;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            // Transforming mid-slow immediately clears it.
            if (IsTransformed) break;
            elapsed += Time.deltaTime;
            yield return null;
        }
        currentSpeedMultiplier = 1f;
        slowAffectsJump = false;
        slowCoroutine = null;
    }

    // ==================================================================
    // Gizmos
    // ==================================================================
    private void OnDrawGizmosSelected()
    {
        // Ground check — green.
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube((Vector2)transform.position + groundCheckOffset, groundCheckSize);
    }
}
