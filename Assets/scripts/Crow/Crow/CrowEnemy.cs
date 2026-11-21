// =====================================================================
// CrowEnemy.cs
// Standalone Silksong-"Craw"-inspired aerial enemy for a 2D side-scroller.
// Depends only on an IDamageable interface with:
//     void TakeDamage(int amount, Vector2 hitDirection);
// Pair with CrowProjectile.cs for the shoot attack.
// Compiles on Unity 2022/2023 LTS and Unity 6.
// (Unity 6 may warn that Rigidbody2D.velocity is deprecated in favour of
//  linearVelocity; the property is still valid and functions identically.)
// =====================================================================

using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
[DisallowMultipleComponent]
public class CrowEnemy : MonoBehaviour, IDamageable
{
    // ---------------------------------------------------------------
    // STATE MACHINE
    // ---------------------------------------------------------------
    public enum CrowState { Idle, Alert, AttackDive, AttackShoot, Retreat, Stunned, Dead }
    public enum AttackChoice { None, Dive, Shoot }

    // ---------------------------------------------------------------
    // HEALTH & DAMAGE
    // ---------------------------------------------------------------
    [Header("Health & Damage")]
    [SerializeField, Min(1), Tooltip("Max hit points. Small enemy, keep this low.")]
    private int maxHealth = 3;

    [SerializeField, Min(0f), Tooltip("Knockback speed applied when the crow takes a hit.")]
    private float hitKnockback = 6f;

    [SerializeField, Min(0), Tooltip("Damage applied while the player remains in contact with the crow.")]
    private int contactDamage = 1;

    [SerializeField, Min(0f), Tooltip("Seconds the hit-flash color stays on.")]
    private float hitFlashDuration = 0.08f;

    [SerializeField, Tooltip("Color used for the hit flash.")]
    private Color hitFlashColor = Color.white;

    [SerializeField, Tooltip("If true, taking a hit cancels the current attack/windup.")]
    private bool canBeStaggered = true;

    [SerializeField, Min(0f), Tooltip("Seconds the crow is stunned after a staggering hit.")]
    private float staggerDuration = 0.25f;

    [SerializeField, Min(0f), Tooltip("Seconds the death animation/fade takes.")]
    private float deathDuration = 0.8f;

    [SerializeField, Tooltip("If true, switches the body to Dynamic + gravity on death so it falls.")]
    private bool fallOnDeath = true;

    [SerializeField, Min(0f), Tooltip("Gravity scale applied during the fall death.")]
    private float deathGravityScale = 2.5f;

    [SerializeField, Tooltip("If true, Destroy the GameObject at end of death; otherwise just SetActive(false).")]
    private bool destroyOnDeath = true;

    // ---------------------------------------------------------------
    // MOVEMENT — HOVER / BOB
    // ---------------------------------------------------------------
    [Header("Hover / Bob")]
    [SerializeField, Min(0f), Tooltip("Max distance the crow drifts from its home point while idling.")]
    private float hoverRadius = 2.5f;

    [SerializeField, Min(0f), Tooltip("Vertical amplitude of the constant idle bob (world units).")]
    private float bobAmplitude = 0.15f;

    [SerializeField, Min(0f), Tooltip("Bob oscillations per second.")]
    private float bobFrequency = 1.6f;

    [SerializeField, Min(0f), Tooltip("Soft pull applied when the crow drifts past the hover radius.")]
    private float returnForce = 3f;

    [SerializeField, Min(0f), Tooltip("Optional hard vertical limits around the home point (0 to disable).")]
    private Vector2 verticalLimits = new Vector2(1.5f, 1.5f); // (down, up) extents

    // ---------------------------------------------------------------
    // MOVEMENT — HOP (the twitchy flap bursts)
    // ---------------------------------------------------------------
    [Header("Flap Hop")]
    [SerializeField, Tooltip("Min/max seconds between random flap hops while idling/chasing.")]
    private Vector2 hopIntervalRange = new Vector2(0.4f, 1.1f);

    [SerializeField, Min(0f), Tooltip("Initial velocity of each flap hop.")]
    private float hopForce = 7f;

    [SerializeField, Min(0.01f), Tooltip("Seconds the hop impulse lasts before velocity settles.")]
    private float hopDuration = 0.18f;

    [SerializeField, Range(0f, 20f), Tooltip("Higher = faster ease-out after a hop (floaty glide to stop).")]
    private float hopEaseOut = 4.5f;

    [SerializeField, Range(0f, 1f), Tooltip("How strongly hops bias back toward home when far away. 0 = pure random.")]
    private float homeBias = 0.7f;

    [SerializeField, Min(0.05f), Tooltip("Radius of the ground-cast ahead of the hop.")]
    private float hopClearanceRadius = 0.25f;

    [SerializeField, Min(0.1f), Tooltip("Distance the ground-cast probes along the hop direction.")]
    private float hopClearanceDistance = 1.2f;

    [SerializeField, Range(1, 16), Tooltip("Max random directions tried before a hop is cancelled.")]
    private int hopDirectionAttempts = 6;

    // ---------------------------------------------------------------
    // DETECTION
    // ---------------------------------------------------------------
    [Header("Detection")]
    [SerializeField, Min(0f), Tooltip("Player detection radius around the crow.")]
    private float detectionRange = 7f;

    [SerializeField, Tooltip("If true, requires a clear line of sight (no ground in the way) to detect the player.")]
    private bool requireLineOfSight = true;

    [SerializeField, Min(0f), Tooltip("Seconds without a sighting before the crow forgets the player.")]
    private float losePlayerTime = 2.5f;

    [SerializeField, Min(0f), Tooltip("Seconds of 'alert' reaction before the first attack.")]
    private float alertTime = 0.35f;

    [SerializeField, Tooltip("Tint flashed while in the alert reaction.")]
    private Color alertColor = new Color(1f, 0.85f, 0.3f);

    // ---------------------------------------------------------------
    // ATTACK SELECTION
    // ---------------------------------------------------------------
    [Header("Attack Selection")]
    [SerializeField, Min(0f), Tooltip("Max distance at which a dive is considered.")]
    private float diveMaxDistance = 5.0f;

    [SerializeField, Min(0f), Tooltip("Min distance at which a shoot is considered.")]
    private float shootMinDistance = 3.5f;

    [SerializeField, Range(0f, 1f), Tooltip("Dive weight in the overlap zone where both attacks are legal.")]
    private float diveChance = 0.55f;

    [SerializeField, Range(0f, 1f), Tooltip("Shoot weight in the overlap zone where both attacks are legal.")]
    private float shootChance = 0.45f;

    [SerializeField, Min(1), Tooltip("Max times the same attack can be chosen in a row.")]
    private int maxRepeatAttacks = 2;

    [SerializeField, Min(0f), Tooltip("Seconds the crow waits after any attack before it can act again.")]
    private float attackCooldown = 1.2f;

    [SerializeField, Min(0f), Tooltip("Max distance from home the crow is allowed to stray while chasing/attacking.")]
    private float leashRadius = 6f;

    // ---------------------------------------------------------------
    // ATTACK A — DIVE
    // ---------------------------------------------------------------
    [Header("Attack A — Dive")]
    [SerializeField, Min(0f), Tooltip("Telegraph windup seconds before the dive fires.")]
    private float diveWindupTime = 0.55f;

    [SerializeField, Min(0f), Tooltip("Seconds before dive-end that the crow locks the target position.")]
    private float lockOnDelay = 0.12f;

    [SerializeField, Tooltip("Tint flashed during the dive windup.")]
    private Color diveWindupColor = new Color(1f, 0.4f, 0.4f);

    [SerializeField, Min(0.1f), Tooltip("Dive flight speed.")]
    private float diveSpeed = 14f;

    [SerializeField, Min(0f), Tooltip("Extra distance past the locked target before the dive ends.")]
    private float diveOvershoot = 1.5f;

    [SerializeField, Range(0f, 1f), Tooltip("Curvature of the dive arc. 0 = straight line.")]
    private float diveCurvature = 0.15f;

    [SerializeField, Min(0.1f), Tooltip("Speed flying back toward home after a dive.")]
    private float retreatSpeed = 6f;

    [SerializeField, Min(0f), Tooltip("Seconds of vulnerable idle after retreat finishes.")]
    private float diveRecoveryTime = 0.5f;

    [SerializeField, Tooltip("Dive hitbox offset (local, flipped by facing).")]
    private Vector2 diveHitboxOffset = new Vector2(0.35f, 0f);

    [SerializeField, Tooltip("Dive hitbox size.")]
    private Vector2 diveHitboxSize = new Vector2(0.9f, 0.9f);

    [SerializeField, Min(0), Tooltip("Damage dealt by the dive contact hitbox.")]
    private int diveDamage = 1;

    [SerializeField, Min(0f), Tooltip("Knockback speed applied to the player on a dive hit.")]
    private float diveKnockback = 8f;

    [SerializeField, Tooltip("If true, bounces off walls on dive impact. If false, enters Stunned instead.")]
    private bool bounceOffWallOnDive = true;

    [SerializeField, Min(0f), Tooltip("Rebound speed when bouncing off a wall during dive.")]
    private float diveBounceForce = 6f;

    [SerializeField, Min(0f), Tooltip("Seconds of stun after dive wall-impact (only if bounceOffWallOnDive is false).")]
    private float diveWallStunTime = 0.4f;

    // ---------------------------------------------------------------
    // ATTACK B — SHOOT (Ricochet projectile)
    // ---------------------------------------------------------------
    [Header("Attack B — Shoot")]
    [SerializeField, Min(0f), Tooltip("Charge-up seconds before the projectile(s) fire.")]
    private float shootWindupTime = 0.55f;

    [SerializeField, Tooltip("Tint flashed during the shoot windup.")]
    private Color shootWindupColor = new Color(0.4f, 0.7f, 1f);

    [SerializeField, Min(0f), Tooltip("Seconds of in-place recovery after firing.")]
    private float shootRecoveryTime = 0.35f;

    [SerializeField, Tooltip("Spawn offset for the projectile, local to the crow (flipped by facing).")]
    private Vector2 projectileSpawnOffset = new Vector2(0.4f, 0f);

    [SerializeField, Min(0.1f), Tooltip("Projectile launch speed.")]
    private float projectileSpeed = 9f;

    [SerializeField, Min(1), Tooltip("Projectiles fired per shot.")]
    private int projectilesPerShot = 1;

    [SerializeField, Range(0f, 90f), Tooltip("Total spread angle (degrees) distributed across projectiles.")]
    private float spreadAngle = 0f;

    [SerializeField, Tooltip("Projectile prefab reference (a GameObject with a CrowProjectile component).")]
    private CrowProjectile projectilePrefab;

    // ---------------------------------------------------------------
    // FACING & VISUALS
    // ---------------------------------------------------------------
    [Header("Facing & Visuals")]
    [SerializeField, Tooltip("If true, the sprite flips to face the player when one is tracked; otherwise it faces movement.")]
    private bool faceTowardsPlayer = true;

    [SerializeField, Tooltip("Sprite renderer used for hit-flash and tint telegraphs.")]
    private SpriteRenderer spriteRenderer;

    [SerializeField, Tooltip("+1 if the raw sprite art faces right at scale.x = +1; -1 if it faces left.")]
    private int spriteDefaultFacing = 1;

    // ---------------------------------------------------------------
    // LAYERS
    // ---------------------------------------------------------------
    [Header("Layers")]
    [SerializeField, Tooltip("Obstacle / bounce layer (walls, floor, platforms).")]
    private LayerMask groundLayer;

    [SerializeField, Tooltip("Player layer used for detection and dive hit checks.")]
    private LayerMask playerLayer;

    // ---------------------------------------------------------------
    // ANIMATION HOOKS (optional)
    // ---------------------------------------------------------------
    [Header("Animation (optional)")]
    [SerializeField, Tooltip("Optional Animator. The script runs correctly with no Animator assigned.")]
    private Animator animator;

    // ---------------------------------------------------------------
    // GIZMO COLORS
    // ---------------------------------------------------------------
    [Header("Gizmo Colors")]
    [SerializeField] private Color gizmoHoverRadius     = new Color(0f,   1f,   1f,   0.9f);
    [SerializeField] private Color gizmoHomePoint       = new Color(0.2f, 1f,   0.2f, 1f);
    [SerializeField] private Color gizmoDetection       = new Color(1f,   0.9f, 0.2f, 0.9f);
    [SerializeField] private Color gizmoLeash           = new Color(1f,   0.2f, 1f,   0.7f);
    [SerializeField] private Color gizmoHop             = new Color(1f,   1f,   1f,   1f);
    [SerializeField] private Color gizmoDiveRange       = new Color(1f,   0.3f, 0.3f, 0.7f);
    [SerializeField] private Color gizmoShootRange      = new Color(0.4f, 0.7f, 1f,   0.7f);
    [SerializeField] private Color gizmoDiveTarget      = new Color(1f,   0f,   0f,   1f);
    [SerializeField] private Color gizmoDivePath        = new Color(1f,   0.4f, 0.4f, 1f);
    [SerializeField] private Color gizmoDiveHitboxIdle  = new Color(1f,   0.5f, 0f,   0.35f);
    [SerializeField] private Color gizmoDiveHitboxLive  = new Color(1f,   0.5f, 0f,   1f);
    [SerializeField] private Color gizmoProjectileSpawn = new Color(0.3f, 0.6f, 1f,   1f);
    [SerializeField] private Color gizmoAimLine         = new Color(1f,   0.6f, 0.6f, 1f);
    [SerializeField] private Color gizmoVerticalLimits  = new Color(0.5f, 0.8f, 1f,   0.6f);

    // ---------------------------------------------------------------
    // CACHED / RUNTIME STATE
    // ---------------------------------------------------------------
    private Rigidbody2D rb;
    private Collider2D  bodyCollider;

    private CrowState state = CrowState.Idle;
    public CrowState State => state;

    private int currentHealth;
    private int facingDirection = 1;
    private Transform target;
    private float lastSeenTime = -999f;
    private float nextAttackTime;

    private Vector2 homePoint;
    private Vector2 externalVelocity;   // decaying velocity from hops
    private Vector2 lastHopDir;
    private float   bobPhase;

    private AttackChoice lastAttack = AttackChoice.None;
    private int repeatCount;

    private bool diveHitboxActive;
    private Vector2 lockedDiveTarget;
    private Vector2 divePathStart;
    private Vector2 divePathEnd;
    private bool diveHitPlayerThisDive;

    private Color baseColor = Color.white;
    private Coroutine idleHopLoopCo;
    private Coroutine alertCo;
    private Coroutine diveCo;
    private Coroutine shootCo;
    private Coroutine retreatCo;
    private Coroutine staggerCo;
    private Coroutine deathCo;
    private Coroutine tintCo;
    private Coroutine flashCo;

    // ===============================================================
    // UNITY LIFECYCLE
    // ===============================================================
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null) baseColor = spriteRenderer.color;

        rb.gravityScale = 0f;
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        currentHealth = maxHealth;
        homePoint = transform.position;
        nextAttackTime = Time.time + Random.Range(0f, 0.3f);
        bobPhase = Random.value * Mathf.PI * 2f;
    }

    private void Start()
    {
        ChangeState(CrowState.Idle);
        // animator.SetBool("IsHovering", true);
    }

    private void Update()
    {
        if (state == CrowState.Dead) return;

        DetectPlayer();

        if (target != null && Time.time - lastSeenTime > losePlayerTime)
            target = null;

        UpdateFacing();

        if (state == CrowState.Idle && target != null && Time.time >= nextAttackTime)
            ChangeState(CrowState.Alert);
    }

    private void FixedUpdate()
    {
        if (state == CrowState.Dead) return;
        if (state == CrowState.Stunned) return;

        switch (state)
        {
            case CrowState.Idle:
            case CrowState.Alert:
            case CrowState.AttackShoot:
                TickHoverPhysics();
                break;
            // AttackDive / Retreat drive rb.velocity directly inside their coroutines.
        }
    }

    // ===============================================================
    // HOVER / BOB / HOP PHYSICS
    // ===============================================================
    private void TickHoverPhysics()
    {
        externalVelocity = Vector2.Lerp(externalVelocity, Vector2.zero, Mathf.Clamp01(hopEaseOut * Time.fixedDeltaTime));

        bobPhase += bobFrequency * Time.fixedDeltaTime * Mathf.PI * 2f;
        float bobVy = Mathf.Cos(bobPhase) * bobAmplitude * bobFrequency * Mathf.PI * 2f;

        Vector2 pos = rb.position;
        Vector2 toHome = homePoint - pos;
        float distFromHome = toHome.magnitude;
        Vector2 homePull = Vector2.zero;
        if (distFromHome > hoverRadius)
            homePull = toHome.normalized * returnForce * Mathf.Min(distFromHome - hoverRadius, 2f);

        // optional hard vertical clamp around home
        float dy = pos.y - homePoint.y;
        if (verticalLimits.x > 0f && dy < -verticalLimits.x) homePull.y += (-verticalLimits.x - dy) * returnForce;
        if (verticalLimits.y > 0f && dy >  verticalLimits.y) homePull.y -= (dy - verticalLimits.y) * returnForce;

        rb.linearVelocity = externalVelocity + homePull + new Vector2(0f, bobVy);
    }

    private IEnumerator IdleHopLoop()
    {
        while (state == CrowState.Idle || state == CrowState.Alert)
        {
            float wait = Random.Range(hopIntervalRange.x, hopIntervalRange.y);
            yield return new WaitForSeconds(wait);
            if (state != CrowState.Idle && state != CrowState.Alert) yield break;
            DoHop();
        }
    }

    private void DoHop()
    {
        Vector2 dir = PickHopDirection();
        if (dir == Vector2.zero) return;
        lastHopDir = dir;
        externalVelocity = dir * hopForce;
        // animator.SetTrigger("Hop");
    }

    private Vector2 PickHopDirection()
    {
        Vector2 pos = rb.position;
        Vector2 toHome = homePoint - pos;
        float distFromHome = toHome.magnitude;
        float bias = (distFromHome > hoverRadius) ? Mathf.Clamp01(distFromHome / Mathf.Max(hoverRadius, 0.01f)) * homeBias : 0f;
        Vector2 homeDir = (distFromHome > 0.001f) ? toHome / distFromHome : Vector2.zero;

        for (int i = 0; i < hopDirectionAttempts; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            Vector2 random = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            Vector2 dir = ((1f - bias) * random + bias * homeDir).normalized;

            RaycastHit2D hit = Physics2D.CircleCast(pos, hopClearanceRadius, dir, hopClearanceDistance, groundLayer);
            if (hit.collider == null) return dir;
        }
        return Vector2.zero;
    }

    // ===============================================================
    // DETECTION & FACING
    // ===============================================================
    private void DetectPlayer()
    {
        Collider2D hit = Physics2D.OverlapCircle(transform.position, detectionRange, playerLayer);
        if (hit == null) return;

        if (requireLineOfSight)
        {
            Vector2 from = transform.position;
            Vector2 to = hit.bounds.center;
            Vector2 delta = to - from;
            RaycastHit2D los = Physics2D.Raycast(from, delta.normalized, delta.magnitude, groundLayer);
            if (los.collider != null) return;
        }

        target = hit.transform;
        lastSeenTime = Time.time;
    }

    private void UpdateFacing()
    {
        int wanted = facingDirection;
        if (faceTowardsPlayer && target != null)
        {
            wanted = (target.position.x >= transform.position.x) ? 1 : -1;
        }
        else if (externalVelocity.sqrMagnitude > 0.05f)
        {
            wanted = (externalVelocity.x >= 0f) ? 1 : -1;
        }

        if (wanted != facingDirection)
        {
            facingDirection = wanted;
            Vector3 s = transform.localScale;
            s.x = Mathf.Abs(s.x) * facingDirection * spriteDefaultFacing;
            transform.localScale = s;
        }
    }

    // ===============================================================
    // STATE MACHINE
    // ===============================================================
    private void ChangeState(CrowState next)
    {
        if (state == next) return;
        if (state == CrowState.Dead) return;

        // stop coroutines belonging to outgoing state
        StopIfRunning(ref idleHopLoopCo);
        StopIfRunning(ref alertCo);
        StopIfRunning(ref diveCo);
        StopIfRunning(ref shootCo);
        StopIfRunning(ref retreatCo);

        diveHitboxActive = false;

        state = next;

        switch (next)
        {
            case CrowState.Idle:
                // animator.SetBool("IsHovering", true); animator.SetBool("IsAlert", false);
                idleHopLoopCo = StartCoroutine(IdleHopLoop());
                break;
            case CrowState.Alert:
                // animator.SetBool("IsAlert", true);
                alertCo = StartCoroutine(AlertRoutine());
                idleHopLoopCo = StartCoroutine(IdleHopLoop());
                break;
            case CrowState.AttackDive:
                // animator.SetTrigger("DiveWindup");
                diveCo = StartCoroutine(DiveRoutine());
                break;
            case CrowState.AttackShoot:
                // animator.SetTrigger("ShootWindup");
                shootCo = StartCoroutine(ShootRoutine());
                break;
            case CrowState.Retreat:
                // animator.SetTrigger("Retreat");
                retreatCo = StartCoroutine(RetreatRoutine());
                break;
            case CrowState.Stunned:
                // animator.SetBool("IsStunned", true);
                break;
            case CrowState.Dead:
                // animator.SetTrigger("Death");
                break;
        }
    }

    private void StopIfRunning(ref Coroutine c)
    {
        if (c != null) { StopCoroutine(c); c = null; }
    }

    // ===============================================================
    // ALERT
    // ===============================================================
    private IEnumerator AlertRoutine()
    {
        StartTint(alertColor, alertTime);
        // animator.SetTrigger("Alert");
        yield return new WaitForSeconds(alertTime);
        if (state != CrowState.Alert) yield break;

        if (target == null)
        {
            ChangeState(CrowState.Idle);
            yield break;
        }

        AttackChoice choice = ChooseAttack(Vector2.Distance(transform.position, target.position));
        ChangeState(choice == AttackChoice.Dive ? CrowState.AttackDive : CrowState.AttackShoot);
    }

    private AttackChoice ChooseAttack(float distance)
    {
        AttackChoice choice;
        bool canDive  = distance <= diveMaxDistance;
        bool canShoot = distance >= shootMinDistance;

        if (canDive && !canShoot)      choice = AttackChoice.Dive;
        else if (canShoot && !canDive) choice = AttackChoice.Shoot;
        else if (canDive && canShoot)
        {
            float total = Mathf.Max(0.0001f, diveChance + shootChance);
            choice = (Random.value < diveChance / total) ? AttackChoice.Dive : AttackChoice.Shoot;
        }
        else
        {
            // target outside both windows — default to the closer behaviour
            choice = (distance < (diveMaxDistance + shootMinDistance) * 0.5f) ? AttackChoice.Dive : AttackChoice.Shoot;
        }

        if (choice == lastAttack)
        {
            repeatCount++;
            if (repeatCount > maxRepeatAttacks)
            {
                choice = (choice == AttackChoice.Dive) ? AttackChoice.Shoot : AttackChoice.Dive;
                repeatCount = 1;
            }
        }
        else
        {
            repeatCount = 1;
        }
        lastAttack = choice;
        return choice;
    }

    // ===============================================================
    // DIVE
    // ===============================================================
    private IEnumerator DiveRoutine()
    {
        // windup
        StartTint(diveWindupColor, diveWindupTime);
        rb.linearVelocity = Vector2.zero;
        externalVelocity = Vector2.zero;

        float preLock = Mathf.Max(0f, diveWindupTime - lockOnDelay);
        if (preLock > 0f) yield return new WaitForSeconds(preLock);
        if (state != CrowState.AttackDive) yield break;

        // tiny pull-back before lock
        if (target != null)
        {
            Vector2 awayDir = ((Vector2)transform.position - (Vector2)target.position).normalized;
            externalVelocity = awayDir * hopForce * 0.5f;
        }

        if (lockOnDelay > 0f) yield return new WaitForSeconds(lockOnDelay);
        if (state != CrowState.AttackDive) yield break;

        if (target == null)
        {
            ChangeState(CrowState.Idle);
            yield break;
        }

        // lock target + clamp inside leash
        Vector2 start = transform.position;
        Vector2 locked = target.position;
        Vector2 fromHome = locked - homePoint;
        if (fromHome.magnitude > leashRadius)
            locked = homePoint + fromHome.normalized * leashRadius;

        Vector2 diveDir = (locked - start).normalized;
        if (diveDir.sqrMagnitude < 0.001f) diveDir = Vector2.right * facingDirection;
        Vector2 end = locked + diveDir * diveOvershoot;

        lockedDiveTarget = locked;
        divePathStart = start;
        divePathEnd = end;
        diveHitPlayerThisDive = false;
        diveHitboxActive = true;
        // animator.SetTrigger("DiveActive");

        float totalDist = Vector2.Distance(start, end);
        float travelTime = totalDist / Mathf.Max(0.01f, diveSpeed);
        Vector2 perp = new Vector2(-diveDir.y, diveDir.x);
        float t = 0f;
        Vector2 prev = start;

        while (t < travelTime)
        {
            if (state != CrowState.AttackDive) { diveHitboxActive = false; yield break; }
            t += Time.fixedDeltaTime;
            float u = Mathf.Clamp01(t / travelTime);
            float arc = Mathf.Sin(u * Mathf.PI) * diveCurvature * totalDist;
            Vector2 straight = Vector2.Lerp(start, end, u);
            Vector2 pos = straight + perp * arc;

            rb.linearVelocity = (pos - prev) / Time.fixedDeltaTime;
            prev = pos;

            // dive hitbox vs player (once per dive)
            if (!diveHitPlayerThisDive)
            {
                Vector2 hbWorld = (Vector2)transform.position
                    + new Vector2(diveHitboxOffset.x * facingDirection, diveHitboxOffset.y);
                Collider2D pHit = Physics2D.OverlapBox(hbWorld, diveHitboxSize, 0f, playerLayer);
                if (pHit != null)
                {
                    IDamageable dmg = pHit.GetComponentInParent<IDamageable>();
                    if (dmg != null)
                    {
                        Vector2 knockDir = diveDir;
                        dmg.TakeDamage(diveDamage, knockDir * diveKnockback);
                        diveHitPlayerThisDive = true;
                    }
                }
            }

            // wall abort
            Collider2D wall = Physics2D.OverlapBox(transform.position, bodyCollider.bounds.size * 0.95f, 0f, groundLayer);
            if (wall != null)
            {
                diveHitboxActive = false;
                if (bounceOffWallOnDive)
                {
                    externalVelocity = -diveDir * diveBounceForce;
                    ChangeState(CrowState.Retreat);
                }
                else
                {
                    rb.linearVelocity = Vector2.zero;
                    yield return StartCoroutine(WallStunRoutine());
                }
                yield break;
            }

            yield return new WaitForFixedUpdate();
        }

        diveHitboxActive = false;
        rb.linearVelocity = Vector2.zero;
        ChangeState(CrowState.Retreat);
    }

    private IEnumerator WallStunRoutine()
    {
        state = CrowState.Stunned;
        // animator.SetBool("IsStunned", true);
        yield return new WaitForSeconds(diveWallStunTime);
        // animator.SetBool("IsStunned", false);
        if (state == CrowState.Stunned)
            ChangeState(CrowState.Idle);
    }

    // ===============================================================
    // RETREAT
    // ===============================================================
    private IEnumerator RetreatRoutine()
    {
        float safety = 3f; // max retreat time
        float t = 0f;
        while (t < safety)
        {
            if (state != CrowState.Retreat) yield break;
            t += Time.fixedDeltaTime;
            Vector2 toHome = homePoint - rb.position;
            if (toHome.magnitude < 0.25f) break;
            rb.linearVelocity = toHome.normalized * retreatSpeed;
            yield return new WaitForFixedUpdate();
        }
        rb.linearVelocity = Vector2.zero;

        // vulnerable window
        yield return new WaitForSeconds(diveRecoveryTime);

        nextAttackTime = Time.time + attackCooldown;
        ChangeState(CrowState.Idle);
    }

    // ===============================================================
    // SHOOT
    // ===============================================================
    private IEnumerator ShootRoutine()
    {
        rb.linearVelocity = Vector2.zero;
        externalVelocity = Vector2.zero;

        if (target != null)
        {
            facingDirection = (target.position.x >= transform.position.x) ? 1 : -1;
            Vector3 s = transform.localScale;
            s.x = Mathf.Abs(s.x) * facingDirection * spriteDefaultFacing;
            transform.localScale = s;
        }

        StartTint(shootWindupColor, shootWindupTime);
        // animator.SetTrigger("ShootWindup");
        yield return new WaitForSeconds(shootWindupTime);
        if (state != CrowState.AttackShoot) yield break;

        FireProjectiles();
        // animator.SetTrigger("Shoot");

        yield return new WaitForSeconds(shootRecoveryTime);
        nextAttackTime = Time.time + attackCooldown;
        ChangeState(CrowState.Idle);
    }

    private void FireProjectiles()
    {
        if (projectilePrefab == null || target == null) return;

        Vector2 spawn = (Vector2)transform.position
            + new Vector2(projectileSpawnOffset.x * facingDirection, projectileSpawnOffset.y);

        Vector2 aim = ((Vector2)target.position - spawn).normalized;
        if (aim.sqrMagnitude < 0.001f) aim = Vector2.right * facingDirection;

        int n = Mathf.Max(1, projectilesPerShot);
        for (int i = 0; i < n; i++)
        {
            float offset = 0f;
            if (n > 1 && spreadAngle > 0f)
                offset = -spreadAngle * 0.5f + spreadAngle * (i / (float)(n - 1));

            Vector2 dir = Rotate(aim, offset);
            CrowProjectile.Spawn(projectilePrefab, spawn, dir * projectileSpeed);
        }
    }

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float r = degrees * Mathf.Deg2Rad;
        float c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    // ===============================================================
    // DAMAGE / STAGGER / DEATH
    // ===============================================================
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (state == CrowState.Dead || contactDamage <= 0) return;
        if (!collision.gameObject.CompareTag("Player")) return;

        PlayerHealth player = collision.gameObject.GetComponentInParent<PlayerHealth>();
        if (player == null || player.IsInvulnerable) return;

        Vector2 away = (Vector2)player.transform.position - (Vector2)transform.position;
        if (away.sqrMagnitude < 0.001f) away = Vector2.down;
        player.TakeDamage(contactDamage, away.normalized + Vector2.up * 0.25f);
    }

    public void TakeDamage(int amount, Vector2 hitDirection)
    {
        if (state == CrowState.Dead) return;

        currentHealth -= Mathf.Max(0, amount);
        // animator.SetTrigger("Hurt");

        if (flashCo != null) StopCoroutine(flashCo);
        flashCo = StartCoroutine(HitFlashRoutine());

        if (currentHealth <= 0)
        {
            StartDeath();
            return;
        }

        if (canBeStaggered)
        {
            if (staggerCo != null) StopCoroutine(staggerCo);
            staggerCo = StartCoroutine(StaggerRoutine(hitDirection));
        }
        else
        {
            // soft knock without state change
            externalVelocity = hitDirection.sqrMagnitude > 0.001f
                ? hitDirection.normalized * hitKnockback * 0.5f
                : externalVelocity;
        }
    }

    private IEnumerator HitFlashRoutine()
    {
        if (spriteRenderer == null) yield break;
        Color saved = spriteRenderer.color;
        spriteRenderer.color = hitFlashColor;
        yield return new WaitForSeconds(hitFlashDuration);
        spriteRenderer.color = baseColor;
        // if a tint coroutine is running, it will restore its own phase next frame
    }

    private IEnumerator StaggerRoutine(Vector2 hitDirection)
    {
        // tear down any attack coroutines
        StopIfRunning(ref alertCo);
        StopIfRunning(ref diveCo);
        StopIfRunning(ref shootCo);
        StopIfRunning(ref retreatCo);
        diveHitboxActive = false;

        state = CrowState.Stunned;
        // animator.SetBool("IsStunned", true);

        Vector2 knockDir = hitDirection.sqrMagnitude > 0.001f ? hitDirection.normalized : Vector2.up;
        externalVelocity = knockDir * hitKnockback;
        rb.linearVelocity = externalVelocity;

        float t = 0f;
        while (t < staggerDuration)
        {
            t += Time.deltaTime;
            externalVelocity = Vector2.Lerp(externalVelocity, Vector2.zero, Mathf.Clamp01(hopEaseOut * Time.deltaTime));
            rb.linearVelocity = externalVelocity;
            yield return null;
        }

        rb.linearVelocity = Vector2.zero;
        // animator.SetBool("IsStunned", false);
        if (state == CrowState.Stunned)
            ChangeState(CrowState.Idle);
    }

    private void StartDeath()
    {
        if (deathCo != null) StopCoroutine(deathCo);
        deathCo = StartCoroutine(DeathRoutine());
    }

    private IEnumerator DeathRoutine()
    {
        StopIfRunning(ref idleHopLoopCo);
        StopIfRunning(ref alertCo);
        StopIfRunning(ref diveCo);
        StopIfRunning(ref shootCo);
        StopIfRunning(ref retreatCo);
        StopIfRunning(ref staggerCo);
        StopIfRunning(ref tintCo);

        state = CrowState.Dead;
        diveHitboxActive = false;
        // animator.SetTrigger("Death");

        if (bodyCollider != null) bodyCollider.enabled = false;

        if (fallOnDeath)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = deathGravityScale;
            rb.linearVelocity = new Vector2(Random.Range(-1f, 1f) * 2f, 2f);
        }
        else
        {
            rb.linearVelocity = Vector2.zero;
        }

        float t = 0f;
        Color start = spriteRenderer != null ? spriteRenderer.color : Color.white;
        while (t < deathDuration)
        {
            t += Time.deltaTime;
            if (spriteRenderer != null)
            {
                float a = Mathf.Lerp(start.a, 0f, t / deathDuration);
                Color c = spriteRenderer.color;
                c.a = a;
                spriteRenderer.color = c;
            }
            yield return null;
        }

        if (destroyOnDeath) Destroy(gameObject);
        else gameObject.SetActive(false);
    }

    // ===============================================================
    // TINT HELPER (non-destructive to hit flash)
    // ===============================================================
    private void StartTint(Color tint, float duration)
    {
        if (spriteRenderer == null) return;
        if (tintCo != null) StopCoroutine(tintCo);
        tintCo = StartCoroutine(TintRoutine(tint, duration));
    }

    private IEnumerator TintRoutine(Color tint, float duration)
    {
        if (spriteRenderer == null) yield break;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            // skip overwriting while a hit flash is briefly active
            if (spriteRenderer.color != hitFlashColor)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 20f);
                spriteRenderer.color = Color.Lerp(baseColor, tint, pulse);
            }
            yield return null;
        }
        if (spriteRenderer.color != hitFlashColor)
            spriteRenderer.color = baseColor;
    }

    // ===============================================================
    // GIZMOS
    // ===============================================================
    private Vector2 GizmoHome => Application.isPlaying ? homePoint : (Vector2)transform.position;

    private void OnDrawGizmos()
    {
        Vector3 home = GizmoHome;

        // home point
        Gizmos.color = gizmoHomePoint;
        Gizmos.DrawWireSphere(home, 0.12f);
        Gizmos.DrawLine(home + Vector3.left  * 0.2f, home + Vector3.right * 0.2f);
        Gizmos.DrawLine(home + Vector3.down  * 0.2f, home + Vector3.up    * 0.2f);

        // hover radius
        Gizmos.color = gizmoHoverRadius;
        Gizmos.DrawWireSphere(home, hoverRadius);

        // vertical limits
        if (verticalLimits.x > 0f || verticalLimits.y > 0f)
        {
            Gizmos.color = gizmoVerticalLimits;
            float w = Mathf.Max(hoverRadius, 0.5f);
            if (verticalLimits.x > 0f) Gizmos.DrawLine(home + new Vector3(-w, -verticalLimits.x, 0f), home + new Vector3(w, -verticalLimits.x, 0f));
            if (verticalLimits.y > 0f) Gizmos.DrawLine(home + new Vector3(-w,  verticalLimits.y, 0f), home + new Vector3(w,  verticalLimits.y, 0f));
        }

        // leash
        Gizmos.color = gizmoLeash;
        Gizmos.DrawWireSphere(home, leashRadius);

        // detection
        Gizmos.color = gizmoDetection;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        // dive hitbox (idle color)
        int face = Application.isPlaying ? facingDirection : 1;
        Vector3 hbWorld = transform.position + new Vector3(diveHitboxOffset.x * face, diveHitboxOffset.y, 0f);
        Gizmos.color = diveHitboxActive ? gizmoDiveHitboxLive : gizmoDiveHitboxIdle;
        Gizmos.DrawWireCube(hbWorld, diveHitboxSize);

        // projectile spawn + edit-mode dive preview
        Vector3 spawn = transform.position + new Vector3(projectileSpawnOffset.x * face, projectileSpawnOffset.y, 0f);
        Gizmos.color = gizmoProjectileSpawn;
        Gizmos.DrawWireSphere(spawn, 0.08f);

        if (!Application.isPlaying)
        {
            // dive preview toward where the body is facing
            Vector3 d = Vector3.right * face;
            Gizmos.color = gizmoDivePath;
            Gizmos.DrawLine(transform.position, transform.position + d * (diveMaxDistance + diveOvershoot));
        }

        // runtime extras
        if (Application.isPlaying)
        {
            if (lastHopDir.sqrMagnitude > 0.001f)
            {
                Gizmos.color = gizmoHop;
                Gizmos.DrawLine(transform.position, (Vector2)transform.position + lastHopDir * 0.6f);
            }
            if (state == CrowState.AttackDive)
            {
                Gizmos.color = gizmoDivePath;
                Gizmos.DrawLine(divePathStart, divePathEnd);
                Gizmos.color = gizmoDiveTarget;
                Gizmos.DrawWireSphere(lockedDiveTarget, 0.15f);
            }
            if (target != null)
            {
                Gizmos.color = gizmoAimLine;
                Gizmos.DrawLine(spawn, target.position);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 home = GizmoHome;

        // highlight attack-range rings (selected-only so the scene isn't noisy)
        Gizmos.color = gizmoDiveRange;
        Gizmos.DrawWireSphere(transform.position, diveMaxDistance);
        Gizmos.color = gizmoShootRange;
        Gizmos.DrawWireSphere(transform.position, shootMinDistance);
    }

    // ===============================================================
    // ANIMATOR PARAMETERS (reference list)
    // ---------------------------------------------------------------
    // Bools:
    //   IsHovering      : true while in Idle / Alert hover
    //   IsAlert         : true during the alert reaction
    //   IsStunned       : true while stunned (stagger or dive wall stun)
    // Triggers:
    //   Alert           : one-shot pop on first detection
    //   Hop             : fired each flap-hop burst
    //   DiveWindup      : start of the dive telegraph
    //   DiveActive      : the swoop itself
    //   Retreat         : flap back to home after a dive
    //   ShootWindup     : start of the shoot telegraph
    //   Shoot           : projectile release
    //   Hurt            : fired on every damage event
    //   Death           : fired once when health hits 0
    // Floats (optional, for blend trees):
    //   HorizontalSpeed : signed horizontal velocity
    //   VerticalSpeed   : signed vertical velocity
    // ===============================================================
}
