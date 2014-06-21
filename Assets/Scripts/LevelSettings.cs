using UnityEngine;

public class LevelSettings : MonoBehaviour {
    // Instance
    public static LevelSettings Instance { get; private set; }

    // Level properties
    public float killZ;
    public Vector3 orbitPositionOffset;
    public Vector3 orbitRotationOffset;

    void Awake() {
        Instance = this;
    }

    void OnDestroy() {
        if (Instance == this)
            Instance = null;
    }
}