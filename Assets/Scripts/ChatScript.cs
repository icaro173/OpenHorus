using System;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;

public class ChatScript : MonoBehaviour {
    public static ChatScript Instance { get; private set; }
    public readonly List<ChatMessage> ChatLog = new List<ChatMessage>();

    public GUISkin Skin;
    public float chatTimeout = 15.0f;
    public bool showChat { get; private set; }

    string lastMessage = "";
    bool forceVisible = false;
    bool processChat = false;

    void Awake() {
        Instance = this;
    }

    void OnServerInitialized() {
        CleanUp();
    }

    void OnConnectedToServer() {
        CleanUp();
    }
    void OnDisconnectedFromServer() {
        Screen.lockCursor = false;
    }

    void CleanUp() {
        ChatLog.Clear();
        showChat = false;
        processChat = false;
        lastMessage = string.Empty;
    }

    void Update() {
        // While tab is hold, display all chat
        forceVisible = Input.GetKey(KeyCode.Tab) || RoundScript.Instance.roundStopped;

        // Update chat message lifetimes
        foreach (ChatMessage log in ChatLog) {
            log.Life += Time.deltaTime;
            if (log.Life > chatTimeout) {
                log.Hidden = true;
            }
        }

        // Update controls
        if (!showChat && Input.GetKeyUp(KeyCode.T)) {
            focusChat();
        }

        //Chat is visible
        if (showChat) {
            //Close chat
            if (Input.GetKeyDown(KeyCode.Escape)) {
                unFocusChat();
            }

            if (processChat) {
                Debug.Log("Chat");
                processChat = false;
                handleChat(lastMessage.Trim());
            }
        }
    }

    void focusChat() {
        GUI.FocusWindow(1);
        GUI.FocusControl("ChatInput");
        showChat = true;
        Screen.lockCursor = false;
    }

    void unFocusChat() {
        lastMessage = string.Empty;
        showChat = false;
        Screen.lockCursor = true;
    }

    void OnGUI() {
        // Do not show chat unless playing
        if (Network.peerType == NetworkPeerType.Disconnected || Network.peerType == NetworkPeerType.Connecting) return;

        GUI.skin = Skin;

        int height = ChatLog.Count(x => !x.Hidden || forceVisible || showChat) * 36;
        // Message list
        GUILayout.Window(1, new Rect(1, (Screen.height - 36) - height, 247, height), Chat, string.Empty);
        // Text control
        GUILayout.Window(2, new Rect(1, Screen.height - 36, 247, 36), ChatControl, string.Empty);
    }

    void Chat(int windowId) {
        foreach (ChatMessage log in ChatLog) {
            if (log.Hidden && !forceVisible && !showChat) {
                continue;
            }

            GUIStyle rowStyle = new GUIStyle(Skin.box) { fixedWidth = 246 };

            GUILayout.BeginHorizontal();

            String message = (log.IsSourceless ? "" : (log.Player.ToUpper() + ": ")) + log.Message;

            GUILayout.Box(message, rowStyle);
            GUILayout.EndHorizontal();
        }
    }

    void ChatControl(int windowId) {
        try {
            GUILayout.BeginHorizontal();

            if (showChat) {
                if (processChat == false && Event.current.keyCode == KeyCode.Return) {
                    processChat = true;
                }

                if (!processChat) {
                    GUIStyle sty = new GUIStyle(Skin.textField) { fixedWidth = 180 };

                    GUI.SetNextControlName("ChatInput");
                    lastMessage = GUILayout.TextField(lastMessage, sty);
                    if (GUI.GetNameOfFocusedControl() != "ChatInput") {
                        GUI.FocusControl("ChatInput");
                    }
                }
            }

            GUILayout.Box("", new GUIStyle(Skin.box) { fixedWidth = showChat ? 1 : 181 });
            if (GUILayout.Button("Disconnect")) {
                GlobalSoundsScript.PlayButtonPress();
                Network.Disconnect();
            }

            GUILayout.EndHorizontal();
        } catch (ArgumentException e) {
            Debug.LogWarning(e.Message);
        }
    }

    void handleChat(string chatmessage) {
        if (lastMessage.StartsWith("/")) {
            doCommand(chatmessage);
        } else {
            // If string is empty dont send
            if (chatmessage != string.Empty) {
                networkView.RPC("LogChat", RPCMode.All, Network.player, chatmessage, false, false);
            }
        }
        unFocusChat();
    }

    void doCommand(string command) {
        string[] messageParts = command.Split(' ');

        // console commands
        switch (messageParts[0]) {
            case "/leave":
                unFocusChat();
                GlobalSoundsScript.PlayButtonPress();
                Network.Disconnect();
                break;

            case "/quit":
                Application.Quit();
                break;

            case "/map":
                if (!Network.isServer) {
                    LogChat(Network.player, "Map change is only allowed on server.", true, true);
                } else if (messageParts.Length != 2) {
                    LogChat(Network.player, "Invalid arguments, expected : /map map_name", true, true);
                } else if (Application.loadedLevelName == messageParts[1]) {
                    LogChat(Network.player, "You're already in " + messageParts[1] + ", dummy.", true, true);
                } else if (!RoundScript.Instance.allowedLevels.Contains(messageParts[1])) {
                    string levels = String.Join(", ", RoundScript.Instance.allowedLevels);
                    LogChat(Network.player, "Level " + messageParts[1] + " does not exist. {" + levels + "}", true, true);
                } else {
                    RoundScript.Instance.ChangeLevelAndRestart(messageParts[1]);
                }
                break;

            case "/spectate":
                if (!ServerScript.Spectating) {
                    PlayerScript player = PlayerRegistry.Get(Network.player).Player;
                    HealthScript healthscript = player.GetComponent<HealthScript>();
                    if (healthscript.Health == 0) {
                        LogChat(Network.player, "Wait until you respawned to spectate", true, true);
                        break;
                    }
                    healthscript.networkView.RPC("ToggleSpectate", RPCMode.All, true);
                    player.networkView.RPC("setPaused", RPCMode.All, true);

                    ServerScript.Spectating = true;
                    networkView.RPC("LogChat", RPCMode.All, Network.player, "went in spectator mode.", true, false);
                } else {
                    LogChat(Network.player, "Already spectating!", true, true);
                }
                break;

            case "/join":
                if (ServerScript.Spectating) {
                    PlayerScript player = PlayerRegistry.Get(Network.player).Player;
                    player.networkView.RPC("setPaused", RPCMode.All, false);
                    player.GetComponent<HealthScript>().Respawn(RespawnZone.GetRespawnPoint());

                    networkView.RPC("LogChat", RPCMode.All, Network.player, "rejoined the game.", true, false);

                    ServerScript.Spectating = false;
                } else {
                    LogChat(Network.player, "Already in-game!", true, true);
                }
                break;

            case "/connect":
                if (messageParts.Length != 2) {
                    LogChat(Network.player, "Expected usage : /join 123.23.45.2", true, true);
                    break;
                }

                TaskManager.Instance.WaitFor(0.1f).Then(() => {
                    GlobalSoundsScript.PlayButtonPress();
                    Network.Disconnect();
                    ServerScript.Spectating = false;
                    TaskManager.Instance.WaitFor(0.75f).Then(
                        () => Network.Connect(messageParts[1], ServerScript.port)
                    );
                });
                break;

            default:
                LogChat(Network.player, lastMessage + " command not recognized.", true, true);
                break;
        }
    }

    [RPC]
    public void LogChat(NetworkPlayer player, string message, bool systemMessage, bool isSourceless) {
        if (!PlayerRegistry.Has(player)) return;
        ChatLog.Add(new ChatMessage { Player = PlayerRegistry.Get(player).Username, Message = message, IsSystem = systemMessage, IsSourceless = isSourceless });
        if (ChatLog.Count > 60)
            ChatLog.RemoveAt(0);
    }

    public class ChatMessage {
        public string Player;
        public string Message;
        public bool IsSystem, IsSourceless;
        public float Life;
        public bool Hidden;
    }
}
