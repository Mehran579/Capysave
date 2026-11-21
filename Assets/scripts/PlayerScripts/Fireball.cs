// Straight-line projectile — spawned by PlayerCombat.
// Damages IDamageable on enemyLayer and dies on hit or on contact with destroyOnLayer.
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class Fireball : MonoBehaviour
{
    [Header("Movement")]
    [Min(0f)] [SerializeField] private float speed = 14f;
    [Min(0.05f)] [SerializeField] private float lifetime = 2f;

    [Header("Damage")]
    [Min(0)] [SerializeField] private int damage = 2;
    [Min(0f)] [SerializeField] private float knockbackForce = 6f;
    [Tooltip("Layers the fireball damages on contact.")]
    [SerializeField] private LayerMask enemyLayer;
    [Tooltip("Layers that cause the fireball to be destroyed on contact (walls, ground).")]
    [SerializeField] private LayerMask destroyOnLayer;

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

    // Called by PlayerCombat immediately after Instantiate.
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

    private void OnTriggerEnter2D(Collider2D other)
    {
        int otherBit = 1 << other.gameObject.layer;

        if ((otherBit & enemyLayer.value) != 0)
        {
            IDamageable dmg = other.GetComponentInParent<IDamageable>();
            if (dmg != null)
                dmg.TakeDamage(damage, new Vector2(facing, 0.1f).normalized * knockbackForce);
            Destroy(gameObject);
            return;
        }

        if ((otherBit & destroyOnLayer.value) != 0)
        {
            Destroy(gameObject);
        }
    }
}
