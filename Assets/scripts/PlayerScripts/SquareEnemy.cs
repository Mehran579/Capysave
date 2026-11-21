using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Placeholder version of the fast flyer in the reference clip. It drifts in
/// the air, makes quick committed passes through the player's space, then pulls
/// away and immediately lines up for another pass.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(SpriteRenderer))]
public class SquareEnemy : MonoBehaviour, IDamageable
{
    private enum State { Drift, Dive, Recover, Dead }

    [Header("Health and damage")]
    [Min(1)] [SerializeField] private int maxHealth = 3;
    [Min(0)] [SerializeField] private int contactDamage = 1;
    [Min(0f)] [SerializeField] private float contactKnockback = 7f;
    [Min(0f)] [SerializeField] private float hitKnockback = 3f;
    [Min(0f)] [SerializeField] private float deathDelay = 0.15f;
    [SerializeField] private Color hitFlashColor = Color.white;
    [Min(0.01f)] [SerializeField] private float hitFlashDuration = 0.08f;
    public UnityEvent<int> OnDamaged;
    public UnityEvent OnDied;

    [Header("Flight and dive")]
    [Min(0f)] [SerializeField] private float detectionRange = 9f;
    [Min(0.1f)] [SerializeField] private float driftSpeed = 2.8f;
    [Min(0.1f)] [SerializeField] private float driftRadius = 2.2f;
    [Min(0f)] [SerializeField] private float driftHeight = 2.4f;
    [Min(0.1f)] [SerializeField] private float diveSpeed = 12f;
    [Min(0.1f)] [SerializeField] private float diveDuration = 0.38f;
    [Min(0f)] [SerializeField] private float recoveryTime = 0.22f;
    [SerializeField] private LayerMask obstacleLayer;

    [Header("Square placeholder")]
    [SerializeField] private Color squareColor = new Color(0.72f, 0.45f, 0.22f, 1f);
    [SerializeField] private Vector2 squareSize = new Vector2(0.8f, 0.8f);

    private Rigidbody2D rb;
    private BoxCollider2D body;
    private SpriteRenderer sr;
    private Sprite generatedSquare;
    private Color baseColor;
    private Vector2 spawnPosition;
    private Transform player;
    private int currentHealth;
    private State state;
    private Coroutine flashRoutine;
    private Coroutine deathRoutine;

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        body = GetComponent<BoxCollider2D>();
        sr = GetComponent<SpriteRenderer>();
        spawnPosition = transform.position;
        currentHealth = maxHealth;
        baseColor = squareColor;

        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.isTrigger = false;
        body.size = squareSize;

        // A Unity built-in white sprite tinted brown gives a no-art square.
        generatedSquare = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        sr.sprite = generatedSquare;
        sr.color = squareColor;
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = squareSize;

        int enemyLayer = LayerMask.NameToLayer("Enemy");
        if (enemyLayer >= 0) gameObject.layer = enemyLayer;
    }

    private void Start()
    {
        player = FindPlayer();
        StartCoroutine(FlightRoutine());
    }

    private void OnDestroy()
    {
        if (generatedSquare != null) Destroy(generatedSquare);
    }

    private IEnumerator FlightRoutine()
    {
        float driftPhase = Random.Range(0f, Mathf.PI * 2f);
        while (state != State.Dead)
        {
            player = FindPlayer();
            if (player == null || Vector2.Distance(transform.position, player.position) > detectionRange)
            {
                state = State.Drift;
                float t = Time.time * 0.9f + driftPhase;
                Vector2 idlePoint = spawnPosition + new Vector2(Mathf.Cos(t) * driftRadius, driftHeight + Mathf.Sin(t * 1.7f) * 0.45f);
                MoveToward(idlePoint, driftSpeed);
                yield return null;
                continue;
            }

            // The clip's flyer does not pause to telegraph: it immediately
            // commits to a fast diagonal pass across the player's position.
            Vector2 playerPosition = player.position;
            float side = Mathf.Sign(transform.position.x - playerPosition.x);
            if (side == 0f) side = Random.value < 0.5f ? -1f : 1f;
            Vector2 passTarget = playerPosition + new Vector2(-side * driftRadius, -0.15f);
            Vector2 diveDirection = (passTarget - (Vector2)transform.position).normalized;
            if (diveDirection.sqrMagnitude < 0.01f) diveDirection = new Vector2(-side, -0.25f).normalized;

            state = State.Dive;
            float diveTimer = 0f;
            while (diveTimer < diveDuration && state == State.Dive)
            {
                diveTimer += Time.deltaTime;
                rb.linearVelocity = diveDirection * diveSpeed;
                if (obstacleLayer.value != 0 && Physics2D.Raycast(transform.position, diveDirection, 0.25f, obstacleLayer))
                    break;
                yield return new WaitForFixedUpdate();
            }

            state = State.Recover;
            float recoverTimer = 0f;
            float recoverDuration = Mathf.Max(0.01f, recoveryTime);
            while (recoverTimer < recoverDuration && state == State.Recover)
            {
                recoverTimer += Time.deltaTime;
                // Pull sharply upward and toward the next side of the player.
                Vector2 retreatPoint = (Vector2)transform.position + new Vector2(-diveDirection.x * driftRadius, 1.2f);
                if (player != null) retreatPoint.y = Mathf.Max(retreatPoint.y, player.position.y + driftHeight);
                MoveToward(retreatPoint, diveSpeed * 0.75f);
                yield return null;
            }
        }
    }

    private void MoveToward(Vector2 target, float speed)
    {
        Vector2 direction = target - rb.position;
        rb.linearVelocity = direction.sqrMagnitude < 0.04f ? Vector2.zero : direction.normalized * speed;
        if (Mathf.Abs(direction.x) > 0.02f) sr.flipX = direction.x < 0f;
    }

    private static Transform FindPlayer()
    {
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        return playerObject != null ? playerObject.transform : null;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (state == State.Dead || contactDamage <= 0 || !collision.gameObject.CompareTag("Player")) return;

        PlayerHealth health = collision.gameObject.GetComponentInParent<PlayerHealth>();
        if (health == null) return;

        Vector2 away = (health.transform.position - transform.position).normalized;
        if (away.sqrMagnitude < 0.01f) away = Vector2.down;
        health.TakeDamage(contactDamage, away * contactKnockback);
    }

    public void TakeDamage(int amount, Vector2 hitDirection)
    {
        if (state == State.Dead || amount <= 0) return;
        currentHealth = Mathf.Max(0, currentHealth - amount);
        OnDamaged?.Invoke(currentHealth);

        if (hitDirection.sqrMagnitude > 0.001f && hitKnockback > 0f)
            rb.linearVelocity = hitDirection.normalized * hitKnockback;

        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(HitFlash());
        if (currentHealth == 0) deathRoutine = StartCoroutine(DeathRoutine());
    }

    private IEnumerator HitFlash()
    {
        sr.color = hitFlashColor;
        yield return new WaitForSeconds(hitFlashDuration);
        if (state != State.Dead) sr.color = baseColor;
        flashRoutine = null;
    }

    private IEnumerator DeathRoutine()
    {
        state = State.Dead;
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = null;
        OnDied?.Invoke();
        rb.linearVelocity = Vector2.zero;
        body.enabled = false;
        yield return new WaitForSeconds(deathDelay);
        Destroy(gameObject);
        deathRoutine = null;
    }
}
