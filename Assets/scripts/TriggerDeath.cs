using UnityEngine;

public class TriggerDeath : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D other)
    {
        Debug.Log("TriggerDeath: OnTriggerEnter called with " + other.name);
        if (other.CompareTag("Player"))
        {
            // Call the Die method on the PlayerHealth script
            PlayerHealth playerHealth = other.GetComponent<PlayerHealth>();

            playerHealth.Die();

        }
    }
}
