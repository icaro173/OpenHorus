using UnityEngine;

public class BulletScript : MonoBehaviour {
    public static int BulletCollisionLayers {
        get {
            return (1 << LayerMask.NameToLayer("Default")) |
                   (1 << LayerMask.NameToLayer("Player Hit"));
        }
    }

    public GameObject bulletCasingPrefab;
    public float casingForce = 500.0f;
    public float casingTorque = 5.0f;
    public float speed = 900;
    public float lifetime = 2;
    public int damage = 1;
    public float areaOfEffect = 0;
    public float recoil = 0;
    public float homing = 0;
    public float accelerationSpeed = 0.1f;
    public Transform target;
    public float TrailAlpha = 0.5f;
    float acceleration = 1.0f;
    float randomBrightness = 1.0f;

    public NetworkPlayer Player { get; set; }

    public LayerMask layerMask; //make sure we aren't in this layer
    public float skinWidth = 0.1f; //probably doesn't need to be changed

    private float minimumExtent;
    private float partialExtent;
    private float sqrMinimumExtent;
    private Vector3 previousPosition;

    void Awake() {
        // Auxillary Collision Testing
        previousPosition = rigidbody.position;
        minimumExtent = Mathf.Min(Mathf.Min(collider.bounds.extents.x, collider.bounds.extents.y), collider.bounds.extents.z);
        partialExtent = minimumExtent * (1.0f - skinWidth);
        sqrMinimumExtent = minimumExtent * minimumExtent;

        // Create bullet casing effect
        GameObject casing = (GameObject)Instantiate(bulletCasingPrefab, transform.position, transform.rotation);
        casing.rigidbody.AddRelativeForce(new Vector3(1 + Random.value, Random.value + 1, 0) * casingForce, ForceMode.Impulse);
        casing.rigidbody.AddTorque(new Vector3(-0.5f - Random.value, -Random.value * 0.1f, -0.5f - Random.value) * casingTorque, ForceMode.Impulse);
        casing.rigidbody.useGravity = true;

        // Set random brightness
        randomBrightness = RandomHelper.Between(0.125f, 1.0f);
    }

    bool DoDamageTo(Transform t) {
        HealthScript health = t.GetComponent<HealthScript>();
        // err, kinda lame, this is so that the collider can be
        // directly inside the damaged object rather than on it,
        // useful when the damage collider is different from the
        // real collider
        if (health == null && t.parent != null) {
            health = t.parent.GetComponent<HealthScript>();
        }

        if (health != null) {
            if (health.player.owner != Player) // No Friendly Fire
			{
                audio.Play(); //Hitreg Sound
                health.DoDamage(damage, Player);
                return true;
            }
        }

        return false;
    }

    void DoRecoil(Vector3 point, bool playerWasHit) {
        Collider[] colliders = Physics.OverlapSphere(point, 15, (1 << LayerMask.NameToLayer("Player Hit")));
        foreach (Collider c in colliders) {
            if (c.gameObject.name != "PlayerHit") {
                continue;
            }

            Transform t = c.transform;
            NetworkView view = t.networkView;
            while (view == null) {
                t = t.parent;
                view = t.networkView;
            }

            t = t.FindChild("mecha_gun");
            Vector3 endpoint = t.position + t.forward;

            Vector3 direction = endpoint - point;
            float dist = Mathf.Max(direction.magnitude, 0.5f);
            direction.Normalize();

            Vector3 impulse = direction * (45 / dist);
            if (impulse.y > 0) {
                impulse.y *= 2.25f;
            } else {
                impulse.y = 0;
            }

            if (playerWasHit) {
                impulse *= 10;
            }

            view.RPC("AddRecoil", RPCMode.All, impulse);
        }
    }

    void Collide(Transform trans, Vector3 point, Vector3 normal) {
        bool playerWasHit = DoDamageTo(trans);
        if (recoil > 0)
            DoRecoil(point, playerWasHit);

        if (playerWasHit) {
            EffectsScript.ExplosionHit(point, Quaternion.LookRotation(normal));
        } else {
            EffectsScript.Explosion(point, Quaternion.LookRotation(normal));
        }

        Destroy(gameObject);
    }

    void OnCollisionEnter(Collision collision) {
        BulletScript hitBullet = collision.gameObject.GetComponent<BulletScript>();
        if (hitBullet == null) {
            Collide(collision.transform, collision.contacts[0].point, collision.contacts[0].normal);
        }
    }

    void OnCollisionStay(Collision collision) {
        BulletScript hitBullet = collision.gameObject.GetComponent<BulletScript>();
        if (hitBullet == null) {
            Collide(collision.transform, collision.contacts[0].point, collision.contacts[0].normal);
        }
    }

    void Update() {
        acceleration += accelerationSpeed * Time.deltaTime;
        float distance = speed * Time.deltaTime;
        distance *= acceleration;

        transform.position += transform.forward * distance;

        // homing
        if (target != null && homing > 0) {
            Vector3 lookVec = (target.position - transform.position).normalized;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookVec),
                                                    Mathf.Clamp01(homing * Time.deltaTime * 9));
        }

        float o = randomBrightness * lifetime / 2f * 0.75f;
        GetComponent<TrailRenderer>().material.SetColor("_TintColor", new Color(o, o, o, TrailAlpha));

        // max lifetime
        lifetime -= Time.deltaTime;
        if (lifetime <= 0) {
            Destroy(gameObject);
        }
    }

    void FixedUpdate() {
        //have we moved more than our minimum extent?
        Vector3 movementThisStep = rigidbody.position - previousPosition;
        float movementSqrMagnitude = movementThisStep.sqrMagnitude;

        if (movementSqrMagnitude > sqrMinimumExtent) {
            float movementMagnitude = Mathf.Sqrt(movementSqrMagnitude);
            RaycastHit hitInfo;

            //check for obstructions we might have missed
            if (Physics.Raycast(previousPosition, movementThisStep, out hitInfo, movementMagnitude, layerMask.value)) {
                rigidbody.position = hitInfo.point - (movementThisStep / movementMagnitude) * partialExtent;
                Collide(hitInfo.transform, hitInfo.point, hitInfo.normal);
            }

            previousPosition = rigidbody.position;
        }
    }
}
