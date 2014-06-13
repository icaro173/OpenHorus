using System;
using UnityEngine;

public class CameraScript : MonoBehaviour {
    public Texture2D crosshair;
    public float collisionRadius = 0.7f;
    public float minDistance = 1;
    public float smoothing = 0.1f;

    bool aimingAtPlayer;
    PlayerScript player;

    Camera mainCamera;

    Quaternion actualCameraRotation;

    void Start() {
        player = transform.parent.parent.GetComponent<PlayerScript>();
        if (player.networkView.isMine) {
            mainCamera = Camera.main;
        }
    }

    void FixedUpdate() {
        RaycastHit hitInfo;

        player.gameObject.FindChild("PlayerHit").collider.enabled = false;

        aimingAtPlayer = Physics.Raycast(transform.position, transform.forward, out hitInfo,
                                             Mathf.Infinity, (1 << LayerMask.NameToLayer("Default")) |
                                                             (1 << LayerMask.NameToLayer("Player Hit"))) &&
                             hitInfo.transform.gameObject.layer == LayerMask.NameToLayer("Player Hit");

        player.gameObject.FindChild("PlayerHit").collider.enabled = true;
    }

    void LateUpdate() {
        if (player.Paused && mainCamera != null) {
            mainCamera.transform.localPosition = new Vector3(-85.77416f, 32.8305f, -69.88891f);
            mainCamera.transform.localRotation = Quaternion.Euler(16.48679f, 21.83607f, 6.487632f);
            return;
        }

        if (player.networkView.isMine) {

            actualCameraRotation = Quaternion.Lerp(transform.rotation, actualCameraRotation,
                Easing.EaseOut(Mathf.Pow(smoothing, Time.deltaTime), EasingType.Quadratic));

            Vector3 scaledLocalPosition = Vector3.Scale(transform.localPosition, transform.lossyScale);
            Vector3 direction = actualCameraRotation * scaledLocalPosition;
            Vector3 cameraPosition = transform.parent.position + direction;
            float magnitude = direction.magnitude;
            direction /= magnitude;

            mainCamera.transform.position = cameraPosition;
            mainCamera.transform.rotation = actualCameraRotation;


            Camera.main.GetComponent<WeaponIndicatorScript>()
                .CrosshairPosition = GetCrosshairPosition();
        }
    }

    void Render(float size, Color color) {
        float scale = (Screen.height / 1750f) * size;

        Vector2 center = GetCrosshairPosition();
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
        if (Physics.Raycast(transform.position, transform.forward, out hitInfo,
                           Mathf.Infinity, 1 << LayerMask.NameToLayer("Default")))
            return hitInfo.point;
        else
            return transform.position + transform.forward * 1000;
    }
}
