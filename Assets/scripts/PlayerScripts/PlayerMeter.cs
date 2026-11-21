// Soul/Silk-style meter + transformation.
// Gains on enemy hits (PlayerCombat.OnEnemyHit), lets player transform when full.
// Transformation: no jump / no attacks / invulnerable; can roll A/D and super-launch with E.
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[RequireComponent(typeof(PlayerController))]
public class PlayerMeter : MonoBehaviour
{
    // ------------------------------------------------------------------
    // Inspector
    // ------------------------------------------------------------------
    [Header("Meter")]
    [Tooltip("Meter fills from 0 to this value. Default is 3 so the meter is full after 3 hits.")]
    [Min(1f)] [SerializeField] private float maxMeter = 3f;
    [Tooltip("Amount added per successful hit.")]
    [Min(0f)] [SerializeField] private float meterGainPerHit = 1f;

    [Header("UI (optional)")]
    [Tooltip("Optional Slider displayed as the meter bar — leave null if you don't have UI yet.")]
    [SerializeField] private Slider meterSlider;

    [Header("Transformation")]
    [Tooltip("Sprite shown while the player is transformed.")]
    [SerializeField] private Sprite transformedSprite;
    [Tooltip("Total time the transformation lasts; meter drains linearly to zero over this duration.")]
    [Min(0.1f)] [SerializeField] private float transformDuration = 5f;

    [Header("Super Launch (E while transformed)")]
    [Tooltip("Vertical distance the launch should carry the player — set ~2x your normal jump height.")]
    [Min(0.1f)] [SerializeField] private float launchHeight = 10f;
    [Min(0f)] [SerializeField] private float launchCooldown = 1.2f;

    [Header("Input Bindings")]
    [SerializeField] private string transformBinding = "<Keyboard>/q";
    [SerializeField] private string launchBinding = "<Keyboard>/e";

    [Header("Events (for UI / VFX hooks)")]
    [Tooltip("Fired whenever the normalized meter value changes.")]
    public UnityEvent<float> OnMeterChanged;
    public UnityEvent OnTransformStart;
    public UnityEvent OnTransformEnd;

    // ------------------------------------------------------------------
    // Public getters
    // ------------------------------------------------------------------
    public float CurrentMeterNormalized => Mathf.Clamp01(currentMeter / maxMeter);
    public bool IsMeterFull => currentMeter >= maxMeter;

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------
    private PlayerController controller;
    private PlayerCombat combat;
    private Sprite originalSprite;
    private float currentMeter;
    private float launchCooldownTimer;

    private InputAction transformAction;
    private InputAction launchAction;

    private Coroutine transformCoroutine;

    // ==================================================================
    // Unity messages
    // ==================================================================
    private void Awake()
    {
        controller = GetComponent<PlayerController>();
        combat = GetComponent<PlayerCombat>();
        originalSprite = controller.SpriteRenderer.sprite;

        transformAction = new InputAction("Transform", InputActionType.Button, transformBinding);
        launchAction = new InputAction("SuperLaunch", InputActionType.Button, launchBinding);
    }

    private void OnEnable()
    {
        transformAction.Enable();
        launchAction.Enable();
        if (combat != null) combat.OnEnemyHit += HandleEnemyHit;
        UpdateUI();
    }

    private void OnDisable()
    {
        transformAction.Disable();
        launchAction.Disable();
        if (combat != null) combat.OnEnemyHit -= HandleEnemyHit;
    }

    private void OnDestroy()
    {
        transformAction?.Dispose();
        launchAction?.Dispose();
    }

    private void Update()
    {
        if (launchCooldownTimer > 0f) launchCooldownTimer -= Time.deltaTime;

        if (!controller.IsTransformed)
        {
            if (IsMeterFull && transformAction.WasPressedThisFrame() && !controller.IsStunned)
                StartTransform();
        }
        else
        {
            if (launchAction.WasPressedThisFrame() && launchCooldownTimer <= 0f)
                SuperLaunch();
        }
    }

    // ==================================================================
    // Meter gain / event handling
    // ==================================================================
    private void HandleEnemyHit(int _damage)
    {
        if (controller.IsTransformed) return;
        AddMeter(meterGainPerHit);
    }

    public void AddMeter(float amount)
    {
        currentMeter = Mathf.Clamp(currentMeter + amount, 0f, maxMeter);
        UpdateUI();
    }

    private void UpdateUI()
    {
        OnMeterChanged?.Invoke(CurrentMeterNormalized);
        if (meterSlider != null) meterSlider.value = CurrentMeterNormalized;
    }

    // ==================================================================
    // Transformation lifecycle
    // ==================================================================
    private void StartTransform()
    {
        if (transformCoroutine != null) StopCoroutine(transformCoroutine);
        transformCoroutine = StartCoroutine(TransformRoutine());
    }

    private IEnumerator TransformRoutine()
    {
        // Cancel in-flight abilities cleanly on entering the form.
        controller.InterruptAbilities();
        controller.ClearSlow();

        controller.IsTransformed = true;
        if (transformedSprite != null) controller.SpriteRenderer.sprite = transformedSprite;
        OnTransformStart?.Invoke();

        float startMeter = currentMeter;
        float elapsed = 0f;
        while (elapsed < transformDuration)
        {
            elapsed += Time.deltaTime;
            currentMeter = Mathf.Lerp(startMeter, 0f, elapsed / transformDuration);
            UpdateUI();
            yield return null;
        }

        currentMeter = 0f;
        controller.IsTransformed = false;
        controller.SpriteRenderer.sprite = originalSprite;
        UpdateUI();
        OnTransformEnd?.Invoke();
        transformCoroutine = null;
    }

    // ==================================================================
    // Super launch (E)
    // ==================================================================
    private void SuperLaunch()
    {
        launchCooldownTimer = launchCooldown;
        Rigidbody2D rb = controller.Rigidbody;
        // v = sqrt(2 * g * h), where g is the effective gravity acting on the body.
        float effectiveG = Mathf.Abs(Physics2D.gravity.y) * Mathf.Max(0.01f, controller.OriginalGravity);
        float v = Mathf.Sqrt(2f * effectiveG * launchHeight);
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, v);
    }
}
