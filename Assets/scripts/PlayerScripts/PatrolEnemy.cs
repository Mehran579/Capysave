// Patrolling enemy with two attacks (low lunge + charged projectile).
// Implements IDamageable so the player's attacks (OverlapBoxAll on enemyLayer, fireballs, eagle purge, pogo)
// damage it with no extra wiring. Put the GameObject on the Enemy layer.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Collider2D))]
public class PatrolEnemy : MonoBehaviour, IDamageable
{
    public enum State { Patrol, WaitAtEdge, Aggravated, AttackLunge, AttackShoot, Stunned, Dead }

    // ==================================================================
    // Inspector — Health
    // ==================================================================
    [Header("Health")]
    [Min(1)] [SerializeField] private int maxHealth = 8;
    [Min(0f)] [SerializeField] private float hitKnockback = 4f;
    [Min(0.01f)] [SerializeField] private float hitFlashDuration = 0.08f;
    [SerializeField] private Color hitFlashColor = Color.red;
    [Min(0)] [SerializeField] private int contactDamage = 1;
    [SerializeField] private bool canBeStaggered = true;
    [Min(0f)] [SerializeField] private float staggerDuration = 0.25f;
    [Tooltip("Delay between hitting 0 HP and destroying/disabling.")]
    [Min(0f)] [SerializeField] private float deathDelay = 0.6f;
    [Tooltip("True: Destroy on death. False: SetActive(false).")]
    [SerializeField] private bool destroyOnDeath = true;

    // ==================================================================
    // Inspector — Patrol
    // ==================================================================
    [Header("Patrol")]
    [Min(0f)] [SerializeField] private float patrolSpeed = 2.5f;
    [Tooltip("Left patrol point as an offset from the spawn position (captured in Awake).")]
    [SerializeField] private Vector2 patrolLeftPoint = new Vector2(-4f, 0f);
    [Tooltip("Right patrol point as an offset from the spawn position.")]
    [SerializeField] private Vector2 patrolRightPoint = new Vector2(4f, 0f);
    [Min(0f)] [SerializeField] private float waitTimeAtEdge = 1.0f;
    [SerializeField] private bool startFacingRight = true;

    [Header("Ledge & Wall Checks")]
    [SerializeField] private LayerMask groundLayer;
    [Tooltip("Local offset of the ledge-check ray origin (x is flipped by facing).")]
    [SerializeField] private Vector2 ledgeCheckOffset = new Vector2(0.6f, -0.8f);
    [Min(0.05f)] [SerializeField] private float ledgeCheckDistance = 1.2f;
    [Tooltip("Local y offset of the wall-check ray origin (ray goes horizontal in facing direction).")]
    [SerializeField] private float wallCheckYOffset = 0f;
    [Min(0.05f)] [SerializeField] private float wallCheckDistance = 0.7f;

    // ==================================================================
    // Inspector — Player Detection
    // ==================================================================
    [Header("Player Detection")]
    [SerializeField] private LayerMask playerLayer;
    [Min(0f)] [SerializeField] private float detectionRange = 7f;
    [Min(0.1f)] [SerializeField] private float detectionHeight = 2.5f;
    [SerializeField] private Vector2 detectionCenterOffset = new Vector2(0f, 0.2f);
    [SerializeField] private bool useLineOfSight = true;
    [Min(0f)] [SerializeField] private float losePlayerTime = 1.5f;

    // ==================================================================
    // Inspector — Attack Selection
    // ==================================================================
    [Header("Attack Selection")]
    [Min(0f)] [SerializeField] private float lungeMaxDistance = 3.5f;
    [Min(0f)] [SerializeField] private float shootMinDistance = 5f;
    [Range(0f, 1f)] [SerializeField] private float closeRangeLungeChance = 0.75f;
    [Range(0f, 1f)] [SerializeField] private float midRangeLungeChance = 0.5f;
    [Range(0f, 1f)] [SerializeField] private float farRangeShootChance = 0.9f;
    [Min(0f)] [SerializeField] private float attackCooldown = 1.2f;
    [Min(0f)] [SerializeField] private float decisionDelay = 0.3f;
    [Min(0f)] [SerializeField] private float aggravatedSpeed = 3.5f;
    [Min(0f)] [SerializeField] private float contactFollowDistance = 0.75f;
    [SerializeField] private Color aggravatedTint = new Color(1f, 0.65f, 0.25f);

    // ==================================================================
    // Inspector — Attack A: Low Lunge
    // ==================================================================
    [Header("Attack A — Low Lunge")]
    [Min(0f)] [SerializeField] private float lungeWindupTime = 0.4f;
    [Min(0f)] [SerializeField] private float lungeHorizontalDistance = 3.5f;
    [Tooltip("Small hop — keep this noticeably less than lungeHorizontalDistance.")]
    [Min(0f)] [SerializeField] private float lungeHopHeight = 0.6f;
    [Min(0.05f)] [SerializeField] private float lungeDuration = 0.35f;
    [Tooltip("Normalized time inside the lunge when the hitbox first becomes active.")]
    [Range(0f, 1f)] [SerializeField] private float lungeActiveStart = 0.1f;
    [Tooltip("Normalized duration (as a fraction of lungeDuration) the hitbox stays active.")]
    [Range(0f, 1f)] [SerializeField] private float lungeActiveDuration = 0.6f;
    [SerializeField] private Vector2 lungeHitboxOffset = new Vector2(0.8f, 0f);
    [SerializeField] private Vector2 lungeHitboxSize = new Vector2(1.3f, 1f);
    [Min(0)] [SerializeField] private int lungeDamage = 1;
    [Min(0f)] [SerializeField] private float lungeKnockback = 8f;
    [Min(0f)] [SerializeField] private float lungeRecoveryTime = 0.35f;
    [SerializeField] private Color lungeWindupTint = new Color(1f, 0.6f, 0.2f);

    // ==================================================================
    // Inspector — Attack B: Charged Projectile
    // ==================================================================
    [Header("Attack B — Charged Projectile")]
    [SerializeField] private EnemyProjectile projectilePrefab;
    [SerializeField] private Vector2 projectileSpawnOffset = new Vector2(0.9f, 0.2f);
    [Min(0.1f)] [SerializeField] private float chargeUpTime = 1.6f;
    [Min(0f)] [SerializeField] private float shootRecoveryTime = 0.4f;
    [SerializeField] private Color chargeTint = new Color(0.2f, 0.4f, 1f);
    [Tooltip("Pulses per second during the charge telegraph.")]
    [Range(1f, 15f)] [SerializeField] private float chargePulseRate = 4f;

    // ==================================================================
    // Inspector — Animation (optional)
    // ==================================================================
    [Header("Animation (optional)")]
    [SerializeField] private Animator animator;

    // ==================================================================
    // Inspector — Events
    // ==================================================================
    [Header("Events")]
    public UnityEvent<int> OnDamaged;
    public UnityEvent OnDied;

    // ==================================================================
    // Cached / runtime
    // ==================================================================
    private Rigidbody2D rb;
    private SpriteRenderer sr;
    private Collider2D col;
    private Color baseColor;
    private float originalGravity;

    private State currentState = State.Patrol;
    public State CurrentState => currentState;

    private int facingDirection = 1;
    private int currentHealth;
    private Vector3 spawnPosition;
    private Transform playerTransform;
    private float losePlayerTimer;
    private float cooldownTimer;

    private Coroutine stateCoroutine;
    private Coroutine flashCoroutine;

    private bool isAttacking;
    private State lastAttackChoice = State.AttackLunge;
    private bool lungeHitboxActive; // for gizmos

    // Patrol world points — captured spawn in play mode, live transform in edit mode (preview).
    private Vector2 OriginForPatrol => Application.isPlaying ? (Vector2)spawnPosition : (Vector2)transform.position;
    public Vector2 LeftWorld => OriginForPatrol + patrolLeftPoint;
    public Vector2 RightWorld => OriginForPatrol + patrolRightPoint;

    // ==================================================================
    // Unity messages
    // ==================================================================
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
        baseColor = sr.color;
        originalGravity = rb.gravityScale;
        spawnPosition = transform.position;
        currentHealth = maxHealth;
        facingDirection = startFacingRight ? 1 : -1;
        sr.flipX = facingDirection < 0;
    }

    private void Start()
    {
        EnterState(State.Patrol);
    }

    private void Update()
    {
        if (currentState == State.Dead) return;
        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;
        TickDetection();
    }

    // ==================================================================
    // State machine
    // ==================================================================
    private void EnterState(State next)
    {
        if (stateCoroutine != null) { StopCoroutine(stateCoroutine); stateCoroutine = null; }

        // Transient-effect cleanup so coroutines can't leak into the next state.
        rb.gravityScale = originalGravity;
        sr.color = baseColor;
        lungeHitboxActive = false;

        currentState = next;
        isAttacking = (next == State.AttackLunge || next == State.AttackShoot);

        switch (next)
        {
            case State.Patrol: stateCoroutine = StartCoroutine(PatrolRoutine()); break;
            case State.WaitAtEdge: stateCoroutine = StartCoroutine(WaitAtEdgeRoutine()); break;
            case State.Aggravated: stateCoroutine = StartCoroutine(AggravatedRoutine()); break;
            case State.AttackLunge: stateCoroutine = StartCoroutine(LungeRoutine()); break;
            case State.AttackShoot: stateCoroutine = StartCoroutine(ShootRoutine()); break;
            case State.Stunned: stateCoroutine = StartCoroutine(StunnedRoutine()); break;
            case State.Dead: stateCoroutine = StartCoroutine(DeathRoutine()); break;
        }
    }

    // ==================================================================
    // Detection
    // ==================================================================
    private void TickDetection()
    {
        if (currentState == State.Dead || currentState == State.Stunned) return;

        Vector2 center = (Vector2)transform.position + detectionCenterOffset;
        Vector2 size = new Vector2(detectionRange * 2f, detectionHeight);
        Collider2D hit = Physics2D.OverlapBox(center, size, 0f, playerLayer);

        bool seen = false;
        if (hit != null)
        {
            if (!useLineOfSight)
                seen = true;
            else
            {
                Vector2 eye = center;
                Vector2 target = hit.bounds.center;
                Vector2 dir = target - eye;
                RaycastHit2D wall = Physics2D.Raycast(eye, dir.normalized, dir.magnitude, groundLayer);
                seen = wall.collider == null;
            }
            if (seen) playerTransform = hit.transform;
        }

        if (seen)
        {
            losePlayerTimer = losePlayerTime;
            if (currentState == State.Patrol || currentState == State.WaitAtEdge)
            {
                EnterState(State.Aggravated);
            }
        }
        else if (playerTransform != null)
        {
            losePlayerTimer -= Time.deltaTime;
            if (losePlayerTimer <= 0f)
            {
                playerTransform = null;
                if (currentState == State.Aggravated)
                    EnterState(State.Patrol);
            }
        }
    }

    // ==================================================================
    // Patrol
    // ==================================================================
    private IEnumerator PatrolRoutine()
    {
        // animator.SetBool("IsWalking", true);
        if (!CanMoveForward() || ReachedPatrolEdge())
            Flip();

        while (true)
        {
            if (!CanMoveForward() || ReachedPatrolEdge())
            {
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                // animator.SetBool("IsWalking", false);
                EnterState(State.WaitAtEdge);
                yield break;
            }
            rb.linearVelocity = new Vector2(facingDirection * patrolSpeed, rb.linearVelocity.y);
            yield return new WaitForFixedUpdate();
        }
    }

    private IEnumerator WaitAtEdgeRoutine()
    {
        // animator.SetBool("IsWalking", false);
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        yield return new WaitForSeconds(waitTimeAtEdge);
        Flip();
        EnterState(State.Patrol);
    }

    private bool ReachedPatrolEdge()
    {
        float x = transform.position.x;
        if (facingDirection > 0 && x >= RightWorld.x) return true;
        if (facingDirection < 0 && x <= LeftWorld.x) return true;
        return false;
    }

    private bool CanMoveForward() => HasGroundAhead() && !HasWallAhead();

    private bool HasGroundAhead()
    {
        Vector2 origin = (Vector2)transform.position
            + new Vector2(Mathf.Abs(ledgeCheckOffset.x) * facingDirection, ledgeCheckOffset.y);
        return Physics2D.Raycast(origin, Vector2.down, ledgeCheckDistance, groundLayer).collider != null;
    }

    private bool HasWallAhead()
    {
        Vector2 origin = (Vector2)transform.position + new Vector2(0f, wallCheckYOffset);
        return Physics2D.Raycast(origin, new Vector2(facingDirection, 0f), wallCheckDistance, groundLayer).collider != null;
    }

    private void Flip()
    {
        facingDirection = -facingDirection;
        sr.flipX = facingDirection < 0;
        // animator.SetTrigger("Turn");
    }

    // ==================================================================
    // Chase & decision
    // ==================================================================
    private IEnumerator AggravatedRoutine()
    {
        sr.color = aggravatedTint;
        while (currentState == State.Aggravated)
        {
            if (playerTransform == null)
            {
                EnterState(State.Patrol);
                yield break;
            }

            // Always face the player while chasing.
            float dx = playerTransform.position.x - transform.position.x;
            int desired = dx >= 0f ? 1 : -1;
            if (desired != facingDirection) Flip();

            // Follow all the way into contact range; patrol endpoints don't bind
            // an aggravated cat, though it still respects walls and ledges.
            if (Mathf.Abs(dx) > contactFollowDistance && CanMoveForward())
                rb.linearVelocity = new Vector2(facingDirection * aggravatedSpeed, rb.linearVelocity.y);
            else
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

            if (cooldownTimer <= 0f)
            {
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                yield return new WaitForSeconds(decisionDelay);
                if (currentState != State.Aggravated) yield break;
                if (playerTransform == null) { EnterState(State.Patrol); yield break; }
                ChooseAttack();
                yield break;
            }

            yield return null;
        }
    }

    private void ChooseAttack()
    {
        float dist = Mathf.Abs(playerTransform.position.x - transform.position.x);
        float roll = Random.value;
        // The lunge is never selected outside its configured horizontal range.
        // Use the mid-range weight in the outer half of that range.
        float closeRangeBoundary = lungeMaxDistance * 0.5f;
        bool pickLunge = false;
        if (dist <= lungeMaxDistance)
        {
            float lungeChance = dist <= closeRangeBoundary ? closeRangeLungeChance : midRangeLungeChance;
            pickLunge = roll <= lungeChance;
        }
        else if (dist > shootMinDistance && roll > farRangeShootChance)
        {
            // At long range, occasionally hold position and retry later instead
            // of firing every decision cycle. Never substitute an out-of-range lunge.
            cooldownTimer = attackCooldown;
            EnterState(State.Aggravated);
            return;
        }

        lastAttackChoice = pickLunge ? State.AttackLunge : State.AttackShoot;
        EnterState(lastAttackChoice);
    }

    // ==================================================================
    // Attack A — Low Lunge
    // ==================================================================
    private IEnumerator LungeRoutine()
    {
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        // animator.SetTrigger("LungeWindup");

        // Windup telegraph.
        sr.color = lungeWindupTint;
        yield return new WaitForSeconds(lungeWindupTime);
        sr.color = baseColor;

        // Recheck after the telegraph so the cat doesn't lunge at a player who
        // has already moved out of range.
        if (playerTransform == null
            || Mathf.Abs(playerTransform.position.x - transform.position.x) > lungeMaxDistance)
        {
            cooldownTimer = attackCooldown;
            EnterState(playerTransform != null ? State.Aggravated : State.Patrol);
            yield break;
        }

        // animator.SetTrigger("LungeActive");
        float startX = transform.position.x;
        float startY = transform.position.y;
        if (playerTransform.position.x != startX)
        {
            int targetFacing = playerTransform.position.x > startX ? 1 : -1;
            if (targetFacing != facingDirection) Flip();
        }
        // Aim at the player's current position, but cap the lunge's travel.
        // This may carry the cat past its patrol endpoints; it will walk back after.
        float targetX = Mathf.MoveTowards(startX, playerTransform.position.x, lungeHorizontalDistance);

        rb.gravityScale = 0f;
        rb.linearVelocity = Vector2.zero;

        HashSet<IDamageable> hitSet = new HashSet<IDamageable>();
        float elapsed = 0f;
        while (elapsed < lungeDuration)
        {
            float t = Mathf.Clamp01(elapsed / lungeDuration);

            // Patrol endpoints don't restrict the attack, but ledges and walls do.
            if (!CanMoveForward()) break;

            float x = Mathf.Lerp(startX, targetX, t);
            float y = startY + Mathf.Sin(t * Mathf.PI) * lungeHopHeight;
            rb.MovePosition(new Vector2(x, y));

            lungeHitboxActive = t >= lungeActiveStart && t <= (lungeActiveStart + lungeActiveDuration);
            if (lungeHitboxActive)
            {
                Vector2 hbCenter = new Vector2(x, y)
                    + new Vector2(Mathf.Abs(lungeHitboxOffset.x) * facingDirection, lungeHitboxOffset.y);
                Collider2D[] hits = Physics2D.OverlapBoxAll(hbCenter, lungeHitboxSize, 0f, playerLayer);
                foreach (Collider2D c in hits)
                {
                    IDamageable d = c.GetComponentInParent<IDamageable>();
                    if (d == null || hitSet.Contains(d)) continue;
                    hitSet.Add(d);
                    // Damage goes through player IDamageable; i-frames + transformed are respected there.
                    Vector2 kb = new Vector2(facingDirection, 0.35f).normalized * lungeKnockback;
                    d.TakeDamage(lungeDamage, kb);
                }
            }

            elapsed += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        rb.gravityScale = originalGravity;
        rb.linearVelocity = Vector2.zero;
        lungeHitboxActive = false;

        // Recover at the lunge's starting position by walking back, even if the
        // attack carried the cat outside its patrol band.
        float returnTimeout = Mathf.Abs(transform.position.x - startX) / Mathf.Max(0.1f, patrolSpeed) + 1f;
        float returnElapsed = 0f;
        while (Mathf.Abs(transform.position.x - startX) > 0.08f && returnElapsed < returnTimeout)
        {
            int returnFacing = transform.position.x < startX ? 1 : -1;
            if (returnFacing != facingDirection) Flip();
            if (!CanMoveForward()) break;

            rb.linearVelocity = new Vector2(facingDirection * patrolSpeed, rb.linearVelocity.y);
            returnElapsed += Time.deltaTime;
            yield return null;
        }
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

        // animator.SetTrigger("LungeRecovery");
        yield return new WaitForSeconds(lungeRecoveryTime);

        cooldownTimer = attackCooldown;
        EnterState(playerTransform != null ? State.Aggravated : State.Patrol);
    }

    // ==================================================================
    // Attack B — Charged Projectile
    // ==================================================================
    private IEnumerator ShootRoutine()
    {
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        // animator.SetTrigger("ChargeUp");

        // Pulsing telegraph. Interrupted externally by a stagger (EnterState(Stunned)).
        float elapsed = 0f;
        while (elapsed < chargeUpTime)
        {
            float t = Mathf.PingPong(elapsed * chargePulseRate, 1f);
            sr.color = Color.Lerp(baseColor, chargeTint, t);
            elapsed += Time.deltaTime;
            yield return null;
        }
        sr.color = baseColor;

        // animator.SetTrigger("Shoot");
        if (projectilePrefab != null)
        {
            Vector2 spawn = (Vector2)transform.position
                + new Vector2(Mathf.Abs(projectileSpawnOffset.x) * facingDirection, projectileSpawnOffset.y);
            EnemyProjectile p = Instantiate(projectilePrefab, spawn, Quaternion.identity);
            p.Launch(facingDirection);
        }
        else
        {
            Debug.LogWarning($"{nameof(PatrolEnemy)}: no projectilePrefab assigned on '{name}'.");
        }

        yield return new WaitForSeconds(shootRecoveryTime);
        cooldownTimer = attackCooldown;
        EnterState(playerTransform != null ? State.Aggravated : State.Patrol);
    }

    // ==================================================================
    // Stagger / Death
    // ==================================================================
    private IEnumerator StunnedRoutine()
    {
        // animator.SetTrigger("Hurt");
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y);
        yield return new WaitForSeconds(staggerDuration);
        EnterState(playerTransform != null ? State.Aggravated : State.Patrol);
    }

    private IEnumerator DeathRoutine()
    {
        // animator.SetTrigger("Die");
        OnDied?.Invoke();
        rb.linearVelocity = Vector2.zero;
        rb.bodyType = RigidbodyType2D.Kinematic;
        col.enabled = false;
        yield return new WaitForSeconds(deathDelay);
        if (destroyOnDeath) Destroy(gameObject);
        else gameObject.SetActive(false);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (currentState == State.Dead || contactDamage <= 0) return;
        if (!collision.gameObject.CompareTag("Player")) return;

        PlayerHealth player = collision.gameObject.GetComponentInParent<PlayerHealth>();
        if (player == null || player.IsInvulnerable) return;

        float awayX = Mathf.Sign(player.transform.position.x - transform.position.x);
        if (awayX == 0f) awayX = -facingDirection;
        player.TakeDamage(contactDamage, new Vector2(awayX, 0.25f));
    }

    // ==================================================================
    // IDamageable
    // ==================================================================
    public void TakeDamage(int amount, Vector2 hitDirection)
    {
        if (currentState == State.Dead) return;

        currentHealth = Mathf.Max(0, currentHealth - amount);
        OnDamaged?.Invoke(currentHealth);

        // Small knockback (only for dynamic bodies).
        if (rb.bodyType == RigidbodyType2D.Dynamic)
        {
            Vector2 kb = hitDirection;
            if (kb.sqrMagnitude < 0.01f) kb = new Vector2(-facingDirection, 0.2f);
            rb.linearVelocity = new Vector2(kb.normalized.x * hitKnockback, Mathf.Max(rb.linearVelocity.y, 0.5f));
        }

        if (currentHealth <= 0)
        {
            EnterState(State.Dead);
            if (flashCoroutine != null) StopCoroutine(flashCoroutine);
            return;
        }

        if (canBeStaggered)
            EnterState(State.Stunned); // cancels any in-flight attack/charge

        if (flashCoroutine != null) StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(HitFlashRoutine());
    }

    private IEnumerator HitFlashRoutine()
    {
        sr.color = hitFlashColor;
        yield return new WaitForSeconds(hitFlashDuration);
        sr.color = baseColor;
        flashCoroutine = null;
    }

    // ==================================================================
    // Gizmos — each hitbox / range in its own color.
    // Edit mode uses transform.position for the preview; play mode uses captured spawn.
    // ==================================================================
    private void OnDrawGizmos()
    {
        Vector3 origin = Application.isPlaying ? spawnPosition : transform.position;
        Vector3 leftW = origin + (Vector3)patrolLeftPoint;
        Vector3 rightW = origin + (Vector3)patrolRightPoint;

        // Patrol shaded area (light green).
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.12f);
        Vector3 areaCenter = (leftW + rightW) * 0.5f + Vector3.up * 0.5f;
        Vector3 areaSize = new Vector3(Mathf.Abs(rightW.x - leftW.x), 1f, 0.01f);
        Gizmos.DrawCube(areaCenter, areaSize);

        // Patrol endpoints & connecting line (green).
        Gizmos.color = new Color(0.2f, 1f, 0.4f);
        Gizmos.DrawSphere(leftW, 0.15f);
        Gizmos.DrawSphere(rightW, 0.15f);
        Gizmos.DrawLine(leftW, rightW);

        int face = Application.isPlaying ? facingDirection : (startFacingRight ? 1 : -1);

        // Ledge check — green if ground, red if drop-off.
        Vector3 ledgeStart = transform.position
            + new Vector3(Mathf.Abs(ledgeCheckOffset.x) * face, ledgeCheckOffset.y, 0f);
        bool groundAhead = Physics2D.Raycast(ledgeStart, Vector2.down, ledgeCheckDistance, groundLayer).collider != null;
        Gizmos.color = groundAhead ? Color.green : Color.red;
        Gizmos.DrawLine(ledgeStart, ledgeStart + Vector3.down * ledgeCheckDistance);

        // Wall check — red if wall hit (blocked), green if clear.
        Vector3 wallStart = transform.position + new Vector3(0f, wallCheckYOffset, 0f);
        bool wallAhead = Physics2D.Raycast(wallStart, new Vector2(face, 0f), wallCheckDistance, groundLayer).collider != null;
        Gizmos.color = wallAhead ? Color.red : Color.green;
        Gizmos.DrawLine(wallStart, wallStart + new Vector3(face * wallCheckDistance, 0f, 0f));

        // Detection box (yellow).
        Gizmos.color = new Color(1f, 1f, 0f, 0.5f);
        Vector3 detCenter = transform.position + (Vector3)detectionCenterOffset;
        Gizmos.DrawWireCube(detCenter, new Vector3(detectionRange * 2f, detectionHeight, 0f));

        // Attack range lines at feet (lunge red, shoot blue). Highlighted when that one is the live choice.
        Vector3 feet = transform.position + Vector3.down * 0.6f;
        bool chosenLunge = Application.isPlaying && lastAttackChoice == State.AttackLunge
            && (currentState == State.AttackLunge || currentState == State.Aggravated);
        bool chosenShoot = Application.isPlaying && lastAttackChoice == State.AttackShoot
            && (currentState == State.AttackShoot || currentState == State.Aggravated);

        Gizmos.color = chosenLunge ? new Color(1f, 0.1f, 0.1f, 1f) : new Color(1f, 0.3f, 0.3f, 0.5f);
        Gizmos.DrawLine(feet + Vector3.left * lungeMaxDistance, feet + Vector3.right * lungeMaxDistance);

        Gizmos.color = chosenShoot ? new Color(0.2f, 0.4f, 1f, 1f) : new Color(0.4f, 0.6f, 1f, 0.5f);
        Gizmos.DrawLine(feet + Vector3.left * shootMinDistance + Vector3.up * 0.15f,
                        feet + Vector3.right * shootMinDistance + Vector3.up * 0.15f);

        // Lunge hitbox — red, brighter while actually active.
        Gizmos.color = lungeHitboxActive ? new Color(1f, 0f, 0f, 0.95f) : new Color(0.9f, 0.15f, 0.15f, 0.4f);
        Vector3 hbCenter = transform.position
            + new Vector3(Mathf.Abs(lungeHitboxOffset.x) * face, lungeHitboxOffset.y, 0f);
        Gizmos.DrawWireCube(hbCenter, lungeHitboxSize);

        // Lunge landing-point projection (orange line + sphere).
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.8f);
        Vector3 landingTarget = transform.position + new Vector3(lungeHorizontalDistance * face, 0f, 0f);
        Gizmos.DrawLine(transform.position, landingTarget);
        Gizmos.DrawSphere(landingTarget, 0.1f);

        // Projectile spawn + shot-path preview (cyan).
        Gizmos.color = new Color(0.3f, 0.75f, 1f);
        Vector3 projSpawn = transform.position
            + new Vector3(Mathf.Abs(projectileSpawnOffset.x) * face, projectileSpawnOffset.y, 0f);
        Gizmos.DrawWireSphere(projSpawn, 0.1f);
        Gizmos.DrawLine(projSpawn, projSpawn + new Vector3(face * Mathf.Max(shootMinDistance, 4f), 0f, 0f));
    }

    // ==================================================================
    // Animator parameters block — create these on your Animator Controller
    // and uncomment the matching animator.SetXxx lines above:
    //
    //   Bool    IsWalking
    //   Trigger Turn
    //   Trigger Alert
    //   Trigger LungeWindup
    //   Trigger LungeActive
    //   Trigger LungeRecovery
    //   Trigger ChargeUp
    //   Trigger Shoot
    //   Trigger Hurt
    //   Trigger Die
    // ==================================================================
}
