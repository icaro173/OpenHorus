using UnityEngine;

public class MouseSensitivityScript : MonoBehaviour {
    public GUISkin skin;
    public float baseSensitivity = 3;

    public static float Sensitivity { get; private set; }

    int sensitivityPercentage = 50;
    GUIStyle windowStyle;

    void Awake() {
        windowStyle = new GUIStyle(skin.window) { normal = { background = null } };
        sensitivityPercentage = PlayerPrefs.GetInt("sensitivity", 50);
    }

    void Update() {
        if (Input.GetButtonDown("Increase Sensitivity")) {
            sensitivityPercentage += 2;
        }
        if (Input.GetButtonDown("Decrease Sensitivity")) {
            sensitivityPercentage -= 2;
        }
        sensitivityPercentage = Mathf.Clamp(sensitivityPercentage, 0, 100);
        PlayerPrefs.SetInt("sensitivity", sensitivityPercentage);

        Sensitivity = baseSensitivity *
            Mathf.Pow(2, sensitivityPercentage / 25.0f - 2);
    }
}
