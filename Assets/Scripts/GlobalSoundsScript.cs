using UnityEngine;

public class GlobalSoundsScript : MonoBehaviour {
    public static bool soundEnabled {
        get { return !AudioListener.pause; }
        set { AudioListener.pause = !value; }
    }
    public AudioSource buttonPressSound;

    static GlobalSoundsScript Instance;
    static bool playing = false;

    public int lastSongId = -1;

    void Start() {
        Instance = this;
    }

    public AudioClip[] songs;

    void Awake() {
        if (!playing) {
            playing = true;
            audio.Play();
        } else Destroy(this);

        // Load last settings if available
        if (PlayerPrefs.HasKey("MusicMute"))
            audio.mute = PlayerPrefs.GetInt("MusicMute") == 1;
        if (PlayerPrefs.HasKey("AudioMute"))
            GlobalSoundsScript.soundEnabled = PlayerPrefs.GetInt("AudioMute") == 1;
    }

    public static void PlayButtonPress() {
        Instance.buttonPressSound.Play();
    }

    void Update() {
        if (Input.GetKeyDown("m")) {
            audio.mute = !audio.mute;
            PlayerPrefs.SetInt("MusicMute", audio.mute ? 1 : 0);
        }

        if (Input.GetKeyDown("n")) {
            GlobalSoundsScript.soundEnabled = !GlobalSoundsScript.soundEnabled;
            PlayerPrefs.SetInt("AudioMute", GlobalSoundsScript.soundEnabled ? 1 : 0);
        }

        if (!audio.mute && !audio.isPlaying && Application.loadedLevel != 0)
            PlaySong();
    }

    void OnLevelWasLoaded(int levelIndex) {
        PlaySong();
    }

    void PlaySong() {
        int songId;
        do { songId = Random.Range(0, songs.Length); }
        while (songId == lastSongId);
        lastSongId = songId;
        audio.clip = songs[songId];
        audio.Play();
    }
}
