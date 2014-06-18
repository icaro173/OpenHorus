using UnityEngine;

public class CameraSpin : MonoBehaviour {
    public float rotateSpeed = 0.2f;
    public int sign = 1;

    Vector3 camPosOrigin, transPosOrigin;
    Quaternion camRotOrigin, transRotOrigin;
    bool wasSpectating;

    void Start() {
        DontDestroyOnLoad(gameObject);
        Camera.main.depthTextureMode = DepthTextureMode.DepthNormals;
    }

    void OnLevelWasLoaded() {
        if (ServerScript.hostState == ServerScript.HostingState.WaitingForInput) {
            ResetTransforms();
        }
    }

    void Update() {
        //todo This is not how you do lerping, change it to a proper, nice transition
        if (transform.localEulerAngles.y > 150) sign *= -1;
        if (transform.localEulerAngles.y < 100) sign *= -1;

        transform.Rotate(0, rotateSpeed * Time.deltaTime * sign, 0);

        if (ServerScript.Spectating) {
            if (!wasSpectating) {
                ResetTransforms();
                wasSpectating = true;
            }
        } else {
            wasSpectating = false;
        }

    }

    void OnDisconnectedFromServer(NetworkDisconnection mode) {
        ResetTransforms();
    }

    public void ResetTransforms() {
        Debug.Log("Resetting transforms");
        transform.position = LevelSettings.instance.spectatorCameraPosition.position;
        transform.rotation = LevelSettings.instance.spectatorCameraPosition.rotation;

        Camera.main.transform.localPosition = new Vector3(-85.77416f, 32.8305f, -69.88891f);
        Camera.main.transform.localRotation = Quaternion.Euler(16.48679f, 21.83607f, 6.487632f);
    }

}
