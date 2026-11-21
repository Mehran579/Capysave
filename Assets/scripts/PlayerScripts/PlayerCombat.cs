// Four-directional melee + Eagle Purge slam + Fireball.
// Reads state from PlayerController, emits OnEnemyHit for PlayerMeter.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerController))]
public class PlayerCombat : MonoBehaviour
{
    public enum AttackDirection { Side, Up, Down }

    // ------------------------------------------------------------------
    // Inspector
    // ------------------------------------------------------------------
    [Header("General")]
    [Tooltip("Layers considered enemies.")]
    [SerializeField] private LayerMask enemyLayer;
    [Tooltip("Cooldown between any melee attacks (seconds).")]
    [Min(0f)] [SerializeField] private float attackCooldown = 0.32f;

    [Header("Side Attack (horizontal slash)")]
    [SerializeField] private Vector2 sideAttackOffset = new Vector2(1.1f, 0f);
    [SerializeField] private Vector2 sideAttackSize = new Vector2(1.8f, 1.2f);
    [Min(0.01f)] [SerializeField] private float sideAttackActive = 0.1f;
    [Min(0)] [SerializeField] private int sideAttackDamage = 1;
    [Min(0f)] [SerializeField] private float sideAttackKnockback = 5f;

    [Header("Up Attack (hold W + attack)")]
    [SerializeField] private Vector2 upAttackOffset = new Vector2(0f, 1.3f);
    [SerializeField] private Vector2 upAttackSize = new Vector2(1.2f, 1.6f);
    [Min(0.01f)] [SerializeField] private float upAttackActive = 0.1f;
    [Min(0)] [SerializeField] private int upAttackDamage = 1;
    [Min(0f)] [SerializeField] private float upAttackKnockback = 4f;

    [Header("Down Attack (hold S + attack, airborne — pogo)")]
    [SerializeField] private Vector2 downAttackOffset = new Vector2(0f, -1.3f);
    [SerializeField] private Vector2 downAttackSize = new Vector2(1.2f, 1.6f);
    [Min(0.01f)] [SerializeField] private float downAttackActive = 0.1f;
    [Min(0)] [SerializeField] private int downAttackDamage = 1;
    [Min(0f)] [SerializeField] private float downAttackKnockback = 3f;
    [Tooltip("Upward velocity applied on a successful pogo hit.")]
    [Min(0f)] [SerializeField] private float pogoForce = 16f;

    [Header("Eagle Purge — K")]
    [Tooltip("Vertical distance the player rises before the slam.")]
    [Min(0f)] [SerializeField] private float purgeRiseHeight = 4f;
    [Min(0.05f)] [SerializeField] private float purgeRiseDuration = 0.22f;
    [Min(0f)] [SerializeField] private float purgeHangTime = 0.18f;
    [Tooltip("Diagonal slam speed (velocity magnitude).")]
    [Min(0f)] [SerializeField] private float purgeSlamSpeed = 28f;
    [Tooltip("Body hitbox around the player during descent.")]
    [SerializeField] private Vector2 purgeBodyBoxSize = new Vector2(1.8f, 1.8f);
    [SerializeField] private Vector2 purgeBodyBoxOffset = Vector2.zero;
    [Min(0)] [SerializeField] private int purgeBodyDamage = 2;
    [Tooltip("Shockwave hitbox spawned on landing.")]
    [SerializeField] private Vector2 purgeShockwaveSize = new Vector2(5f, 1.2f);
    [SerializeField] private Vector2 purgeShockwaveOffset = new Vector2(0f, -0.5f);
    [Min(0.01f)] [SerializeField] private float purgeShockwaveDuration = 0.25f;
    [Min(0)] [SerializeField] private int purgeShockwaveDamage = 3;
    [Min(0f)] [SerializeField] private float purgeCooldown = 1.5f;
    [Tooltip("If true the player can still influence horizontal motion during the slam descent.")]
    [SerializeField] private bool allowLimitedControlDuringSlam = false;

    [Header("Fireball — F")]
    [SerializeField] private Fireball fireballPrefab;
    [SerializeField] private Vector2 fireballSpawnOffset = new Vector2(0.9f, 0.2f);
    [Min(0f)] [SerializeField] private float fireballCooldown = 0.55f;

    [Header("Input Bindings")]
    [SerializeField] private string attackBindingPrimary = "<Keyboard>/j";
    [SerializeField] private string attackBindingSecondary = "<Mouse>/leftButton";
    [SerializeField] private string purgeBinding = "<Keyboard>/k";
    [SerializeField] private string fireballBinding = "<Keyboard>/f";

    // ------------------------------------------------------------------
    // Events
    // ------------------------------------------------------------------
    /// <summary>Raised each time a melee or purge strike lands on an IDamageable (fireball is handled separately).</summary>
    public event Action<int> OnEnemyHit;

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------
    private PlayerController controller;

    private InputAction attackAction;
    private InputAction purgeAction;
    private InputAction fireballAction;

    private float attackCooldownTimer;
    private float purgeCooldownTimer;
    private float fireballCooldownTimer;

    private Coroutine attackCoroutine;
    private Coroutine purgeCoroutine;

    // For OnDrawGizmos we remember the last attack direction so you can tweak visually.
    private AttackDirection lastDirection = AttackDirection.Side;

    // ==================================================================
    // Unity messages
    // ==================================================================
    private void Awake()
    {
        controller = GetComponent<PlayerController>();

        attackAction = new InputAction("Attack", InputActionType.Button);
        attackAction.AddBinding(attackBindingPrimary);
        attackAction.AddBinding(attackBindingSecondary);

        purgeAction = new InputAction("EaglePurge", InputActionType.Button, purgeBinding);
        fireballAction = new InputAction("Fireball", InputActionType.Button, fireballBinding);
    }

    private void OnEnable()
    {
        attackAction.Enable();
        purgeAction.Enable();
        fireballAction.Enable();
    }

    private void OnDisable()
    {
        attackAction.Disable();
        purgeAction.Disable();
        fireballAction.Disable();
    }

    private void OnDestroy()
    {
        attackAction?.Dispose();
        purgeAction?.Dispose();
        fireballAction?.Dispose();
    }

    private void Update()
    {
        if (attackCooldownTimer > 0f) attackCooldownTimer -= Time.deltaTime;
        if (purgeCooldownTimer > 0f) purgeCooldownTimer -= Time.deltaTime;
        if (fireballCooldownTimer > 0f) fireballCooldownTimer -= Time.deltaTime;

        if (!CanAct()) return;

        if (attackAction.WasPressedThisFrame() && attackCooldownTimer <= 0f)
            TryAttack();

        if (purgeAction.WasPressedThisFrame() && purgeCooldownTimer <= 0f)
            TryEaglePurge();

        if (fireballAction.WasPressedThisFrame() && fireballCooldownTimer <= 0f)
            SpawnFireball();
    }

    private bool CanAct()
    {
        return !controller.IsTransformed
            && !controller.IsStunned
            && !controller.IsDashing
            && !controller.IsSlamming;
    }

    // ==================================================================
    // Melee attack
    // ==================================================================
    private void TryAttack()
    {
        AttackDirection dir = AttackDirection.Side;
        if (controller.MoveInput.y > 0.5f)
            dir = AttackDirection.Up;
        else if (controller.MoveInput.y < -0.5f && !controller.IsGrounded)
            dir = AttackDirection.Down;

        if (attackCoroutine != null) StopCoroutine(attackCoroutine);
        attackCoroutine = StartCoroutine(AttackRoutine(dir));
    }

    private IEnumerator AttackRoutine(AttackDirection direction)
    {
        lastDirection = direction;
        attackCooldownTimer = attackCooldown;

        GetAttackParams(direction, out Vector2 offset, out Vector2 size, out float active, out int damage, out float knockback);
        if (direction == AttackDirection.Side)
            offset.x = Mathf.Abs(offset.x) * controller.FacingDirection;

        HashSet<IDamageable> alreadyHit = new HashSet<IDamageable>();
        bool pogoTriggered = false;
        float elapsed = 0f;

        while (elapsed < active)
        {
            Vector2 center = (Vector2)transform.position + offset;
            Collider2D[] hits = Physics2D.OverlapBoxAll(center, size, 0f, enemyLayer);
            foreach (Collider2D c in hits)
            {
                IDamageable d = c.GetComponentInParent<IDamageable>();
                if (d == null || alreadyHit.Contains(d)) continue;
                alreadyHit.Add(d);

                Vector2 kbDir = direction == AttackDirection.Up ? Vector2.up
                              : direction == AttackDirection.Down ? Vector2.down
                              : new Vector2(controller.FacingDirection, 0f);
                d.TakeDamage(damage, kbDir * knockback);
                OnEnemyHit?.Invoke(damage);

                // Pogo on a successful down-attack hit while airborne.
                if (direction == AttackDirection.Down && !pogoTriggered)
                {
                    pogoTriggered = true;
                    controller.PogoBounce(pogoForce);
                    // Reset attack-cooldown and air-dash so the player can chain.
                    attackCooldownTimer = 0f;
                    controller.CanAirDash = true;
                }
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        attackCoroutine = null;
    }

    private void GetAttackParams(AttackDirection dir, out Vector2 offset, out Vector2 size, out float active, out int damage, out float knockback)
    {
        switch (dir)
        {
            case AttackDirection.Up:
                offset = upAttackOffset; size = upAttackSize; active = upAttackActive; damage = upAttackDamage; knockback = upAttackKnockback; break;
            case AttackDirection.Down:
                offset = downAttackOffset; size = downAttackSize; active = downAttackActive; damage = downAttackDamage; knockback = downAttackKnockback; break;
            default:
                offset = sideAttackOffset; size = sideAttackSize; active = sideAttackActive; damage = sideAttackDamage; knockback = sideAttackKnockback; break;
        }
    }

    // ==================================================================
    // Eagle Purge
    // ==================================================================
    private void TryEaglePurge()
    {
        if (purgeCoroutine != null) StopCoroutine(purgeCoroutine);
        purgeCoroutine = StartCoroutine(EaglePurgeRoutine());
    }

    private IEnumerator EaglePurgeRoutine()
    {
        controller.IsSlamming = true;
        purgeCooldownTimer = purgeCooldown;

        Rigidbody2D rb = controller.Rigidbody;
        float savedGravity = rb.gravityScale;
        rb.gravityScale = 0f;

        // Phase 1 — rise.
        float riseSpeed = purgeRiseHeight / purgeRiseDuration;
        rb.linearVelocity = new Vector2(0f, riseSpeed);
        yield return new WaitForSeconds(purgeRiseDuration);
        rb.linearVelocity = Vector2.zero;

        // Phase 2 — hang at apex.
        yield return new WaitForSeconds(purgeHangTime);

        // Phase 3 — diagonal slam toward the ground in facing direction.
        Vector2 slamDir = new Vector2(controller.FacingDirection, -1f).normalized;
        rb.linearVelocity = slamDir * purgeSlamSpeed;

        HashSet<IDamageable> hitOnDescent = new HashSet<IDamageable>();
        float safetyTimer = 0f;
        const float safetyMax = 3f; // bail if we never land (open air)

        while (!controller.IsGrounded && safetyTimer < safetyMax)
        {
            // Allow horizontal override if configured.
            if (allowLimitedControlDuringSlam)
            {
                float desiredX = controller.MoveInput.x * purgeSlamSpeed * 0.6f;
                if (Mathf.Abs(controller.MoveInput.x) > 0.1f)
                    rb.linearVelocity = new Vector2(desiredX, rb.linearVelocity.y);
            }

            // Body hitbox damage.
            Vector2 bodyCenter = (Vector2)transform.position + purgeBodyBoxOffset;
            Collider2D[] hits = Physics2D.OverlapBoxAll(bodyCenter, purgeBodyBoxSize, 0f, enemyLayer);
            foreach (Collider2D c in hits)
            {
                IDamageable d = c.GetComponentInParent<IDamageable>();
                if (d == null || hitOnDescent.Contains(d)) continue;
                hitOnDescent.Add(d);
                d.TakeDamage(purgeBodyDamage, slamDir * 10f);
                OnEnemyHit?.Invoke(purgeBodyDamage);
            }

            safetyTimer += Time.deltaTime;
            yield return null;
        }

        // Phase 4 — landing shockwave.
        rb.linearVelocity = Vector2.zero;
        rb.gravityScale = savedGravity;

        HashSet<IDamageable> hitShock = new HashSet<IDamageable>();
        float shockElapsed = 0f;
        while (shockElapsed < purgeShockwaveDuration)
        {
            Vector2 shockCenter = (Vector2)transform.position + purgeShockwaveOffset;
            Collider2D[] hits = Physics2D.OverlapBoxAll(shockCenter, purgeShockwaveSize, 0f, enemyLayer);
            foreach (Collider2D c in hits)
            {
                IDamageable d = c.GetComponentInParent<IDamageable>();
                if (d == null || hitShock.Contains(d)) continue;
                hitShock.Add(d);
                float sign = Mathf.Sign(c.transform.position.x - transform.position.x);
                if (sign == 0f) sign = 1f;
                d.TakeDamage(purgeShockwaveDamage, new Vector2(sign * 8f, 4f));
                OnEnemyHit?.Invoke(purgeShockwaveDamage);
            }
            shockElapsed += Time.deltaTime;
            yield return null;
        }

        controller.IsSlamming = false;
        purgeCoroutine = null;
    }

    // ==================================================================
    // Fireball
    // ==================================================================
    private void SpawnFireball()
    {
        if (fireballPrefab == null)
        {
            Debug.LogWarning($"{nameof(PlayerCombat)}: no fireballPrefab assigned.");
            return;
        }
        fireballCooldownTimer = fireballCooldown;

        Vector2 spawn = (Vector2)transform.position
            + new Vector2(fireballSpawnOffset.x * controller.FacingDirection, fireballSpawnOffset.y);
        Fireball fb = Instantiate(fireballPrefab, spawn, Quaternion.identity);
        fb.Launch(controller.FacingDirection);
    }

    // ==================================================================
    // Interruption (called by PlayerHealth on damage, etc.)
    // ==================================================================
    public void InterruptAll()
    {
        if (attackCoroutine != null) { StopCoroutine(attackCoroutine); attackCoroutine = null; }
        if (purgeCoroutine != null)
        {
            StopCoroutine(purgeCoroutine);
            purgeCoroutine = null;
            controller.IsSlamming = false;
            controller.Rigidbody.gravityScale = controller.OriginalGravity;
        }
    }

    // ==================================================================
    // Gizmos — every hitbox in its own color.
    // ==================================================================
    private void OnDrawGizmos()
    {
        Vector3 p = transform.position;
        int facing = Application.isPlaying && controller != null ? controller.FacingDirection : 1;

        // Side attack — red.
        Gizmos.color = new Color(1f, 0.25f, 0.25f, 0.6f);
        Vector3 sideCenter = p + new Vector3(Mathf.Abs(sideAttackOffset.x) * facing, sideAttackOffset.y, 0f);
        Gizmos.DrawWireCube(sideCenter, sideAttackSize);

        // Up attack — cyan.
        Gizmos.color = new Color(0.25f, 0.9f, 1f, 0.6f);
        Gizmos.DrawWireCube(p + (Vector3)upAttackOffset, upAttackSize);

        // Down / pogo — magenta.
        Gizmos.color = new Color(1f, 0.3f, 0.9f, 0.6f);
        Gizmos.DrawWireCube(p + (Vector3)downAttackOffset, downAttackSize);

        // Eagle Purge body box — yellow.
        Gizmos.color = new Color(1f, 0.9f, 0.1f, 0.6f);
        Gizmos.DrawWireCube(p + (Vector3)purgeBodyBoxOffset, purgeBodyBoxSize);

        // Eagle Purge shockwave — orange.
        Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.6f);
        Gizmos.DrawWireCube(p + (Vector3)purgeShockwaveOffset, purgeShockwaveSize);

        // Fireball spawn point — white.
        Gizmos.color = Color.white;
        Vector3 fbSpawn = p + new Vector3(fireballSpawnOffset.x * facing, fireballSpawnOffset.y, 0f);
        Gizmos.DrawWireSphere(fbSpawn, 0.15f);
    }
}
