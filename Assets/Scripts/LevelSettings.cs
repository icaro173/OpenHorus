using UnityEngine;

public class LevelSettings : MonoBehaviour {
    // Instance
    public static LevelSettings instance;

    // Level properties
    public float killZ;
    public Vector3 orbitPositionOffset;
    public Vector3 orbitRotationOffset;

    void Awake() {
        instance = this;
    }

    void OnDestroy() {
        if (instance == this)
            instance = null;
    }
}