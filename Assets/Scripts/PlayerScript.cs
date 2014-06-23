using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(CharacterController))]
public class PlayerScript : MonoBehaviour {
    const float JumpInputQueueTime = 0.2f;

    // Tunable
    public float speed = 10;
    public float mouseSensitivity = 1.5f;
    public float lookAngleLimit = 80;
    public float gravity = -100;
    public float jumpVelocity = 65;
    public float timeBetweenDashes = 1;
    public float dashForwardVelocity = 70;
    public float dashUpwardVelocity = 30;
    public float airVelocityDamping = 0.05f;    // air velocity damping: 0.05f -> speed drops to 5% in one second
    public float recoilDamping = 0.0005f;       // TODO: Fix! recoil works bad with lag
    public float IdleTransitionFadeLength = 1.0f;

    public bool paused { get; private set; }

    //References
    public Transform cameraPivot;
    public Transform dashEffectPivot;
    public Renderer dashEffectRenderer;
    public CharacterController controller;
    public GameObject warningSphereFab;
    public HealthScript health;

    //Audio
    public AudioSource warningSound;
    public AudioSource dashSound;
    public AudioSource landingSound;
    public AudioSource jumpSound;

    //Private
    public NetworkPlayer owner;
    private GameObject textBubble;
    private Vector3 fallingVelocity;
    private Vector3 lastFallingVelocity;
    private Vector3 recoilVelocity;
    private bool invertMouse = true;
    private Vector3 inputVelocity;
    private Vector3 lastInputVelocity;
    private Vector3 lookRotationEuler;
    private float lastJumpInputTime = -1;
    private float dashCooldown = 0;
    private Animation characterAnimation;
    private string currentAnim;
    private float sinceNotGrounded;
    private bool activelyJumping;
    private bool textBubbleVisible;
    private bool playJumpSound = false;
    private bool playDashSound = false;
    private int jumpsSinceGrounded = 0;
    private Quaternion smoothLookRotation;
    private float smoothYaw;
    private List<GameObject> warningSpheres;
    private VectorInterpolator iPosition;
    private Vector3 lastNetworkFramePosition;

    void Awake() {
        warningSpheres = new List<GameObject>();

        //Find player parts
        controller = GetComponent<CharacterController>();
        characterAnimation = transform.Find("Animated Mesh Fixed").animation;
        characterAnimation.Play(currentAnim = "idle");
        textBubble = gameObject.transform.Find("TextBubble").gameObject;
        textBubble.renderer.material.color = new Color(1, 1, 1, 0);
        health = gameObject.GetComponent<HealthScript>();

        // Set animation speeds
        characterAnimation["run"].speed = 1.25f;
        characterAnimation["backward"].speed = 1.75f;
        characterAnimation["strafeLeft"].speed = 1.5f;
        characterAnimation["strafeRight"].speed = 1.5f;
    }

    void OnDestroy() {
        PlayerRegistry.Instance.UnregisterPlayer(owner);
    }

    void OnNetworkInstantiate(NetworkMessageInfo info) {
        Debug.Log("Player object instantiated by " + info.sender);

        // Invalidate owner
        owner = new NetworkPlayer();

        if (!networkView.isMine) {
            enabled = false;
            iPosition = new VectorInterpolator();
        } else {
            gameObject.layer = LayerMask.NameToLayer("LocalPlayer");
        }
    }

    IEnumerator WaitAndLabel() {
        while (!PlayerRegistry.Has(owner))
            yield return new WaitForSeconds(1 / 30f);
        UpdateLabel(PlayerRegistry.Get(owner).Username);
    }

    void OnGUI() {
        if (Event.current.type == EventType.KeyDown &&
           Event.current.keyCode == KeyCode.Escape) {
            Screen.lockCursor = false;
        }
    }

    [RPC]
    private void setOwner(NetworkPlayer player) {
        Debug.Log("Player object owned by " + player);
        owner = player;

        // Since we now have a valid owner id for this object, start checking the label
        // TODO replace with a callback when the object is registered
        if (!networkView.isMine) {
            StartCoroutine(WaitAndLabel());
        }
    }

    [RPC]
    public void Targeted(NetworkPlayer aggressor) {
        if (!networkView.isMine) return;

        warningSound.Play();

        GameObject sphere = (GameObject)Instantiate(warningSphereFab, transform.position, transform.rotation);
        sphere.transform.parent = gameObject.transform;
        sphere.GetComponent<Billboard>().target = PlayerRegistry.Get(aggressor).Player.transform;

        warningSpheres.Add(sphere);
    }

    [RPC]
    public void Untargeted(NetworkPlayer aggressor) {
        if (!networkView.isMine) return;

        int id = -1;
        id = warningSpheres.FindIndex(a => a.GetComponent<Billboard>().target == PlayerRegistry.Get(aggressor).Player.transform);
        if (id == -1) return;

        Destroy(warningSpheres[id]);
        warningSpheres.RemoveAt(id);
    }

    [RPC]
    private void setPaused(bool status) {
        paused = status;
    }

    public void ResetWarnings() {
        if (!networkView.isMine) return;

        for (int i = 0; i < warningSpheres.Count; i++) Destroy(warningSpheres[i]);
        warningSpheres.Clear();
    }

    public void AddRecoil(Vector3 impulse) {
        recoilVelocity += impulse;
        if (impulse.y > 0)
            sinceNotGrounded = 0.25f;
    }

    public void ResetVelocities() {
        if (!networkView.isMine) return;
        recoilVelocity = Vector3.zero;
        fallingVelocity = Vector3.zero;
    }

    private Vector3 RawAxisMovementDirection {
        get {
            return (Input.GetAxisRaw("Strafe") * transform.right +
                    Input.GetAxisRaw("Thrust") * transform.forward).normalized;
        }
    }

    public void UpdateLabel(string username) {
        TextMesh label = GetComponentInChildren<TextMesh>();
        label.text = username;
    }

    void Update() {
        if (Network.peerType == NetworkPeerType.Disconnected) return;
        if (paused) return;

        if (networkView.isMine) {
            textBubbleVisible = ChatScript.Instance.showChat;

            inputVelocity =
                Input.GetAxis("Strafe") * transform.right +
                Input.GetAxis("Thrust") * transform.forward;
            if (inputVelocity.sqrMagnitude > 1)
                inputVelocity.Normalize();

            inputVelocity *= speed;

            if (Input.GetButtonDown("Jump") && fallingVelocity.y <= 2 && !(sinceNotGrounded > 0 && jumpsSinceGrounded > 1)) {
                jumpsSinceGrounded++;
                lastJumpInputTime = Time.time;
            }

            if (!Input.GetButton("Jump")) {
                activelyJumping = false;
                if (fallingVelocity.y > 2)
                    fallingVelocity.y = 2;
            }

            if (Screen.lockCursor) {
                float invertMultiplier = invertMouse ? -1 : 1;
                lookRotationEuler += MouseSensitivityScript.Sensitivity * new Vector3(
                    Input.GetAxis("Vertical Look") * invertMultiplier,
                    Input.GetAxis("Horizontal Look"),
                    0);
            }

            lookRotationEuler.x = Mathf.Clamp(
                lookRotationEuler.x, -lookAngleLimit, lookAngleLimit);

            if (Input.GetKeyDown("i"))
                invertMouse = !invertMouse;

            if (Input.GetMouseButtonUp(0))
                Screen.lockCursor = true;

            Screen.showCursor = !Screen.lockCursor;
            smoothYaw = lookRotationEuler.y;
            smoothLookRotation = Quaternion.Euler(lookRotationEuler);

        } else {
            if (iPosition.IsRunning) {
                transform.position += iPosition.Update();
            }

            smoothYaw = Mathf.LerpAngle(smoothYaw, lookRotationEuler.y, 0.4f);
            smoothLookRotation = Quaternion.Slerp(smoothLookRotation, Quaternion.Euler(lookRotationEuler), 0.3f);
        }

        // set up text bubble visibility
        if (!textBubbleVisible) {
            float o = textBubble.renderer.material.color.a;
            textBubble.renderer.material.color = new Color(1, 1, 1, Mathf.Clamp(o - Time.deltaTime * 10, 0, 0.875f));
            if (o <= 0)
                textBubble.renderer.enabled = false;
        } else {
            textBubble.renderer.enabled = true;
            float o = textBubble.renderer.material.color.a;
            textBubble.renderer.material.color = new Color(1, 1, 1, Mathf.Clamp(o + Time.deltaTime * 10, 0, 0.875f));
        }
        textBubble.transform.LookAt(Camera.main.transform);
        textBubble.transform.localRotation = textBubble.transform.localRotation * Quaternion.Euler(90, 0, 0);

        // sync up actual player and camera transforms
        Vector3 euler = transform.rotation.eulerAngles;
        euler.y = smoothYaw;
        transform.rotation = Quaternion.Euler(euler);
        cameraPivot.rotation = smoothLookRotation;

        // dash animation
        Color color = dashEffectRenderer.material.GetColor("_TintColor");
        Vector3 dashVelocity = new Vector3(fallingVelocity.x, activelyJumping ? 0 : Math.Max(fallingVelocity.y, 0), fallingVelocity.z);
        if (dashVelocity.magnitude > 1 / 256.0) {
            color.a = dashVelocity.magnitude / dashForwardVelocity / 8;
            dashEffectPivot.LookAt(transform.position + dashVelocity.normalized);
        } else {
            color.a = 0;
        }
        dashEffectRenderer.material.SetColor("_TintColor", color);
    }

    void FixedUpdate() {
        if (!controller.enabled) return;
        if (paused) return;

        Vector3 smoothedInputVelocity = inputVelocity * 0.6f + lastInputVelocity * 0.45f;
        lastInputVelocity = smoothedInputVelocity;

        // jump and dash
        dashCooldown -= Time.fixedDeltaTime;
        bool justJumped = false;
        if (networkView.isMine && Time.time - lastJumpInputTime <= JumpInputQueueTime) {
            if ((controller.isGrounded || sinceNotGrounded < 0.25f) && recoilVelocity.y <= 0) {
                lastJumpInputTime = -1;
                justJumped = true;
                activelyJumping = true;
                fallingVelocity.y = jumpVelocity;
                characterAnimation.CrossFade(currentAnim = "jump", IdleTransitionFadeLength);
                playJumpSound = true;

                jumpSound.Play();

                sinceNotGrounded = 0.25f;
            } else if (dashCooldown <= 0) {
                activelyJumping = false;
                lastJumpInputTime = -1;
                dashCooldown = timeBetweenDashes;

                if (currentAnim == "jump")
                    characterAnimation.Rewind("jump");
                characterAnimation.CrossFade(currentAnim = "jump", IdleTransitionFadeLength);
                playDashSound = true;

                dashSound.Play();

                Vector3 dashDirection = RawAxisMovementDirection;
                if (dashDirection.magnitude < Mathf.Epsilon)
                    dashDirection = Vector3.up * 0.4f;

                fallingVelocity +=
                    dashDirection * dashForwardVelocity +
                    Vector3.up * dashUpwardVelocity;

                recoilVelocity.y *= 0.5f;
            }
        }

        if (controller.isGrounded) {
            if (!justJumped) {
                sinceNotGrounded = 0;
                jumpsSinceGrounded = 0;
            }
            // infinite friction
            if (fallingVelocity.y <= 0)
                fallingVelocity = Vector3.up * gravity * Time.fixedDeltaTime;
        } else {
            sinceNotGrounded += Time.fixedDeltaTime;
            // air drag / gravity
            fallingVelocity.y += gravity * Time.fixedDeltaTime;
            fallingVelocity.x *= Mathf.Pow(airVelocityDamping, Time.fixedDeltaTime);
            fallingVelocity.z *= Mathf.Pow(airVelocityDamping, Time.fixedDeltaTime);
        }

        // Update running animation
        if (controller.isGrounded && !justJumped) {
            if (MathHelper.AlmostEquals(smoothedInputVelocity, Vector3.zero, 0.1f) && currentAnim != "idle")
                characterAnimation.CrossFade(currentAnim = "idle", IdleTransitionFadeLength);
            else {
                float xDir = Vector3.Dot(smoothedInputVelocity, transform.right);
                float zDir = Vector3.Dot(smoothedInputVelocity, transform.forward);

                const float epsilon = 15f;

                if (zDir > epsilon) {
                    if (currentAnim != "run")
                        characterAnimation.CrossFade(currentAnim = "run", IdleTransitionFadeLength);
                } else if (zDir < -epsilon) {
                    if (currentAnim != "backward")
                        characterAnimation.CrossFade(currentAnim = "backward", IdleTransitionFadeLength);
                } else if (xDir > epsilon) {
                    if (currentAnim != "strafeRight")
                        characterAnimation.CrossFade(currentAnim = "strafeRight", IdleTransitionFadeLength);
                } else if (xDir < -epsilon) {
                    if (currentAnim != "strafeLeft")
                        characterAnimation.CrossFade(currentAnim = "strafeLeft", IdleTransitionFadeLength);
                }
            }
        }

        Vector3 smoothFallingVelocity = fallingVelocity * 0.4f + lastFallingVelocity * 0.65f;
        lastFallingVelocity = smoothFallingVelocity;

        // damp recoil
        if (!controller.isGrounded) {
            recoilVelocity.x *= Mathf.Pow(recoilDamping * 10, Time.fixedDeltaTime);
            recoilVelocity.y *= Mathf.Pow(recoilDamping * 100, Time.fixedDeltaTime);
            recoilVelocity.z *= Mathf.Pow(recoilDamping * 10, Time.fixedDeltaTime);
        } else {
            recoilVelocity.x *= Mathf.Pow(recoilDamping * 0.04f, Time.fixedDeltaTime);
            recoilVelocity.y *= Mathf.Pow(recoilDamping * 100f, Time.fixedDeltaTime);
            recoilVelocity.z *= Mathf.Pow(recoilDamping * 0.04f, Time.fixedDeltaTime);
        }

        // move!
        controller.Move((smoothFallingVelocity + smoothedInputVelocity + recoilVelocity) * Time.fixedDeltaTime);

        if (sinceNotGrounded > 0.25f && controller.isGrounded) {
            landingSound.Play();
        }

        if (controller.isGrounded)
            recoilVelocity.y = 0;
    }

    // Used by HealthScript in Respawn
    public void ResetAnimation() {
        characterAnimation.Play(currentAnim = "idle");
        lastInputVelocity = inputVelocity = Vector3.zero;
    }

    void OnSerializeNetworkView(BitStream stream, NetworkMessageInfo info) {
        Vector3 pPosition = stream.isWriting ? transform.position : Vector3.zero;

        stream.Serialize(ref pPosition);
        stream.Serialize(ref lookRotationEuler);
        stream.Serialize(ref inputVelocity);
        stream.Serialize(ref fallingVelocity);
        stream.Serialize(ref activelyJumping);
        stream.Serialize(ref recoilVelocity);
        stream.Serialize(ref textBubbleVisible);
        stream.Serialize(ref playDashSound);
        stream.Serialize(ref playJumpSound);

        if (stream.isReading) {
            if (lastNetworkFramePosition == pPosition)
                transform.position = pPosition;

            if (!iPosition.Start(pPosition - transform.position))
                transform.position = pPosition;
            // Play sounds
            if (playDashSound) dashSound.Play();
            if (playJumpSound) jumpSound.Play();
            lastNetworkFramePosition = pPosition;
        }

        playJumpSound = playDashSound = false;
    }
}

