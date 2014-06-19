using UnityEngine;

public class PlayerDeathScript : MonoBehaviour {
    public AudioClip death;
    public AudioClip waterDeath;

    ParticleSystem p;

    void Awake() {
        p = GetComponentInChildren<ParticleSystem>();
    }

    void Update() {
        if (!p.IsAlive()) {
            Destroy(gameObject);
        }
    }
}
