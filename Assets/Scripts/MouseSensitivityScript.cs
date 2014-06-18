using UnityEngine;

public class MouseSensitivityScript : MonoBehaviour {
    public const float baseSensitivity = 3;

    public static float Sensitivity { get; private set; }

    int sensitivityPercentage = 50;

    void Awake() {
        sensitivityPercentage = PlayerPrefs.GetInt("sensitivity", 50);
    }

    void Update() {
        if (Input.GetButtonDown("Increase Sensitivity")) {
            changeSensitivity(2);
        }
        if (Input.GetButtonDown("Decrease Sensitivity")) {
            changeSensitivity(-2);
        }
    }

    void changeSensitivity(int offset) {
        sensitivityPercentage += offset;
        sensitivityPercentage = Mathf.Clamp(sensitivityPercentage, 0, 100);
        PlayerPrefs.SetInt("sensitivity", sensitivityPercentage);

        Sensitivity = baseSensitivity * Mathf.Pow(2, sensitivityPercentage / 25.0f - 2);
    }
}
