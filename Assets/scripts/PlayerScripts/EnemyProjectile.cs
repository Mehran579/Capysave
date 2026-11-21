// Charged projectile fired by PatrolEnemy's Attack B.
// Damages the player through IDamageable (PlayerHealth) so invulnerability + transformed state are respected.
// Also applies a heavy slow via PlayerController.ApplySlow.
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class EnemyProjectile : MonoBehaviour
{
    // ------------------------------------------------------------------
    // Inspector
    // ------------------------------------------------------------------
    [Header("Movement")]
    [Min(0f)] [SerializeField] private float speed = 10f;
    [Min(0.05f)] [SerializeField] private float lifetime = 3f;

    [Header("Damage")]
    [Min(0)] [SerializeField] private int projectileDamage = 2;
    [Min(0f)] [SerializeField] private float knockbackForce = 4f;

    [Header("Slow Applied To Player")]
    [Tooltip("Player speed is multiplied by this while slowed (0.15 = 15% speed).")]
    [Range(0f, 1f)] [SerializeField] private float slowMultiplier = 0.15f;
    [Tooltip("Slow duration in seconds.")]
    [Min(0f)] [SerializeField] private float slowDuration = 0.4f;
    [Tooltip("If true, the slow also reduces jump force while active.")]
    [SerializeField] private bool slowAffectsJump = false;

    [Header("Layers")]
    [SerializeField] private LayerMask playerLayer;
    [SerializeField] private LayerMask groundLayer;

    // ------------------------------------------------------------------
    // Cached
    // ------------------------------------------------------------------
    private Rigidbody2D rb;
    private Collider2D col;
    private int facing = 1;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        rb.gravityScale = 0f;
        col.isTrigger = true;
    }

    // Called by PatrolEnemy right after Instantiate.
    public void Launch(int direction)
    {
        facing = direction >= 0 ? 1 : -1;
        rb.linearVelocity = new Vector2(facing * speed, 0f);
        Vector3 s = transform.localScale;
        s.x = Mathf.Abs(s.x) * facing;
        transform.localScale = s;
        StartCoroutine(LifetimeRoutine());
    }

    private IEnumerator LifetimeRoutine()
    {
        yield return new WaitForSeconds(lifetime);
        if (this != null) Destroy(gameObject);
    }

    // ------------------------------------------------------------------
    // Collisions
    // ------------------------------------------------------------------
    private void OnTriggerEnter2D(Collider2D other)
    {
        int bit = 1 << other.gameObject.layer;

        if ((bit & playerLayer.value) != 0)
        {
            // Damage via IDamageable so PlayerHealth decides whether to apply (invuln / transformed).
            IDamageable dmg = other.GetComponentInParent<IDamageable>();
            if (dmg != null)
                dmg.TakeDamage(projectileDamage, new Vector2(facing, 0.1f).normalized * knockbackForce);

            // Slow — PlayerController.ApplySlow ignores while transformed.
            PlayerController pc = other.GetComponentInParent<PlayerController>();
            if (pc != null)
                pc.ApplySlow(slowMultiplier, slowDuration, slowAffectsJump);

            Destroy(gameObject);
            return;
        }

        if ((bit & groundLayer.value) != 0)
        {
            Destroy(gameObject);
        }
    }

    // ------------------------------------------------------------------
    // Gizmos
    // ------------------------------------------------------------------
    private void OnDrawGizmos()
    {
        Collider2D c = GetComponent<Collider2D>();
        Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.8f);
        if (c is BoxCollider2D box)
            Gizmos.DrawWireCube(transform.position + (Vector3)box.offset, box.size);
        else if (c is CircleCollider2D circ)
            Gizmos.DrawWireSphere(transform.position + (Vector3)circ.offset, circ.radius);
        else
            Gizmos.DrawWireSphere(transform.position, 0.25f);

        // Travel direction line.
        Gizmos.color = new Color(1f, 0.8f, 0.2f);
        int face = Application.isPlaying ? facing : (int)Mathf.Sign(transform.localScale.x);
        if (face == 0) face = 1;
        Gizmos.DrawLine(transform.position, transform.position + new Vector3(face * 1.5f, 0f, 0f));
    }
}
