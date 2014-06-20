using UnityEngine;

public class CameraSpin : MonoBehaviour {
    public float rotateSpeed = 0.2f;
    public int sign = 1;

    bool wasSpectating = true;

    public Transform center;
    public Vector3 axis = Vector3.up;

    void Start() {
        DontDestroyOnLoad(gameObject);
        Camera.main.depthTextureMode = DepthTextureMode.DepthNormals;

        // If we're hand-tuning the orbit
        if (Application.isEditor && ServerScript.Instance == null)
            ResetTransforms();
    }

    void OnLevelWasLoaded() {
        ResetTransforms();
        if (ServerScript.hostState == ServerScript.HostingState.WaitingForInput) {
            ResetTransforms();
        }
    }

    void Update() {
        if (center == null) return;
        transform.RotateAround(center.position, axis, rotateSpeed * Time.deltaTime);

        if (ServerScript.Spectating && !wasSpectating) {
            ResetTransforms();
        }

        wasSpectating = ServerScript.Spectating;
    }

    void OnDisconnectedFromServer(NetworkDisconnection mode) {
        ResetTransforms();
    }

    public void ResetTransforms() {
        if (LevelSettings.instance == null) return;
        center = LevelSettings.instance.transform;
        transform.position = center.position;
        transform.rotation = center.rotation;
        Camera.main.transform.localPosition = LevelSettings.instance.orbitPositionOffset;
        Camera.main.transform.localRotation = Quaternion.Euler(LevelSettings.instance.orbitRotationOffset);
    }
}