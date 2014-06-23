using UnityEngine;

public class CameraScript : MonoBehaviour {
    public Texture2D crosshair;
    public float collisionRadius = 0.7f;
    public float minDistance = 1;
    public float smoothing = 0.1f;

    public bool hasSmoothedRotation = true;
    public bool usesRaycastCrosshair = true;
    public float crosshairSmoothingSpeed = 8.5f;

    bool aimingAtPlayer;
    bool resetDone;
    PlayerScript player;

    Camera mainCamera;
    WeaponIndicatorScript weaponIndicator;
    CameraSpin orbitCamera;

    Quaternion actualCameraRotation;

    // Used only for drawing the crosshair on screen.
    // Actual aiming raycast will not use this.
    private Vector2 smoothedCrosshairPosition;

    void Start() {
        player = transform.parent.parent.GetComponent<PlayerScript>();
        if (player.networkView.isMine) {
            mainCamera = Camera.main;
        }
        resetDone = false;
        weaponIndicator = Camera.main.GetComponent<WeaponIndicatorScript>();
        orbitCamera = FindObjectOfType<CameraSpin>();
        smoothedCrosshairPosition = GetCrosshairPosition();
    }

    void FixedUpdate() {
        int layerMask = (1 << LayerMask.NameToLayer("Player Hit"));
        aimingAtPlayer = Physics.Raycast(transform.position, transform.forward, Mathf.Infinity, layerMask);
    }

    void LateUpdate() {
        if (player.paused && mainCamera != null) {
            if (!resetDone) {
                orbitCamera.ResetTransforms();
            }
            resetDone = true;
            return;
        }

        if (resetDone) resetDone = false;

        if (player.networkView.isMine) {

            if (hasSmoothedRotation) {
                //actualCameraRotation = Quaternion.Lerp(transform.rotation, actualCameraRotation,
                //    Easing.EaseOut(Mathf.Pow(smoothing, Time.deltaTime), EasingType.Quadratic));
                float amt = Mathf.Pow(0.0000000000001f, Time.deltaTime);
                actualCameraRotation = Quaternion.Slerp(actualCameraRotation, transform.rotation, 1.0f - amt);
            } else {
                actualCameraRotation = transform.rotation;
            }

            Vector3 scaledLocalPosition = Vector3.Scale(transform.localPosition, transform.lossyScale);
            Vector3 direction = actualCameraRotation * scaledLocalPosition;
            Vector3 cameraPosition = transform.parent.position + direction;
            float magnitude = direction.magnitude;
            direction /= magnitude;

            mainCamera.transform.position = cameraPosition;
            mainCamera.transform.rotation = actualCameraRotation;

            Vector2 rawCrosshairPosition = GetCrosshairPosition();
            smoothedCrosshairPosition = Vector2.Lerp(smoothedCrosshairPosition, rawCrosshairPosition,
                1.0f - Mathf.Pow(crosshairSmoothingSpeed, -crosshairSmoothingSpeed * Time.deltaTime));
            weaponIndicator.CrosshairPosition = smoothedCrosshairPosition;
        }
    }

    void Render(float size, Color color) {
        float scale = (Screen.height / 1750f) * size;

        Vector2 center = smoothedCrosshairPosition;
        Rect position = new Rect(
            center.x - crosshair.width / 2f * scale,
            Screen.height - center.y - crosshair.height / 2f * scale,
            crosshair.width * scale,
            crosshair.height * scale);

        GUI.color = color;
        GUI.DrawTexture(position, crosshair);
    }

    void OnGUI() {
        if (player.networkView.isMine) {
            Color color = Color.white;
            if (aimingAtPlayer)
                GUI.color = Color.red;
            Render(1.0f, color);
            Render(0.75f, Color.black);
        }
    }

    public Vector2 GetCrosshairPosition() {
        return Camera.main.WorldToScreenPoint(GetTargetPosition());
    }

    public Vector3 GetTargetPosition() {
        RaycastHit hitInfo;
        Transform origin = usesRaycastCrosshair ? transform : mainCamera.transform;
        if (Physics.Raycast(origin.position, origin.forward, out hitInfo,
                           Mathf.Infinity, 1 << LayerMask.NameToLayer("Default"))) {
            return hitInfo.point;
        } else {
            return transform.position + transform.forward * 1000;
        }
    }
}
