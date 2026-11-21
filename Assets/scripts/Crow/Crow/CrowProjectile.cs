// =====================================================================
// CrowProjectile.cs
// Ricocheting projectile fired by CrowEnemy. Standalone.
// Uses a *trigger* CircleCollider2D so it doesn't get pushed around by
// physics; the reflection is computed manually off the surface normal.
// Implements IDamageable (1 HP) so player attacks can destroy it.
// Depends only on IDamageable (void TakeDamage(int, Vector2)).
// Compiles on Unity 2022/2023 LTS and Unity 6.
// =====================================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
[DisallowMultipleComponent]
public class CrowProjectile : MonoBehaviour, IDamageable
{
    // ---------------------------------------------------------------
    // SERIALIZED
    // ---------------------------------------------------------------
    [Header("Damage")]
    [SerializeField, Min(0), Tooltip("Damage dealt to the player on hit.")]
    private int projectileDamage = 1;

    [SerializeField, Min(0f), Tooltip("Knockback speed applied to the player on hit.")]
    private float playerKnockback = 6f;

    [Header("Ricochet")]
    [SerializeField, Tooltip("Layers this projectile ricochets off (walls, floors, platforms).")]
    private LayerMask bounceLayers;

    [SerializeField, Min(0f), Tooltip("Speed scalar applied after each bounce (1 = keeps speed).")]
    private float bounceSpeedMultiplier = 1f;

    [SerializeField, Min(0), Tooltip("Max bounces before the projectile breaks. 0 = unlimited (relies on lifetime).")]
    private int maxBounces = 0;

    [Header("Layers")]
    [SerializeField, Tooltip("Layer that counts as the player. Projectile deals damage then breaks on contact.")]
    private LayerMask playerLayer;

    [SerializeField, Tooltip("Obstacle layer (reserved for future ground-specific logic; typically = bounceLayers).")]
    private LayerMask groundLayer;

    [Header("Lifetime")]
    [SerializeField, Min(0.1f), Tooltip("Max seconds the projectile stays alive.")]
    private float lifetime = 4f;

    [SerializeField, Min(0f), Tooltip("Seconds at the end of lifetime that play the shrink/flash warning.")]
    private float warnBeforeBreakTime = 0.6f;

    [Header("Break Effect")]
    [SerializeField, Min(0f), Tooltip("Seconds the break scale-pop plays before despawn.")]
    private float breakFxDuration = 0.1f;

    [SerializeField, Tooltip("Color flashed during the break pop.")]
    private Color breakFxColor = Color.white;

    [Header("Visuals")]
    [SerializeField, Tooltip("Sprite renderer used for the warning flash and break effect.")]
    private SpriteRenderer spriteRenderer;

    [SerializeField, Tooltip("If true, the transform rotates so +X faces the current velocity.")]
    private bool rotateToVelocity = true;

    [Header("Collision Safety")]
    [SerializeField, Min(0f), Tooltip("Extra probe distance ahead when solving the reflection normal.")]
    private float normalProbeDistance = 0.4f;

    [Header("Animation (optional)")]
    [SerializeField, Tooltip("Optional Animator. The projectile runs correctly with no Animator assigned.")]
    private Animator animator;

    [Header("Gizmo Colors")]
    [SerializeField] private Color gizmoCollider  = new Color(0.4f, 1f,   0.4f, 0.9f);
    [SerializeField] private Color gizmoVelocity  = new Color(1f,   1f,   0f,   1f);
    [SerializeField] private Color gizmoNextBounce = new Color(1f,  0.4f, 1f,   1f);

    // ---------------------------------------------------------------
    // RUNTIME
    // ---------------------------------------------------------------
    private Rigidbody2D rb;
    private CircleCollider2D col;
    private Coroutine lifetimeCo;
    private bool active;
    private int hp = 1;
    private int bouncesLeft;
    private Vector2 lastVelocity;
    private Color baseColor = Color.white;
    private Vector3 baseScale = Vector3.one;
    private int sourcePrefabId;

    // ---------------------------------------------------------------
    // SIMPLE PREFAB-KEYED POOL
    // ---------------------------------------------------------------
    private static Dictionary<int, Stack<CrowProjectile>> pool = new Dictionary<int, Stack<CrowProjectile>>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearPoolOnReload() { pool.Clear(); }

    public static CrowProjectile Spawn(CrowProjectile prefab, Vector2 position, Vector2 velocity)
    {
        if (prefab == null) return null;
        int id = prefab.GetInstanceID();
        CrowProjectile instance = null;

        if (pool.TryGetValue(id, out var stack))
        {
            while (stack.Count > 0)
            {
                var candidate = stack.Pop();
                if (candidate != null) { instance = candidate; break; }
            }
        }

        if (instance == null)
        {
            instance = Instantiate(prefab);
            instance.sourcePrefabId = id;
        }

        instance.transform.position = position;
        instance.transform.rotation = Quaternion.identity;
        instance.transform.localScale = instance.baseScale == Vector3.zero ? Vector3.one : instance.baseScale;
        instance.gameObject.SetActive(true);
        instance.Launch(velocity);
        return instance;
    }

    private void ReturnToPool()
    {
        gameObject.SetActive(false);
        if (!pool.TryGetValue(sourcePrefabId, out var stack))
        {
            stack = new Stack<CrowProjectile>();
            pool[sourcePrefabId] = stack;
        }
        stack.Push(this);
    }

    // ---------------------------------------------------------------
    // LIFECYCLE
    // ---------------------------------------------------------------
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<CircleCollider2D>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null) baseColor = spriteRenderer.color;
        baseScale = transform.localScale;
        if (baseScale == Vector3.zero) baseScale = Vector3.one;

        rb.gravityScale = 0f;
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        col.isTrigger = true;
    }

    public void Launch(Vector2 velocity)
    {
        active = true;
        hp = 1;
        bouncesLeft = maxBounces;
        rb.linearVelocity = velocity;
        lastVelocity = velocity;
        transform.localScale = baseScale;
        if (spriteRenderer != null) spriteRenderer.color = baseColor;

        if (rotateToVelocity && velocity.sqrMagnitude > 0.001f)
            transform.right = velocity.normalized;

        if (lifetimeCo != null) StopCoroutine(lifetimeCo);
        lifetimeCo = StartCoroutine(LifetimeRoutine());
        // animator.SetTrigger("Launch");
    }

    private void FixedUpdate()
    {
        if (!active) return;
        if (rb.linearVelocity.sqrMagnitude > 0.0001f)
        {
            lastVelocity = rb.linearVelocity;
            if (rotateToVelocity) transform.right = rb.linearVelocity.normalized;
        }
    }

    // ---------------------------------------------------------------
    // COLLISION / RICOCHET
    // ---------------------------------------------------------------
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!active) return;
        int otherLayerBit = 1 << other.gameObject.layer;

        // ignore self / other enemies: we don't deal damage to anything not on playerLayer.
        // (IDamageable is only invoked for the player below.)

        if ((playerLayer.value & otherLayerBit) != 0)
        {
            IDamageable dmg = other.GetComponentInParent<IDamageable>();
            if (dmg != null)
            {
                Vector2 dir = lastVelocity.sqrMagnitude > 0.001f ? lastVelocity.normalized : Vector2.right;
                dmg.TakeDamage(projectileDamage, dir * playerKnockback);
            }
            Break();
            return;
        }

        if ((bounceLayers.value & otherLayerBit) != 0)
        {
            Reflect(other);
            if (maxBounces > 0)
            {
                bouncesLeft--;
                if (bouncesLeft < 0) Break();
            }
        }
    }

    private void Reflect(Collider2D other)
    {
        Vector2 vel = lastVelocity.sqrMagnitude > 0.001f ? lastVelocity : rb.linearVelocity;
        if (vel.sqrMagnitude < 0.0001f) { Break(); return; }

        Vector2 dir = vel.normalized;

        // best case: probe back along our heading to find the real normal
        Vector2 origin = (Vector2)transform.position - dir * normalProbeDistance;
        RaycastHit2D hit = Physics2D.CircleCast(origin, col.radius * 0.95f, dir,
                                                normalProbeDistance + col.radius * 2f, bounceLayers);
        Vector2 normal;
        if (hit.collider != null)
        {
            normal = hit.normal;
        }
        else
        {
            // fallback: use ClosestPoint on the collider we entered
            Vector2 cp = other.ClosestPoint(transform.position);
            Vector2 away = (Vector2)transform.position - cp;
            if (away.sqrMagnitude > 0.0001f) normal = away.normalized;
            else                             normal = -dir;
        }

        Vector2 reflected = Vector2.Reflect(vel, normal) * bounceSpeedMultiplier;
        rb.linearVelocity = reflected;
        lastVelocity = reflected;

        // nudge out of the surface so we don't re-trigger
        transform.position = (Vector2)transform.position + normal * 0.03f;

        if (rotateToVelocity && reflected.sqrMagnitude > 0.001f)
            transform.right = reflected.normalized;

        // animator.SetTrigger("Bounce");
    }

    // ---------------------------------------------------------------
    // LIFETIME / WARNING
    // ---------------------------------------------------------------
    private IEnumerator LifetimeRoutine()
    {
        float warn = Mathf.Clamp(warnBeforeBreakTime, 0f, lifetime);
        float preWarn = Mathf.Max(0f, lifetime - warn);
        if (preWarn > 0f) yield return new WaitForSeconds(preWarn);

        if (warn > 0f && spriteRenderer != null)
        {
            float t = 0f;
            Color start = baseColor;
            while (t < warn && active)
            {
                t += Time.deltaTime;
                float u = t / warn;
                transform.localScale = Vector3.Lerp(baseScale, baseScale * 0.4f, u);
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 30f);
                spriteRenderer.color = Color.Lerp(start, breakFxColor, pulse);
                yield return null;
            }
        }
        else if (warn > 0f)
        {
            yield return new WaitForSeconds(warn);
        }

        if (active) Break();
    }

    // ---------------------------------------------------------------
    // IDamageable — projectile is destroyable by the player
    // ---------------------------------------------------------------
    public void TakeDamage(int amount, Vector2 hitDirection)
    {
        if (!active) return;
        hp -= Mathf.Max(0, amount);
        if (hp <= 0) Break();
        // animator.SetTrigger("Hurt");
    }

    // ---------------------------------------------------------------
    // BREAK (with placeholder FX + hooks)
    // ---------------------------------------------------------------
    private void Break()
    {
        if (!active) return;
        active = false;
        if (lifetimeCo != null) { StopCoroutine(lifetimeCo); lifetimeCo = null; }
        StartCoroutine(BreakFxThenReturn());
        // animator.SetTrigger("Break");
        // Hook for later:
        // if (breakParticlePrefab != null) Instantiate(breakParticlePrefab, transform.position, Quaternion.identity);
    }

    private IEnumerator BreakFxThenReturn()
    {
        rb.linearVelocity = Vector2.zero;
        if (col != null) col.enabled = false;

        float t = 0f;
        Vector3 start = transform.localScale;
        Vector3 big = start * 1.6f;
        while (t < breakFxDuration)
        {
            t += Time.deltaTime;
            float u = t / breakFxDuration;
            transform.localScale = Vector3.Lerp(big, Vector3.zero, u);
            if (spriteRenderer != null)
            {
                Color c = Color.Lerp(breakFxColor, baseColor, u);
                c.a = Mathf.Lerp(1f, 0f, u);
                spriteRenderer.color = c;
            }
            yield return null;
        }

        // reset for pool reuse
        transform.localScale = baseScale;
        if (col != null) col.enabled = true;
        if (spriteRenderer != null) spriteRenderer.color = baseColor;

        ReturnToPool();
    }

    // ---------------------------------------------------------------
    // GIZMOS
    // ---------------------------------------------------------------
    private void OnDrawGizmos()
    {
        var c = GetComponent<CircleCollider2D>();
        if (c != null)
        {
            Gizmos.color = gizmoCollider;
            Gizmos.DrawWireSphere(transform.position + (Vector3)c.offset, c.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y));
        }

        if (!Application.isPlaying) return;

        Vector2 vel = rb != null ? rb.linearVelocity : Vector2.zero;
        if (vel.sqrMagnitude > 0.001f)
        {
            Gizmos.color = gizmoVelocity;
            Gizmos.DrawLine(transform.position, (Vector2)transform.position + vel.normalized * 0.8f);

            // predicted next bounce
            RaycastHit2D hit = Physics2D.CircleCast(transform.position,
                c != null ? c.radius : 0.1f,
                vel.normalized, 6f, bounceLayers);
            if (hit.collider != null)
            {
                Gizmos.color = gizmoNextBounce;
                Gizmos.DrawLine(transform.position, hit.point);
                Vector2 reflected = Vector2.Reflect(vel.normalized, hit.normal);
                Gizmos.DrawLine(hit.point, hit.point + reflected * 1.0f);
            }
        }
    }

    // ===============================================================
    // ANIMATOR PARAMETERS (reference list)
    // ---------------------------------------------------------------
    // Triggers:
    //   Launch : fired when the projectile is (re)spawned
    //   Bounce : fired on each ricochet
    //   Hurt   : fired when the projectile itself is damaged
    //   Break  : fired on final break
    // ===============================================================
}
