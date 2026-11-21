
using UnityEngine;

/// Attach to each background layer (a GameObject with a SpriteRenderer).
/// Handles parallax scrolling and optional seamless horizontal tiling.
[RequireComponent(typeof(SpriteRenderer))]
public class ParallaxLayer : MonoBehaviour
{
    [Header("Camera (defaults to Camera.main)")]
    [SerializeField] private Transform cam;

    [Header("Parallax: 0 = moves with camera, 1 = locked to world")]
    [SerializeField, Range(0f, 1f)] private float effectX = 0.5f;
    [SerializeField, Range(0f, 1f)] private float effectY = 0f;

    [Header("Tiling")]
    [SerializeField] private bool tileHorizontally = true;

    private SpriteRenderer sr;
    private float tileWidth;
    private float originX;
    private float originY;
    private float camStartX;
    private float camStartY;
    private float z;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();

        if (cam == null && Camera.main != null)
            cam = Camera.main.transform;
    }

    private void Start()
    {
        if (cam == null)
        {
            Debug.LogError("ParallaxLayer: No camera assigned or found.", this);
            enabled = false;
            return;
        }

        Vector3 p = transform.position;

        originX = p.x;
        originY = p.y;
        z = p.z;

        camStartX = cam.position.x;
        camStartY = cam.position.y;

        tileWidth = sr.bounds.size.x;

        if (tileHorizontally && sr.sprite != null)
            CreateSideTiles();
    }

    private void LateUpdate()
    {
        float dx = cam.position.x - camStartX;
        float dy = cam.position.y - camStartY;

        float x = originX + dx * (1f - effectX);
        float y = originY + dy * (1f - effectY);

        if (tileHorizontally && tileWidth > 0f)
        {
            float shift = Mathf.Round((cam.position.x - x) / tileWidth) * tileWidth;

            originX += shift;
            x += shift;
        }

        transform.position = new Vector3(x, y, z);
    }

    // Creates one copy on either side of the original sprite.
    private void CreateSideTiles()
    {
        float localWidth = sr.sprite.bounds.size.x;

        for (int i = -1; i <= 1; i += 2)
        {
            GameObject go = new GameObject(
                $"{name}_tile_{(i < 0 ? "L" : "R")}"
            );

            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(i * localWidth, 0f, 0f);

            SpriteRenderer c = go.AddComponent<SpriteRenderer>();

            c.sprite = sr.sprite;
            c.sharedMaterial = sr.sharedMaterial;
            c.color = sr.color;
            c.flipX = sr.flipX;
            c.flipY = sr.flipY;
            c.sortingLayerID = sr.sortingLayerID;
            c.sortingOrder = sr.sortingOrder;
        }
    }
}
