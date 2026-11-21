using UnityEngine;

// Anything the player (or player projectiles) can damage implements this.
public interface IDamageable
{
    void TakeDamage(int amount, Vector2 hitDirection);
}
