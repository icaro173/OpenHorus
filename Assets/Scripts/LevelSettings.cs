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

    void Start() {
        // Set orbiting camera position
        if (ServerScript.Instance != null) {
            GameObject orbit = FindObjectOfType<CameraSpin>().gameObject;
            orbit.transform.position = spectatorCameraPosition.position;
            orbit.transform.rotation = spectatorCameraPosition.rotation;
        }

        // Are we starting this level directly? Do QuickPlay
        //if (Application.isEditor && ServerScript.Instance == null) {
            //todo Quick play here
        //}
    }

}