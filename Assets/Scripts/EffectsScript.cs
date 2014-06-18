using UnityEngine;

public class EffectsScript : MonoBehaviour {
    public static EffectsScript Instance { get; private set; }

    public GameObject explosionPrefab;
    public GameObject explosionHitPrefab;
    public GameObject areaExplosionPrefab;
    public GameObject hitConePrefab;

    void Awake() {
        Instance = this;
    }

    public static void Explosion(Vector3 position, Quaternion rotation) {
        GameObject exp = (GameObject)Instantiate(Instance.explosionPrefab, position, rotation);

        exp.GetComponent<AudioSource>().mute = false;

        int count = RandomHelper.Random.Next(1, 4);
        for (int i = 0; i < count; i++)
            Instantiate(Instance.hitConePrefab, position, rotation);
    }

    public static void ExplosionHit(Vector3 position, Quaternion rotation) {
        Instantiate(Instance.explosionHitPrefab, position, rotation);

        int count = RandomHelper.Random.Next(1, 4);
        for (int i = 0; i < count; i++)
            Instantiate(Instance.hitConePrefab, position, rotation);
    }

    public static void ExplosionArea(Vector3 position, Quaternion rotation) {
        Instantiate(Instance.areaExplosionPrefab, position, rotation);
    }

    public static void ExplosionHitArea(Vector3 position, Quaternion rotation) {
        ExplosionArea(position, rotation);
    }
}
