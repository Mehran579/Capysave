using UnityEngine;
using UnityEngine.UI;

/// <summary>Creates a compact screen-space HUD for player health and transformation time.</summary>
[RequireComponent(typeof(PlayerHealth))]
[RequireComponent(typeof(PlayerMeter))]
public class PlayerHUD : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] private Vector2 topLeftOffset = new Vector2(32f, 30f);
    [SerializeField] private Vector2 barSize = new Vector2(300f, 24f);
    [SerializeField] private float spacing = 15f;

    [Header("Colors")]
    [SerializeField] private Color healthColor = new Color(0.9f, 0.2f, 0.24f, 1f);
    [SerializeField] private Color meterColor = new Color(0.2f, 0.85f, 0.95f, 1f);

    private PlayerHealth health;
    private PlayerMeter meter;
    private Image healthFill;
    private Image meterFill;

    private void Awake()
    {
        health = GetComponent<PlayerHealth>();
        meter = GetComponent<PlayerMeter>();
        BuildHud();
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

    private void BuildHud()
    {
        var canvasObject = new GameObject("Player HUD Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Slider unusedHealthSlider;
        healthFill = CreateBar(canvasObject.transform, "Health", 0, healthColor, out unusedHealthSlider);
        Slider meterSlider;
        meterFill = CreateBar(canvasObject.transform, "Transformation Time", 1, meterColor, out meterSlider);
        meter.SetMeterSlider(meterSlider);
    }

    private Image CreateBar(Transform parent, string label, int row, Color fillColor, out Slider slider)
    {
        float y = -(topLeftOffset.y + row * (barSize.y + spacing));
        var backgroundObject = new GameObject(label + " Bar Background", typeof(RectTransform), typeof(Image));
        backgroundObject.transform.SetParent(parent, false);
        RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
        backgroundRect.anchorMin = new Vector2(0f, 1f);
        backgroundRect.anchorMax = new Vector2(0f, 1f);
        backgroundRect.pivot = new Vector2(0f, 1f);
        backgroundRect.anchoredPosition = new Vector2(topLeftOffset.x, y);
        backgroundRect.sizeDelta = barSize;
        backgroundObject.GetComponent<Image>().color = new Color(0.035f, 0.055f, 0.09f, 0.88f);

        var fillObject = new GameObject(label + " Bar Fill", typeof(RectTransform), typeof(Image));
        fillObject.transform.SetParent(backgroundObject.transform, false);
        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(3f, 3f);
        fillRect.offsetMax = new Vector2(-3f, -3f);
        Image fill = fillObject.GetComponent<Image>();
        fill.color = fillColor;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 1f;
        slider = null;
        if (row == 1)
        {
            slider = backgroundObject.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.direction = Slider.Direction.LeftToRight;
            slider.fillRect = fillRect;
            slider.targetGraphic = fill;
        }
        return fill;
    }
}
