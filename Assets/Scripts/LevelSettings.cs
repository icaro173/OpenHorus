using UnityEngine;

public class LevelSettings : MonoBehaviour {
    // Instance
    public static LevelSettings instance;

    // Level properties
    public Transform spectatorCameraPosition { get { return transform; } }
    public float killZ;

    void Awake() {
        instance = this;
    }

    void OnDestroy() {
        if (instance == this)
            instance = null;
    }
}