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
    public enum CrowState { Idle, Alert, Pursue, AttackDive, AttackShoot, Stunned, Dead }
    private enum AttackChoice { None, Swoop, Ricochet }

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
    // MOVEMENT — AIRBORNE IDLE
    // ---------------------------------------------------------------
    [Header("Airborne Idle")]
    [SerializeField, Min(0.1f), Tooltip("Maximum vertical drift above or below the crow's starting height while idling.")]
    private float idleVerticalRange = 0.75f;

    [SerializeField, Min(0f), Tooltip("How strongly idle flight steers back toward its bounded vertical band.")]
    private float idleVerticalCorrection = 2f;

    [SerializeField, Min(0f), Tooltip("Vertical amplitude of the constant idle bob (world units).")]
    private float bobAmplitude = 0.15f;

    [SerializeField, Min(0f), Tooltip("Bob oscillations per second.")]
    private float bobFrequency = 1.6f;

    // ---------------------------------------------------------------
    // MOVEMENT — HOP (the twitchy flap bursts)
    // ---------------------------------------------------------------
    [Header("Flap Hop")]
    [SerializeField, Tooltip("Min/max seconds between random flap hops while idling/chasing.")]
    private Vector2 hopIntervalRange = new Vector2(0.4f, 1.1f);

    [SerializeField, Min(0f), Tooltip("Initial velocity of each flap hop.")]
    private float hopForce = 7f;

    [SerializeField, Range(0f, 20f), Tooltip("Higher = faster ease-out after a hop (floaty glide to stop).")]
    private float hopEaseOut = 4.5f;

    // ---------------------------------------------------------------
    // DETECTION
    // ---------------------------------------------------------------
    [Header("Detection")]
    [SerializeField, Min(0f), Tooltip("Player detection radius around the crow.")]
    private float detectionRange = 7f;

    [SerializeField, Tooltip("If true, requires a clear line of sight (no ground in the way) to detect the player.")]
    private bool requireLineOfSight = true;

    [SerializeField, Min(0f), Tooltip("Seconds without a sighting before the crow forgets the player.")]
    private float losePlayerTime = 4f;

    [SerializeField, Min(0f), Tooltip("Seconds of 'alert' reaction before the first attack.")]
    private float alertTime = 0.35f;

    [SerializeField, Tooltip("Tint flashed while in the alert reaction.")]
    private Color alertColor = new Color(1f, 0.85f, 0.3f);

    // ---------------------------------------------------------------
    // ACTION CHOICE AND BETWEEN-ACTION IDLE
    // ---------------------------------------------------------------
    [Header("Attack Choice and Idle Wait")]
    [SerializeField, Min(0f), Tooltip("Minimum wait between attack rolls while the crow flaps in the air.")]
    private float actionWaitMin = 0.25f;
    [SerializeField, Min(0f), Tooltip("Maximum wait between attack rolls while the crow flaps in the air.")]
    private float actionWaitMax = 1.5f;
    [SerializeField, Range(0f, 1f), Tooltip("Chance to choose the ricochet projectile instead of a swoop.")]
    private float ricochetChoiceChance = 0.35f;
    [SerializeField, Min(1), Tooltip("Maximum consecutive times the crow can choose the same action.")]
    private int maxRepeatedAction = 3;

    [SerializeField, Min(0f), Tooltip("Maximum distance the crow will pursue the player before giving up.")]
    private float giveUpDistance = 10f;
    [SerializeField, Min(0f), Tooltip("Maximum horizontal distance for one swoop attack.")]
    private float swoopMaxDistance = 7f;

    [Header("Airborne Pursuit")]
    [SerializeField, Min(0f), Tooltip("Preferred horizontal spacing from the player while airborne.")]
    private float preferredPlayerDistance = 3.5f;
    [SerializeField, Min(0.1f), Tooltip("Speed used to flap into a swoop position.")]
    private float pursuitSpeed = 4f;
    [SerializeField, Min(0f), Tooltip("Amplitude of the short vertical flaps during pursuit.")]
    private float pursuitFlapAmplitude = 0.3f;
    [SerializeField, Min(0f), Tooltip("Frequency of the short vertical flaps during pursuit.")]
    private float pursuitFlapFrequency = 5f;
    [SerializeField, Min(0f), Tooltip("Maximum vertical difference at which the Craw commits to a level sweep.")]
    private float verticalAlignment = 0.5f;
    [SerializeField, Min(0f), Tooltip("Minimum time between completed swoop attacks.")]
    private float swoopCooldown = 1.2f;

    // ---------------------------------------------------------------
    // ATTACK A — DIVE
    // ---------------------------------------------------------------
    [Header("Attack A — Dive")]
    [SerializeField, Min(0f), Tooltip("Telegraph windup seconds before the dive fires.")]
    private float diveWindupTime = 0.25f;

    [SerializeField, Tooltip("Tint flashed during the dive windup.")]
    private Color diveWindupColor = new Color(1f, 0.4f, 0.4f);

    [SerializeField, Min(0.1f), Tooltip("Horizontal sweep speed.")]
    private float diveSpeed = 5f;

    [SerializeField, Min(0f), Tooltip("Extra distance past the locked target before the dive ends.")]
    private float diveOvershoot = 1.5f;

    [SerializeField, Min(0), Tooltip("Damage dealt when the crow collides with the player during a swoop.")]
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
    [SerializeField] private Color gizmoDetection       = new Color(1f,   0.9f, 0.2f, 0.9f);
    [SerializeField] private Color gizmoHop             = new Color(1f,   1f,   1f,   1f);
    [SerializeField] private Color gizmoDiveTarget      = new Color(1f,   0f,   0f,   1f);
    [SerializeField] private Color gizmoDivePath        = new Color(1f,   0.4f, 0.4f, 1f);
    [SerializeField] private Color gizmoProjectileSpawn = new Color(0.3f, 0.6f, 1f,   1f);
    [SerializeField] private Color gizmoAimLine         = new Color(1f,   0.6f, 0.6f, 1f);

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
    private float nextSwoopTime;
    private AttackChoice lastChosenAction = AttackChoice.None;
    private int repeatedActionCount;
    private Coroutine waitActionCo;
    // Crow tuning values are authored at scale 1. Keep their world-space behavior
    // proportional when the scene instance is resized (for example to 0.4).
    private float BehaviorScale => Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
    private float Scaled(float value) => value * BehaviorScale;
    private Vector2 Scaled(Vector2 value) => Vector2.Scale(value, new Vector2(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y)));
    private Vector2 externalVelocity;   // decaying velocity from hops
    private Vector2 lastHopDir;
    private float   bobPhase;
    private bool swoopCollisionActive;
    private Vector2 lockedDiveTarget;
    private Vector2 swoopCommitTarget;
    private Vector2 divePathStart;
    private Vector2 divePathEnd;
    private bool diveHitPlayerThisDive;

    private Color baseColor = Color.white;
    private Coroutine alertCo;
    private Coroutine diveCo;
    private Coroutine shootCo;
    private Coroutine pursuitCo;
    private Vector2 idleVelocity;
    private float idleHopTimer;
    private float idleCenterY;
    private int committedSwoopDirection = 1;
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
        rb.useFullKinematicContacts = true;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        currentHealth = maxHealth;
        idleCenterY = rb.position.y;
        idleHopTimer = Random.Range(hopIntervalRange.x, hopIntervalRange.y);
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

        if (target != null && Vector2.Distance(transform.position, target.position) > giveUpDistance)
        {
            target = null;
            if (state == CrowState.Idle || state == CrowState.Alert || state == CrowState.Pursue)
                ChangeState(CrowState.Idle);
        }

        UpdateFacing();

        if (state == CrowState.Idle && target != null && waitActionCo == null)
            waitActionCo = StartCoroutine(ChooseNextActionAfterWait());
    }

    private void FixedUpdate()
    {
        if (state == CrowState.Dead) return;
        if (state == CrowState.Stunned) return;

        switch (state)
        {
            case CrowState.Idle:
                TickIdleFlight();
                break;
            // Active states drive movement directly inside their coroutines.
        }
    }

    // ===============================================================
    // HOVER / BOB / HOP PHYSICS
    // ===============================================================
    private void TickIdleFlight()
    {
        float bobVy = StepFlapVelocity();
        float upperY = idleCenterY + Scaled(idleVerticalRange);
        float lowerY = idleCenterY - Scaled(idleVerticalRange);
        float y = rb.position.y;
        float verticalCorrection = 0f;
        if (y > upperY)
            verticalCorrection = -Mathf.Min(Scaled(pursuitSpeed), (y - upperY) * Scaled(idleVerticalCorrection));
        else if (y < lowerY)
            verticalCorrection = Mathf.Min(Scaled(pursuitSpeed), (lowerY - y) * Scaled(idleVerticalCorrection));
        idleHopTimer -= Time.fixedDeltaTime;
        if (idleHopTimer <= 0f)
        {
            idleHopTimer = Random.Range(hopIntervalRange.x, hopIntervalRange.y);
            float side = Random.Range(-1f, 1f);
            float forwardBias = target != null && Mathf.Abs(target.position.x - transform.position.x) > Scaled(1.5f)
                ? Mathf.Sign(target.position.x - transform.position.x) * 0.35f
                : 0f;
            idleVelocity = new Vector2(side + forwardBias, Random.Range(-0.35f, 0.35f)).normalized * Scaled(hopForce) * 0.2f;
        }

        idleVelocity = Vector2.Lerp(idleVelocity, Vector2.zero, hopEaseOut * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(idleVelocity.x, idleVelocity.y + bobVy + verticalCorrection);
    }

    private float StepFlapVelocity()
    {
        bobPhase += bobFrequency * Time.fixedDeltaTime * Mathf.PI * 2f;
        return Mathf.Cos(bobPhase) * Scaled(bobAmplitude) * bobFrequency * Mathf.PI * 2f;
    }

    private IEnumerator FlappyPause(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float bobVy = StepFlapVelocity();
            float horizontalFlutter = Mathf.Sin(Time.time * bobFrequency * Mathf.PI * 2f) * Scaled(0.12f);
            rb.linearVelocity = new Vector2(horizontalFlutter, bobVy);
            yield return new WaitForFixedUpdate();
            elapsed += Time.fixedDeltaTime;
        }
    }

    // ===============================================================
    // DETECTION & FACING
    // ===============================================================
    private void DetectPlayer()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, detectionRange, playerLayer);
        Collider2D hit = null;
        PlayerHealth hitHealth = null;
        float nearestSqrDistance = float.PositiveInfinity;
        foreach (Collider2D candidate in hits)
        {
            if (candidate == null) continue;
            Vector2 delta = (Vector2)candidate.bounds.center - (Vector2)transform.position;
            float sqrDistance = delta.sqrMagnitude;
            if (sqrDistance >= nearestSqrDistance) continue;

            if (requireLineOfSight)
            {
                RaycastHit2D los = Physics2D.Raycast(transform.position, delta.normalized, delta.magnitude, groundLayer);
                if (los.collider != null) continue;
            }

            hit = candidate;
            hitHealth = candidate.GetComponentInParent<PlayerHealth>();
            nearestSqrDistance = sqrDistance;
        }
        if (hit == null)
        {
            if (target != null && Time.time - lastSeenTime > losePlayerTime)
                target = null;
            return;
        }

        target = hitHealth != null ? hitHealth.transform : hit.transform;
        lastSeenTime = Time.time;
    }

    private void UpdateFacing()
    {
        int wanted = facingDirection;
        if (state == CrowState.AttackDive)
        {
            wanted = committedSwoopDirection;
        }
        else if (faceTowardsPlayer && target != null)
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
        StopIfRunning(ref alertCo);
        StopIfRunning(ref diveCo);
        StopIfRunning(ref shootCo);
        StopIfRunning(ref pursuitCo);
        StopIfRunning(ref waitActionCo);

        swoopCollisionActive = false;

        state = next;
        if (next == CrowState.Idle)
        {
            idleVelocity = Vector2.zero;
            idleHopTimer = Random.Range(hopIntervalRange.x, hopIntervalRange.y);
            rb.linearVelocity = Vector2.zero;
        }
        switch (next)
        {
            case CrowState.Idle:
                // animator.SetBool("IsHovering", true); animator.SetBool("IsAlert", false);
                break;
            case CrowState.Alert:
                // animator.SetBool("IsAlert", true);
                alertCo = StartCoroutine(AlertRoutine());
                rb.linearVelocity = Vector2.zero;
                break;
            case CrowState.Pursue:
                pursuitCo = StartCoroutine(PursuitRoutine());
                break;
            case CrowState.AttackDive:
                // animator.SetTrigger("DiveWindup");
                diveCo = StartCoroutine(DiveRoutine());
                break;
            case CrowState.AttackShoot:
                // animator.SetTrigger("ShootWindup");
                shootCo = StartCoroutine(ShootRoutine());
                break;
            case CrowState.Stunned:
                // animator.SetBool("IsStunned", true);
                break;
            case CrowState.Dead:
                // animator.SetTrigger("Death");
                break;
        }

        if (next == CrowState.Idle && target != null && waitActionCo == null)
            waitActionCo = StartCoroutine(ChooseNextActionAfterWait());
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

        if (Vector2.Distance(transform.position, target.position) > giveUpDistance)
        {
            target = null;
            ChangeState(CrowState.Idle);
            yield break;
        }

        ChangeState(CrowState.Idle);
    }

    private IEnumerator ChooseNextActionAfterWait()
    {
        if (target == null) yield break;
        float minWait = Mathf.Min(actionWaitMin, actionWaitMax);
        float maxWait = Mathf.Max(actionWaitMin, actionWaitMax);
        float wait = Random.Range(minWait, maxWait);
        float elapsed = 0f;
        while (elapsed < wait && state == CrowState.Idle && target != null)
        {
            yield return null;
            elapsed += Time.deltaTime;
        }
        if (state != CrowState.Idle || target == null) yield break;
        AttackChoice choice = ChooseNextAction();
        if (choice == AttackChoice.Ricochet)
            ChangeState(CrowState.AttackShoot);
        else
        {
            Debug.Log($"{name}: swoop selected; pursuing player before horizontal sweep.", this);
            ChangeState(CrowState.Pursue);
        }
    }

    private AttackChoice ChooseNextAction()
    {
        AttackChoice choice = projectilePrefab != null
            && Random.value < ricochetChoiceChance
                ? AttackChoice.Ricochet
                : AttackChoice.Swoop;

        if (choice == lastChosenAction)
            repeatedActionCount++;
        else
        {
            lastChosenAction = choice;
            repeatedActionCount = 1;
        }

        if (repeatedActionCount > Mathf.Max(1, maxRepeatedAction))
        {
            choice = choice == AttackChoice.Ricochet ? AttackChoice.Swoop : AttackChoice.Ricochet;
            if (choice == AttackChoice.Ricochet && projectilePrefab == null)
                choice = AttackChoice.Swoop;
            lastChosenAction = choice;
            repeatedActionCount = 1;
        }
        return choice;
    }

    private IEnumerator PursuitRoutine()
    {
        while (state == CrowState.Pursue && target != null)
        {
            Vector2 player = target.position;
            float side = Mathf.Sign(rb.position.x - player.x);
            if (side == 0f) side = facingDirection == 0 ? 1f : -facingDirection;

            // Stay at the preferred distance on the side the Craw already occupies,
            // with a continuous short-arc flap around the player's height.
            Vector2 pursuitPosition = new Vector2(
                player.x + side * preferredPlayerDistance,
                player.y + Mathf.Sin(Time.time * pursuitFlapFrequency) * pursuitFlapAmplitude);
            Vector2 nextPosition = Vector2.MoveTowards(
                rb.position,
                pursuitPosition,
                pursuitSpeed * Time.fixedDeltaTime);
            rb.MovePosition(nextPosition);

            float horizontalDistance = Mathf.Abs(rb.position.x - player.x);
            bool verticallyAligned = Mathf.Abs(rb.position.y - player.y) <= verticalAlignment;
            bool atAttackDistance = horizontalDistance <= preferredPlayerDistance + 0.5f;

            if (verticallyAligned && atAttackDistance && Time.time >= nextSwoopTime)
            {
                swoopCommitTarget = player;
                committedSwoopDirection = player.x == rb.position.x
                    ? facingDirection
                    : (player.x > rb.position.x ? 1 : -1);
                Debug.Log($"{name}: performing committed horizontal swoop toward {swoopCommitTarget}.", this);
                ChangeState(CrowState.AttackDive);
                yield break;
            }

            yield return new WaitForFixedUpdate();
        }

        if (state != CrowState.Pursue) yield break;
        if (target == null)
        {
            target = null;
            ChangeState(CrowState.Idle);
            yield break;
        }

    }

    // ===============================================================
    // DIVE
    // ===============================================================
    private IEnumerator DiveRoutine()
    {
        if (target == null)
        {
            ChangeState(CrowState.Idle);
            yield break;
        }

        Vector2 locked = swoopCommitTarget;
        StartTint(diveWindupColor, diveWindupTime);
        externalVelocity = Vector2.zero;
        yield return StartCoroutine(FlappyPause(diveWindupTime));
        if (state != CrowState.AttackDive) yield break;

        // Sweep through the committed target's horizontal lane. Its position is
        // never sampled again during windup or flight.
        Vector2 start = transform.position;
        float direction = committedSwoopDirection;
        Vector2 diveDir = Vector2.right * direction;
        Vector2 end = new Vector2(locked.x + direction * diveOvershoot, start.y);

        lockedDiveTarget = locked;
        divePathStart = start;
        divePathEnd = end;
        diveHitPlayerThisDive = false;
        swoopCollisionActive = true;
        // animator.SetTrigger("DiveActive");

        while (Vector2.Distance(rb.position, end) > 0.05f)
        {
            if (state != CrowState.AttackDive) { swoopCollisionActive = false; yield break; }
            Vector2 nextPosition = Vector2.MoveTowards(rb.position, end, diveSpeed * Time.fixedDeltaTime);
            rb.MovePosition(nextPosition);

            // wall abort
            Collider2D wall = Physics2D.OverlapBox(transform.position, bodyCollider.bounds.size * 0.95f, 0f, groundLayer);
            if (wall != null)
            {
                swoopCollisionActive = false;
                if (bounceOffWallOnDive)
                {
                    externalVelocity = -diveDir * diveBounceForce;
                    ChangeState(CrowState.Idle);
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

        swoopCollisionActive = false;
        rb.linearVelocity = Vector2.zero;
        nextSwoopTime = Time.time + swoopCooldown;
        ChangeState(CrowState.Idle);
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
    // ===============================================================
    // SHOOT
    // ===============================================================
    private IEnumerator ShootRoutine()
    {
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
        yield return StartCoroutine(FlappyPause(shootWindupTime));
        if (state != CrowState.AttackShoot) yield break;

        FireProjectiles();
        // animator.SetTrigger("Shoot");

        yield return StartCoroutine(FlappyPause(shootRecoveryTime));
        ChangeState(CrowState.Idle);
    }

    private void FireProjectiles()
    {
        if (projectilePrefab == null || target == null) return;

        Vector2 scaledSpawnOffset = Scaled(projectileSpawnOffset);
        Vector2 spawn = (Vector2)transform.position
            + new Vector2(scaledSpawnOffset.x * facingDirection, scaledSpawnOffset.y);

        Vector2 aim = ((Vector2)target.position - spawn).normalized;
        if (aim.sqrMagnitude < 0.001f) aim = Vector2.right * facingDirection;

        int n = Mathf.Max(1, projectilesPerShot);
        for (int i = 0; i < n; i++)
        {
            float offset = 0f;
            if (n > 1 && spreadAngle > 0f)
                offset = -spreadAngle * 0.5f + spreadAngle * (i / (float)(n - 1));

            Vector2 dir = Rotate(aim, offset);
            CrowProjectile.Spawn(projectilePrefab, spawn, dir * Scaled(projectileSpeed));
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
        if (state == CrowState.Dead) return;
        if (!collision.gameObject.CompareTag("Player")) return;

        PlayerHealth player = collision.gameObject.GetComponentInParent<PlayerHealth>();
        if (player == null || player.IsInvulnerable) return;

        bool swoopHit = state == CrowState.AttackDive && swoopCollisionActive;
        if (swoopHit && diveHitPlayerThisDive) return;

        int damage = swoopHit ? diveDamage : contactDamage;
        if (damage <= 0) return;

        Vector2 away = (Vector2)player.transform.position - (Vector2)transform.position;
        if (away.sqrMagnitude < 0.001f) away = Vector2.down;
        Vector2 hitDirection = swoopHit
            ? new Vector2(facingDirection, 0.25f).normalized * diveKnockback
            : away.normalized + Vector2.up * 0.25f;

        if (swoopHit)
        {
            diveHitPlayerThisDive = true;
            Debug.Log($"{name}: swoop collision hit Player; PlayerHealth will apply damage and i-frames.", this);
        }
        else
        {
            Debug.Log($"{name}: contact collision hit Player; PlayerHealth will apply damage and i-frames.", this);
        }
        player.TakeDamage(damage, hitDirection);
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
        swoopCollisionActive = false;

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
        StopIfRunning(ref alertCo);
        StopIfRunning(ref diveCo);
        StopIfRunning(ref shootCo);
        StopIfRunning(ref staggerCo);
        StopIfRunning(ref tintCo);

        state = CrowState.Dead;
        swoopCollisionActive = false;
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
    private void OnDrawGizmos()
    {
        // detection
        Gizmos.color = gizmoDetection;
        Gizmos.DrawWireSphere(transform.position, Scaled(detectionRange));

        // projectile spawn + edit-mode dive preview
        int face = Application.isPlaying ? facingDirection : 1;
        Vector2 scaledSpawnOffset = Scaled(projectileSpawnOffset);
        Vector3 spawn = transform.position + new Vector3(scaledSpawnOffset.x * face, scaledSpawnOffset.y, 0f);
        Gizmos.color = gizmoProjectileSpawn;
        Gizmos.DrawWireSphere(spawn, Scaled(0.08f));

        if (!Application.isPlaying)
        {
            // dive preview toward where the body is facing
            Vector3 d = Vector3.right * face;
            Gizmos.color = gizmoDivePath;
            Gizmos.DrawLine(transform.position, transform.position + d * (Scaled(swoopMaxDistance) + Scaled(diveOvershoot)));
        }

        // runtime extras
        if (Application.isPlaying)
        {
            if (lastHopDir.sqrMagnitude > 0.001f)
            {
                Gizmos.color = gizmoHop;
                Gizmos.DrawLine(transform.position, (Vector2)transform.position + lastHopDir * Scaled(0.6f));
            }
            if (state == CrowState.AttackDive)
            {
                Gizmos.color = gizmoDivePath;
                Gizmos.DrawLine(divePathStart, divePathEnd);
                Gizmos.color = gizmoDiveTarget;
                Gizmos.DrawWireSphere(lockedDiveTarget, Scaled(0.15f));
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
        // Show the detection range and horizontal swoop reach.
        Gizmos.color = gizmoDetection;
        Gizmos.DrawWireSphere(transform.position, Scaled(giveUpDistance));
        Gizmos.color = gizmoDivePath;
        Gizmos.DrawLine(transform.position, transform.position + Vector3.right * facingDirection * Scaled(swoopMaxDistance));
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
    //   ShootWindup     : start of the shoot telegraph
    //   Shoot           : projectile release
    //   Hurt            : fired on every damage event
    //   Death           : fired once when health hits 0
    // Floats (optional, for blend trees):
    //   HorizontalSpeed : signed horizontal velocity
    //   VerticalSpeed   : signed vertical velocity
    // ===============================================================
}
