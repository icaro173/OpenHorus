using UnityEngine;

public class NameLabelScript : MonoBehaviour {
    void LateUpdate() {
        transform.rotation = Camera.main.transform.rotation;
        float distance = Mathf.Sqrt(Vector3.Distance(Camera.main.transform.position, transform.position)) / 10;
        if (distance > 5) distance = 5;
        transform.localScale = new Vector3(distance, distance, distance);
    }
}
