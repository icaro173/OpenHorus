using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;
using Random = UnityEngine.Random;

public class PlayerShootingScript : MonoBehaviour {
    public const float AimingTime = 0.75f;

    public int BurstCount = 8;
    public float ShotCooldown = 0.045f;
    public float ReloadTime = 0.45f;
    public float BurstSpread = 1.5f;
    public float ShotgunSpreadBase = 0.375f;
    public float ShotgunSpread = 10;
    public float ShotgunBulletSpeedMultiplier = 0.25f;
    public float ShotgunHomingSpeed = 0.675f;
    public float CannonChargeTime = 0.5f;
    public float HeatAccuracyFudge = 0.5f;

    public AudioSource reloadSound;
    public AudioSource targetSound;
    public AudioSource pepperGunSound;
    public AudioSource burstGunSound;

    public BulletScript bulletPrefab;
    public BulletScript cannonBulletPrefab;
    public Transform gun;

    public Texture2D cannonIndicator;
    public AnimationCurve cannonOuterScale;
    public AnimationCurve cannonInnerScale;

    Material mat;

    public float heat = 0.0f;
    float cooldownLeft = 0.0f;
    int bulletsLeft;

    WeaponIndicatorScript weaponIndicator;
    public List<WeaponIndicatorScript.PlayerData> targets;

    CameraScript playerCamera;
    PlayerScript playerScript;

    void Awake() {
        bulletsLeft = BurstCount;
        playerCamera = GetComponentInChildren<CameraScript>();
        weaponIndicator = Camera.main.GetComponent<WeaponIndicatorScript>();
        targets = weaponIndicator.Targets;
        playerScript = GetComponent<PlayerScript>();
    }

    WeaponIndicatorScript.PlayerData GetFirstTarget() {
        return targets
               .Where(x => x.SinceInCrosshair >= AimingTime)
               .OrderBy(x => Guid.NewGuid())
               .First();
    }

    void Update() {
        gun.LookAt(playerCamera.GetTargetPosition());

        if (playerScript.paused)
            bulletsLeft = BurstCount;

        if (networkView.isMine && Screen.lockCursor && !playerScript.paused) {
            cooldownLeft = Mathf.Max(0, cooldownLeft - Time.deltaTime);
            heat = Mathf.Clamp01(heat - Time.deltaTime);
            weaponIndicator.CooldownStep = 1 - Math.Min(Math.Max(cooldownLeft - ShotCooldown, 0) / ReloadTime, 1);

            if (cooldownLeft == 0) {
                // Shotgun
                if (Input.GetButton("Alternate Fire")) {
                    playerScript.health.ShotFired();

                    // find homing target(s)
                    IEnumerable<WeaponIndicatorScript.PlayerData> aimedAt = targets.Where(x => x.SinceInCrosshair >= AimingTime);

                    int bulletsShot = bulletsLeft;
                    bool first = true;
                    while (bulletsLeft > 0) {
                        if (!aimedAt.Any())
                            DoHomingShot(ShotgunSpread, null, 0, first);
                        else {
                            WeaponIndicatorScript.PlayerData pd = aimedAt.OrderBy(x => Guid.NewGuid()).First();
                            DoHomingShot(ShotgunSpread, pd.Script, Mathf.Clamp01(pd.SinceInCrosshair / AimingTime) * ShotgunHomingSpeed, first);
                        }

                        cooldownLeft += ShotCooldown;
                        first = false;
                    }
                    cooldownLeft += ReloadTime;

                    Vector3 recoilImpulse = -gun.forward * ((float)bulletsShot / BurstCount);
                    recoilImpulse *= playerScript.controller.isGrounded ? 25 : 87.5f;
                    recoilImpulse.y *= playerScript.controller.isGrounded ? 0.1f : 0.375f;
                    playerScript.AddRecoil(recoilImpulse);

                    //cannonChargeCountdown = CannonChargeTime;
                }

                // Burst
                else if (Input.GetButton("Fire")) // burst fire
                {
                    playerScript.health.ShotFired();

                    DoShot(BurstSpread);
                    cooldownLeft += ShotCooldown;
                    if (bulletsLeft <= 0)
                        cooldownLeft += ReloadTime;
                }

                if (bulletsLeft != BurstCount && Input.GetButton("Reload")) {
                    bulletsLeft = BurstCount;

                    reloadSound.Play();

                    cooldownLeft += ReloadTime;
                }
            }

            if (bulletsLeft <= 0) {
                bulletsLeft = BurstCount;
                reloadSound.Play();
            }

            Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
            float allowedDistance = 130 * Screen.height / 1500f;

            foreach (WeaponIndicatorScript.PlayerData v in targets) v.Found = false;

            // Test for players in crosshair
            foreach (PlayerScript player in FindObjectsOfType(typeof(PlayerScript))) {
                // Is targeting self?
                if (player == playerScript) continue;

                HealthScript health = player.GetComponent<HealthScript>();
                Vector3 position = player.transform.position;
                Vector3 screenPos = Camera.main.WorldToScreenPoint(position);

                if (health.Health > 0 && screenPos.z > 0 && (new Vector2(screenPos.x, screenPos.y) - screenCenter).magnitude < allowedDistance) {
                    WeaponIndicatorScript.PlayerData data;
                    if ((data = targets.FirstOrDefault(x => x.Script == player)) == null) {
                        targets.Add(data = new WeaponIndicatorScript.PlayerData { Script = player, WasLocked = false });
                    }

                    data.ScreenPosition = new Vector2(screenPos.x,screenPos.y);
                    data.SinceInCrosshair += Time.deltaTime;
                    data.Found = true;

                    if (!data.WasLocked && data.Locked) { // Send target notification
                        targetSound.Play();
                        data.Script.networkView.RPC("Targeted", RPCMode.All, playerScript.owner);
                    }
                }
            }
            CheckTargets();
        }
    }

    void OnApplicationQuit() {
        CheckTargets();
    }

    public void CheckTargets() {
        if (targets.Count > 0) {
            for (int i = 0; i < targets.Count; i++) {
                if (targets[i].Script != null) {
                    if (targets[i].WasLocked && !targets[i].Found)
                        targets[i].Script.networkView.RPC("Untargeted", RPCMode.All, playerScript.owner);
                    targets[i].WasLocked = targets[i].Locked;

                    if (!targets[i].Found || gameObject.GetComponent<HealthScript>().Health < 1 || targets[i].Script == null) // Is player in target list dead, or unseen? Am I dead?
                        targets.RemoveAt(i);
                } else {
                    targets.RemoveAt(i);
                }
            }
        }
    }

    void DoShot(float spread) {
        bulletsLeft -= 1;
        spread += heat * HeatAccuracyFudge;
        heat += 0.25f;

        float roll = Random.value * 360;
        Quaternion spreadRotation =
            Quaternion.Euler(0, 0, roll) *
            Quaternion.Euler(Random.value * spread, 0, 0) *
            Quaternion.Euler(0, 0, -roll);

        networkView.RPC("Shoot", RPCMode.All,
            gun.position + gun.forward * 4.0f, gun.rotation * spreadRotation,
            Network.player);
    }

    public void InstantReload() {
        bulletsLeft = BurstCount;
    }

    void DoHomingShot(float spread, PlayerScript target, float homing, bool doSound) {
        bulletsLeft -= 1;

        spread *= (ShotgunSpreadBase + homing * 5);

        float roll = RandomHelper.Between(homing * 90, 360 - homing * 90);
        Quaternion spreadRotation =
            Quaternion.Euler(0, 0, roll) *
            Quaternion.Euler(Random.value * spread, 0, 0) *
            Quaternion.Euler(0, 0, -roll);

        Vector3 lastKnownPosition = Vector3.zero;
        NetworkPlayer targetOwner = Network.player;
        if (target != null) {
            targetOwner = target.owner;
            lastKnownPosition = target.transform.position;
        }

        networkView.RPC("ShootHoming", RPCMode.All,
            gun.position + gun.forward * 4.0f, gun.rotation * spreadRotation,
            Network.player, targetOwner, lastKnownPosition, homing, doSound);
    }

    [RPC]
    void Shoot(Vector3 position, Quaternion rotation, NetworkPlayer player) {
        BulletScript bullet = (BulletScript)Instantiate(bulletPrefab, position, rotation);
        bullet.Player = player;
        burstGunSound.Play();
    }

    [RPC]
    void ShootHoming(Vector3 position, Quaternion rotation, NetworkPlayer player, NetworkPlayer target, Vector3 lastKnownPosition, float homing, bool doSound) {
        BulletScript bullet = (BulletScript)Instantiate(bulletPrefab, position, rotation);
        bullet.Player = player;

        PlayerScript targetScript;
        try {
            targetScript = FindObjectsOfType<PlayerScript>()
                .Where(x => x.owner == target)
                .OrderBy(x => Vector3.Distance(x.transform.position, lastKnownPosition))
                .FirstOrDefault();
        } catch (Exception) {
            targetScript = null;
        }

        bullet.target = targetScript == null ? null : targetScript.transform;
        bullet.homing = homing;
        bullet.speed *= ShotgunBulletSpeedMultiplier;
        bullet.recoil = 1;

        if (doSound) {
            pepperGunSound.Play();
        }
    }
}
