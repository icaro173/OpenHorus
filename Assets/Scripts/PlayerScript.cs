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

    public bool Paused;

    //References
    public Transform cameraPivot;
    public Transform dashEffectPivot;
    public Renderer dashEffectRenderer;
    public CharacterController controller;
    public GameObject warningSphereFab;
    public NetworkPlayer owner;

    //Audio
    public AudioSource warningSound;
    public AudioSource dashSound;
    public AudioSource landingSound;
    public AudioSource jumpSound;
    
    //Private
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

    void Awake() {
        warningSpheres = new List<GameObject>();

        //Find player parts
        controller = GetComponent<CharacterController>();
        characterAnimation = transform.Find("Animated Mesh Fixed").animation;
        characterAnimation.Play(currentAnim = "idle");
        textBubble = gameObject.FindChild("TextBubble");
        textBubble.renderer.material.color = new Color(1, 1, 1, 0);

        // Set animation speeds
        characterAnimation["run"].speed = 1.25f;
        characterAnimation["backward"].speed = 1.75f;
        characterAnimation["strafeLeft"].speed = 1.5f;
        characterAnimation["strafeRight"].speed = 1.5f;

        // Tag materials
        /*foreach (Renderer r in GetComponentsInChildren<Renderer>()) {
            if (!r.material.HasProperty("_Color")) continue;
            if (r.gameObject.name == "TextBubble") continue;
            if (r.gameObject.name == "flag_flag001") continue;
            r.tag = "PlayerMaterial";
        }*/
    }

    void OnNetworkInstantiate(NetworkMessageInfo info) {
        if (!networkView.isMine) {
            owner = networkView.owner;
            StartCoroutine(WaitAndLabel());
            enabled = false;
        } else {
            owner = Network.player;
            gameObject.layer = LayerMask.NameToLayer("LocalPlayer");
        }
    }

    IEnumerator WaitAndLabel() {
        while (!PlayerRegistry.Has(owner))
            yield return new WaitForSeconds(1 / 30f);
        UpdateLabel(PlayerRegistry.For(owner).Username);
    }

    void OnGUI() {
        if (Event.current.type == EventType.KeyDown &&
           Event.current.keyCode == KeyCode.Escape) {
            Screen.lockCursor = false;
        }
    }

    [RPC]
    public void Targeted(NetworkPlayer aggressor) {
        if (!networkView.isMine) return;

        if (GlobalSoundsScript.soundEnabled)
            warningSound.Play();

        print("Targeted by: " + PlayerRegistry.For(aggressor).Username);

        GameObject sphere = (GameObject)Instantiate(warningSphereFab, transform.position, transform.rotation);
        sphere.transform.parent = gameObject.transform;
        sphere.GetComponent<Billboard>().target = PlayerRegistry.For(aggressor).Location;

        warningSpheres.Add(sphere);
    }

    [RPC]
    public void Untargeted(NetworkPlayer aggressor) {
        if (!networkView.isMine) return;

        print("Untargeted by: " + PlayerRegistry.For(aggressor).Username);

        int id = -1;
        id = warningSpheres.FindIndex(a => a.GetComponent<Billboard>().target == PlayerRegistry.For(aggressor).Location);
        if (id == -1) return;

        Destroy(warningSpheres[id]);
        warningSpheres.RemoveAt(id);
    }

    public void ResetWarnings() {
        if (!networkView.isMine) return;

        for (int i = 0; i < warningSpheres.Count; i++) Destroy(warningSpheres[i]);
        warningSpheres.Clear();
    }

    [RPC]
    public void AddRecoil(Vector3 impulse) {
        if (!networkView.isMine) return;
        recoilVelocity += impulse;
        if (impulse.y > 0)
            sinceNotGrounded = 0.25f;
    }

    public void ResetVelocities() {
        if (!networkView.isMine) return;
        recoilVelocity = Vector3.zero;
        fallingVelocity = Vector3.zero;
    }

    public void UpdateLabel(string username) {
        TextMesh label = GetComponentInChildren<TextMesh>();
        label.text = username;
    }

    void Update() {
        if (Network.peerType == NetworkPeerType.Disconnected) return;
        if (Paused) return;

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

            //Copy
            Vector3 euler = transform.rotation.eulerAngles;
            euler.y = smoothYaw;
            transform.rotation = Quaternion.Euler(euler);
            cameraPivot.rotation = smoothLookRotation;
        } else {
            //if (iPosition.IsRunning) {
                //transform.position += iPosition.Update();
            //}

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
        if (Paused) return;

        Vector3 smoothedInputVelocity = inputVelocity * 0.6f + lastInputVelocity * 0.45f;
        lastInputVelocity = smoothedInputVelocity;

        // jump and dash
        dashCooldown -= Time.deltaTime;
        bool justJumped = false;
        if (networkView.isMine && Time.time - lastJumpInputTime <= JumpInputQueueTime) {
            if ((controller.isGrounded || sinceNotGrounded < 0.25f) && recoilVelocity.y <= 0) {
                lastJumpInputTime = -1;
                justJumped = true;
                activelyJumping = true;
                fallingVelocity.y = jumpVelocity;
                characterAnimation.CrossFade(currentAnim = "jump", IdleTransitionFadeLength);
                playJumpSound = true;

                if (GlobalSoundsScript.soundEnabled)
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

                if (GlobalSoundsScript.soundEnabled) {
                    dashSound.Play();
                }

                Vector3 dashDirection = inputVelocity.normalized;
                if (dashDirection == Vector3.zero)
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
                fallingVelocity = Vector3.up * gravity * Time.deltaTime;
        } else {
            sinceNotGrounded += Time.deltaTime;
            // air drag / gravity
            fallingVelocity.y += gravity * Time.deltaTime;
            fallingVelocity.x *= Mathf.Pow(airVelocityDamping, Time.deltaTime);
            fallingVelocity.z *= Mathf.Pow(airVelocityDamping, Time.deltaTime);
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
            recoilVelocity.x *= Mathf.Pow(recoilDamping * 10, Time.deltaTime);
            recoilVelocity.y *= Mathf.Pow(recoilDamping * 100, Time.deltaTime);
            recoilVelocity.z *= Mathf.Pow(recoilDamping * 10, Time.deltaTime);
        } else {
            recoilVelocity.x *= Mathf.Pow(recoilDamping * 0.04f, Time.deltaTime);
            recoilVelocity.y *= Mathf.Pow(recoilDamping * 100f, Time.deltaTime);
            recoilVelocity.z *= Mathf.Pow(recoilDamping * 0.04f, Time.deltaTime);
        }

        // move!
        controller.Move((smoothFallingVelocity + smoothedInputVelocity + recoilVelocity) * Time.deltaTime);

        if (sinceNotGrounded > 0.25f && controller.isGrounded) {
            if (GlobalSoundsScript.soundEnabled) {
                landingSound.Play();
            }
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
        NetworkPlayer pOwner = owner;
        stream.Serialize(ref pOwner);
        if (stream.isReading) owner = pOwner;

        stream.Serialize(ref inputVelocity);
        stream.Serialize(ref fallingVelocity);
        stream.Serialize(ref activelyJumping);
        stream.Serialize(ref recoilVelocity);
        stream.Serialize(ref textBubbleVisible);
        stream.Serialize(ref playDashSound);
        stream.Serialize(ref playJumpSound);

        if (stream.isReading) {
            // Play sounds
            if (playDashSound && GlobalSoundsScript.soundEnabled) dashSound.Play();
            if (playJumpSound && GlobalSoundsScript.soundEnabled) jumpSound.Play();
        }

        playJumpSound = playDashSound = false;
    }
}

