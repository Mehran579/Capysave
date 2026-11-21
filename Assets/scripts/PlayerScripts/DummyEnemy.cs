// Training dummy on the enemyLayer — IDamageable with hit flash, knockback and respawn.
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Collider2D))]
public class DummyEnemy : MonoBehaviour, IDamageable
{
    [Header("Health")]
    [Min(1)] [SerializeField] private int maxHealth = 5;
    [Tooltip("If true, the dummy respawns after dying; otherwise it is disabled.")]
    [SerializeField] private bool respawnAfterDeath = true;
    [Min(0.1f)] [SerializeField] private float respawnDelay = 2f;

    [Header("Behavior")]
    [Tooltip("True: training dummy that never moves. False: takes knockback like a normal enemy.")]
    [SerializeField] private bool stayStill = true;

    [Header("Feedback")]
    [SerializeField] private Color hitFlashColor = Color.red;
    [Min(0.01f)] [SerializeField] private float hitFlashDuration = 0.08f;
    [Min(0f)] [SerializeField] private float knockbackMultiplier = 0.4f;
    [Min(0f)] [SerializeField] private float knockbackRecoverTime = 0.2f;

    [Header("Events")]
    public UnityEvent<int> OnDamaged;
    public UnityEvent OnDied;
    public UnityEvent OnRespawned;

    // ------------------------------------------------------------------
    private int currentHealth;
    private SpriteRenderer sr;
    private Rigidbody2D rb;
    private Collider2D col;
    private Color originalColor;
    private Vector3 spawnPosition;
    private Coroutine flashCoroutine;
    private Coroutine knockbackCoroutine;

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        originalColor = sr.color;
        spawnPosition = transform.position;
        currentHealth = maxHealth;
        if (stayStill) rb.bodyType = RigidbodyType2D.Kinematic;
    }

    public void TakeDamage(int amount, Vector2 hitDirection)
    {
        if (currentHealth <= 0) return;
        currentHealth = Mathf.Max(0, currentHealth - amount);
        OnDamaged?.Invoke(currentHealth);

        if (flashCoroutine != null) StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(HitFlashRoutine());

        if (!stayStill && rb.bodyType != RigidbodyType2D.Kinematic)
        {
            if (knockbackCoroutine != null) StopCoroutine(knockbackCoroutine);
            knockbackCoroutine = StartCoroutine(KnockbackRoutine(hitDirection * knockbackMultiplier));
        }

        if (currentHealth <= 0) Die();
    }

    private IEnumerator HitFlashRoutine()
    {
        sr.color = hitFlashColor;
        yield return new WaitForSeconds(hitFlashDuration);
        sr.color = originalColor;
        flashCoroutine = null;
    }

    private IEnumerator KnockbackRoutine(Vector2 velocity)
    {
        rb.linearVelocity = velocity;
        yield return new WaitForSeconds(knockbackRecoverTime);
        rb.linearVelocity = Vector2.zero;
        knockbackCoroutine = null;
    }

    private void Die()
    {
        OnDied?.Invoke();
        if (respawnAfterDeath) StartCoroutine(RespawnRoutine());
        else gameObject.SetActive(false);
    }

    private IEnumerator RespawnRoutine()
    {
        sr.enabled = false;
        col.enabled = false;
        yield return new WaitForSeconds(respawnDelay);
        transform.position = spawnPosition;
        currentHealth = maxHealth;
        sr.enabled = true;
        col.enabled = true;
        sr.color = originalColor;
        OnRespawned?.Invoke();
    }
}
