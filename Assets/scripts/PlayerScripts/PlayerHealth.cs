// Basic player health with knockback + i-frames.
// Ignores damage while transformed (invulnerable form).
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(PlayerController))]
public class PlayerHealth : MonoBehaviour, IDamageable
{
    // ------------------------------------------------------------------
    // Inspector
    // ------------------------------------------------------------------
    [Header("Health")]
    [Min(1)] [SerializeField] private int maxHealth = 5;
    [Tooltip("Total time the player is invulnerable after being hit.")]
    [Min(0f)] [SerializeField] private float invulnerabilityDuration = 0.8f;
    [Tooltip("Stun duration — no movement input during this window.")]
    [Min(0f)] [SerializeField] private float stunDuration = 0.18f;
    [Tooltip("Magnitude applied to the hit direction as knockback velocity.")]
    [Min(0f)] [SerializeField] private float knockbackForce = 10f;
    [Tooltip("Flashes per second while invulnerable.")]
    [Range(1f, 30f)] [SerializeField] private float flashRate = 10f;

    [Header("Events")]
    public UnityEvent<int> OnHealthChanged;
    public UnityEvent OnDied;

    // ------------------------------------------------------------------
    // State
    // ------------------------------------------------------------------
    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public bool IsInvulnerable { get; private set; }

    private PlayerController controller;
    private int currentHealth;
    private Coroutine iFrameCoroutine;

    private void Awake()
    {
        controller = GetComponent<PlayerController>();
        currentHealth = maxHealth;
    }

    private void Start()
    {
        OnHealthChanged?.Invoke(currentHealth);
    }

    // ==================================================================
    // IDamageable
    // ==================================================================
    public void TakeDamage(int amount, Vector2 hitDirection)
    {
        if (IsInvulnerable) return;
        if (controller.IsDashing || controller.IsSlamming) return;
        if (controller.IsTransformed) return; // transformed = invulnerable
        if (currentHealth <= 0) return;

        currentHealth = Mathf.Max(0, currentHealth - amount);
        OnHealthChanged?.Invoke(currentHealth);

        // Knockback. If the caller didn't supply a useful direction, knock away from facing.
        Vector2 kb = hitDirection;
        if (kb.sqrMagnitude < 0.01f)
            kb = new Vector2(-controller.FacingDirection, 0.4f);
        controller.ApplyKnockback(kb.normalized * knockbackForce);

        if (currentHealth <= 0)
        {
            controller.ClearSlow();
            OnDied?.Invoke();
            return;
        }

        controller.InterruptAbilities();

        if (iFrameCoroutine != null) StopCoroutine(iFrameCoroutine);
        iFrameCoroutine = StartCoroutine(IFrameRoutine());
    }

    private IEnumerator IFrameRoutine()
    {
        IsInvulnerable = true;
        controller.SetInvulnerabilityEnemyPassThrough(true);
        controller.SetStunned(true);
        float elapsed = 0f;
        SpriteRenderer sr = controller.SpriteRenderer;
        Color baseColor = sr.color;
        while (elapsed < invulnerabilityDuration)
        {
            // Keep the player stunned only for the configured hit-stun window,
            // while flashing white/transparent for the full invulnerability window.
            controller.SetStunned(elapsed < stunDuration);
            bool flashOn = Mathf.FloorToInt(elapsed * flashRate) % 2 == 0;
            sr.color = flashOn
                ? Color.white
                : new Color(baseColor.r, baseColor.g, baseColor.b, 0.2f);
            elapsed += Time.deltaTime;
            yield return null;
        }
        controller.SetStunned(false);
        controller.SetInvulnerabilityEnemyPassThrough(false);
        sr.color = baseColor;
        IsInvulnerable = false;
        iFrameCoroutine = null;
    }
}
