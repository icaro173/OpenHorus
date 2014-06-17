using System.Linq;
using UnityEngine;
using System.Collections;

public class HealthScript : MonoBehaviour {
    public int maxShield = 1;
    public int maxHealth = 2;
    public float shieldRegenTime = 5;
    public float invulnerabilityTime = 2;
    public GameObject deathPrefab;
    bool dead;

    readonly static Color DefaultShieldColor = new Color(110 / 255f, 190 / 255f, 255 / 255f, 30f / 255f);
    readonly static Color HitShieldColor = new Color(1, 1, 1, 1f); // new Color(1, 0, 0, 1f);
    readonly static Color RecoverShieldColor = new Color(1, 1, 1, 1f);

    public int Shield { get; private set; }
    public int Health { get; private set; }

    public Renderer shieldRenderer;

    float timeUntilShieldRegen;
    float timeSinceRespawn;
    public float timeUntilRespawn = 5;

    bool invulnerable;
    bool firstSet;

    Renderer bigCell;
    Renderer[] smallCells;

    void Awake() {
        Shield = maxShield;
        Health = maxHealth;

        GameObject graphics = gameObject.FindChild("Animated Mesh Fixed");
        bigCell = graphics.FindChild("healthsphere_rear").GetComponentInChildren<Renderer>();
        smallCells = new[] { graphics.FindChild("healthsphere_left").GetComponentInChildren<Renderer>(), graphics.FindChild("healthsphere_right").GetComponentInChildren<Renderer>() };
    }

    void Update() {
        //todo This should be level dependant
        if (networkView.isMine && transform.position.y < -104) {
            DoDamage(999, (NetworkPlayer)GetComponent<PlayerScript>().owner);
        }

        if (!firstSet && shieldRenderer != null) {
            shieldRenderer.material.SetColor("_TintColor", DefaultShieldColor);
            firstSet = true;
        }

        if (networkView.isMine) {
            timeUntilShieldRegen -= Time.deltaTime;
            if (timeUntilShieldRegen < 0 && Shield < maxShield) {
                timeUntilShieldRegen += shieldRegenTime;
                if (Shield == 0)
                    networkView.RPC("SetShield", RPCMode.All, true, false);
                Shield += 1;
            }

            if (invulnerable) {
                timeSinceRespawn += Time.deltaTime;
                if (timeSinceRespawn > invulnerabilityTime)
                    invulnerable = false;
            }
        }
    }

    void ShotFired() {
        invulnerable = false;
    }

    [RPC]
    void SetShield(bool on, bool immediate) {
        if (immediate) {
            shieldRenderer.enabled = on;
            shieldRenderer.material.SetColor("_TintColor", DefaultShieldColor);
            return;
        }

        TaskManager.Instance.WaitUntil(t => {
            if (shieldRenderer == null)
                return true;

            float p = Easing.EaseIn(Mathf.Clamp01(t / 0.75f), EasingType.Quadratic);
            p = on ? p : 1 - p;

            shieldRenderer.enabled = RandomHelper.Probability(Mathf.Clamp01(p));
            shieldRenderer.material.SetColor("_TintColor", on ? Color.Lerp(RecoverShieldColor, DefaultShieldColor, p) : HitShieldColor);

            return t >= 1;
        }).Then(() => {
            if (shieldRenderer != null) {
                shieldRenderer.enabled = on;
                shieldRenderer.material.SetColor("_TintColor", DefaultShieldColor);
            }
        });
    }
    [RPC]
    void SetHealth(int health) {
        bigCell.enabled = health >= 2;
        foreach (Renderer r in smallCells)
            r.enabled = health >= 2;
    }

    //[RPC]
    public void DoDamage(int damage, NetworkPlayer shootingPlayer) //, NetworkPlayer hitPlayer 
    {
        if (!dead)  //!networkView.isMine &&
        {
            if (invulnerable)
                return;

            int oldShield = Shield;
            Shield -= damage;
            timeUntilShieldRegen = shieldRegenTime;
            if (Shield < 0) {
                Health += Shield;
                Shield = 0;
            }
            if (Health <= 0) {
                if (Network.player == shootingPlayer) {
                    NetworkLeaderboard.Instance.networkView.RPC("RegisterKill", RPCMode.All, shootingPlayer, GetComponent<PlayerScript>().owner);
                    networkView.RPC("ScheduleRespawn", RPCMode.All, RespawnZone.GetRespawnPoint());
                }

                Health = 0;
                dead = true;
                Camera.main.GetComponent<WeaponIndicatorScript>().CooldownStep = 0;
            }

            networkView.RPC("SetHealth", RPCMode.All, Health);

            if ((Shield != 0) != (oldShield != 0)) {
                networkView.RPC("SetShield", RPCMode.All, Shield > 0, dead);
            }
        }
    }

    object respawnLock;
    [RPC]
    void ScheduleRespawn(Vector3 position) {
        Hide();
        Instantiate(deathPrefab, transform.position, transform.rotation);
        object thisLock = new object();
        respawnLock = thisLock;
        TaskManager.Instance.WaitFor(timeUntilRespawn).Then(() => {
            if (this != null && respawnLock == thisLock && !ServerScript.Spectating && !RoundScript.Instance.roundStopped)
                Respawn(position);
        });
    }

    [RPC]
    void ImmediateRespawn() {
        Hide();
        Respawn(RespawnZone.GetRespawnPoint());
    }

    public void Hide() {
        if (!(ServerScript.hostState == ServerScript.HostingState.Hosting || ServerScript.hostState == ServerScript.HostingState.Connected))
            return;

        Health = 0;
        dead = true;
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        foreach (Collider r in GetComponentsInChildren<Collider>()) r.enabled = false;
        foreach (PlayerShootingScript r in GetComponentsInChildren<PlayerShootingScript>())
        {
            r.CheckTargets();
            r.targets.Clear();
            r.enabled = false;
        }
    }
    public void UnHide() {
        if (!(ServerScript.hostState == ServerScript.HostingState.Hosting || ServerScript.hostState == ServerScript.HostingState.Connected))
            return;

        foreach (Renderer r in GetComponentsInChildren<Renderer>()) if (r.name != "Canon" && r.name != "flag_flag" && r.name != "Cube" && r.name != "flag_pole") r.enabled = true; // Reenable non glitched renderers
        foreach (Collider r in GetComponentsInChildren<Collider>()) r.enabled = true;
        foreach (PlayerShootingScript r in GetComponentsInChildren<PlayerShootingScript>()) r.enabled = true;

        GetComponent<PlayerScript>().ResetVelocities();
        GetComponent<PlayerShootingScript>().InstantReload();

        Shield = maxShield;
        Health = maxHealth;
        dead = false;
        timeSinceRespawn = 0;
    }

    [RPC]
    public void ToggleSpectate(bool isSpectating) {
        if (isSpectating) Hide();
        else UnHide();

        PlayerRegistry.Get(networkView.owner).Spectating = isSpectating;
    }

    public void Respawn(Vector3 position) {
        if (!(ServerScript.hostState == ServerScript.HostingState.Hosting || ServerScript.hostState == ServerScript.HostingState.Connected))
            return;

        networkView.RPC("ToggleSpectate", RPCMode.All, false);

        SendMessage("ResetAnimation");
        SendMessage("ResetWarnings");

        transform.position = position;
    }
}
