// Soul/Silk-style meter + transformation.
// Gains on enemy hits (PlayerCombat.OnEnemyHit), lets player transform when full.
// Transformation lasts until E is pressed; eject restores the normal sprite and launches upward.
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
    [Header("Eject (E while transformed)")]
    [Tooltip("Vertical height in world units reached by the eject jump.")]
    [Min(0.1f)] [SerializeField] private float ejectJumpHeight = 10f;

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

    /// <summary>Connects a runtime-created HUD slider to this meter.</summary>
    public void SetMeterSlider(Slider slider)
    {
        meterSlider = slider;
        UpdateUI();
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------
    private PlayerController controller;
    private PlayerCombat combat;
    private Sprite originalSprite;
    private float currentMeter;

    private InputAction transformAction;
    private InputAction launchAction;


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
        if (!controller.IsTransformed)
        {
            if (IsMeterFull && transformAction.WasPressedThisFrame() && !controller.IsStunned)
                StartTransform();
        }
        else
        {
            if (launchAction.WasPressedThisFrame())
                Eject();
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
        // Cancel in-flight abilities cleanly on entering the form.
        controller.InterruptAbilities();
        controller.ClearSlow();

        controller.IsTransformed = true;
        if (transformedSprite != null) controller.SpriteRenderer.sprite = transformedSprite;
        currentMeter = 0f;
        UpdateUI();
        OnTransformStart?.Invoke();
    }

    private void Eject()
    {
        // Restore the regular form before applying the dedicated launch velocity.
        controller.IsTransformed = false;
        controller.SpriteRenderer.sprite = originalSprite;
        OnTransformEnd?.Invoke();
        Rigidbody2D rb = controller.Rigidbody;
        float effectiveG = Mathf.Abs(Physics2D.gravity.y) * Mathf.Max(0.01f, controller.OriginalGravity);
        float v = Mathf.Sqrt(2f * effectiveG * ejectJumpHeight);
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, v);
    }
}
