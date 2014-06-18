using UnityEngine;

public class ParticleSystemAutoDestructScript : MonoBehaviour {
    void Update() {
        if (!particleSystem.IsAlive()) {
            Object.Destroy(gameObject);
        }
    }
}
