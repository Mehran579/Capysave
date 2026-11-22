using UnityEngine;
using UnityEngine.UI;

/// <summary>Updates the scene-authored screen-space HUD for player health and transformation time.</summary>
[RequireComponent(typeof(PlayerHealth))]
[RequireComponent(typeof(PlayerMeter))]
public class PlayerHUD : MonoBehaviour
{
    private PlayerHealth health;
    private PlayerMeter meter;
    [SerializeField] private Image healthFill;
    [SerializeField] private Image meterFill;
    [SerializeField] private Slider meterSlider;

    private void Start()
    {
        health = GetComponent<PlayerHealth>();
        meter = GetComponent<PlayerMeter>();
        if (meter != null) meter.SetMeterSlider(meterSlider);
    }

    private void OnEnable()
    {
        if (health == null) health = GetComponent<PlayerHealth>();
        if (meter == null) meter = GetComponent<PlayerMeter>();
        if (health != null) health.OnHealthChanged.AddListener(UpdateHealth);
        if (meter != null) meter.OnMeterChanged.AddListener(UpdateMeter);
        Refresh();
    }

    private void OnDisable()
    {
        if (health != null) health.OnHealthChanged.RemoveListener(UpdateHealth);
        if (meter != null) meter.OnMeterChanged.RemoveListener(UpdateMeter);
    }

    private void Refresh()
    {
        if (health != null) UpdateHealth(health.CurrentHealth);
        if (meter != null) UpdateMeter(meter.CurrentMeterNormalized);
    }

    private void UpdateHealth(int current)
    {
        if (healthFill != null) healthFill.fillAmount = health.MaxHealth > 0 ? (float)current / health.MaxHealth : 0f;
    }

    private void UpdateMeter(float normalized)
    {
        if (meterFill != null) meterFill.fillAmount = Mathf.Clamp01(normalized);
    }

}
