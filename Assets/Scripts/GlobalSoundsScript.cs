using UnityEngine;
using System.Collections;

public class GlobalSoundsScript : MonoBehaviour {
    public static bool soundEnabled = true;
    public AudioSource buttonPressSound;

    static GlobalSoundsScript Instance;
    static bool playing = false; //work around the fact that TaskManager does a DontDestroyOnLoad

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
    }

    public static void PlayButtonPress() {
        Instance.buttonPressSound.Play();
    }

    void Update() {
        if (Input.GetKeyDown("m"))
            audio.mute = !audio.mute; // Disable music

        if (Input.GetKeyDown("n"))
            GlobalSoundsScript.soundEnabled = !GlobalSoundsScript.soundEnabled;

        if (!audio.mute && !audio.isPlaying)
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
