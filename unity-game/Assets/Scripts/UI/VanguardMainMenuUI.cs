using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using VanguardZero.Core;
using VanguardZero.Networking;

namespace VanguardZero.UI
{
    /// <summary>
    /// AAA Tactical Main Menu & Pre-Match Lobby UI over a live 3D dusk rain-swept BLACKSITE-09 backdrop.
    /// Supports Quick Match (Unity Relay), Solo Skirmish vs Bots, Host Public/Private/LAN Match,
    /// Join by Code / LAN IP, Live Server Browser, Pre-Match Lobby Roster, and Arsenal & Settings.
    /// </summary>
    public class VanguardMainMenuUI : MonoBehaviour
    {
        [Header("3D Live Backdrop Orbit")]
        [SerializeField] private Transform backdropCamera;
        [SerializeField] private Vector3 orbitTarget = new Vector3(2.8f, 1.55f, 3.2f);
        [SerializeField] private float orbitRadius = 8.4f;
        [SerializeField] private float orbitHeight = 1.95f;
        [SerializeField] private float orbitDegreesPerSec = 1.8f;

        private Canvas _canvas;
        private GameObject _homePanel;
        private GameObject _hostPanel;
        private GameObject _joinPanel;
        private GameObject _browserPanel;
        private GameObject _lobbyPanel;
        private GameObject _settingsPanel;

        private TextMeshProUGUI _cloudStatusLabel;
        private UnityEngine.UI.Image _cloudStatusDot;
        private TextMeshProUGUI _statusToast;
        private TMP_InputField _callsignInput;

        // Host controls
        private TMP_InputField _sessionNameInput;
        private TMP_InputField _lanPortInput;
        private MatchMode _selectedHostMode = MatchMode.TeamDeathmatch;
        private bool _hostPrivate;
        private bool _hostFillBots = true;
        private TextMeshProUGUI _hostModeLabel;
        private TextMeshProUGUI _hostPrivacyLabel;
        private TextMeshProUGUI _hostBotsLabel;

        // Join controls
        private TMP_InputField _joinCodeInput;
        private TMP_InputField _lanIpInput;

        // Browser
        private Transform _browserListContainer;
        private TextMeshProUGUI _browserEmptyLabel;

        // Lobby
        private TextMeshProUGUI _lobbyHeaderLabel;
        private TextMeshProUGUI _lobbyJoinCodeLabel;
        private TextMeshProUGUI _lobbyModeLabel;
        private Transform _lobbyRosterContainer;
        private UnityEngine.UI.Button _lobbyStartButton;
        private bool _localReady;
        private TeamId _localPreferredTeam = TeamId.Alpha;

        private float _orbitAngle = 38f;
        private bool _busy;

        private void Awake()
        {
            EnsureEventSystem();
            VanguardAudioManager.EnsureInstance();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 1f;

            if (backdropCamera == null && Camera.main != null)
                backdropCamera = Camera.main.transform;

            // Enforce v2.0 cinematic low-angle hero framing (overrides legacy v1 scene serialized values!)
            // Frames ShowcaseOperator (4.2, 0, 4.6) heroically full-body right-center with Bunker, Radar, Watchtower & 4K Dusk Sky behind!
            orbitTarget = new Vector3(3.0f, 0f, 3.2f);
            orbitRadius = 5.8f;
            orbitHeight = 1.48f;
            orbitDegreesPerSec = 1.1f;
            _orbitAngle = 0f;

            BuildMenuCanvas();
        }

        private async void Start()
        {
            VanguardAudioManager.Instance?.PlayAmbienceLoop("ambience_blacksite_rain_loop");
            VanguardAudioManager.Instance?.PlayMusicLoop("music_main_menu_loop");

            BindSessionEvents();
            ShowScreen(_homePanel);

            if (VanguardNetServices.Session != null)
            {
                UpdateCloudBadge(VanguardNetServices.Session.CloudState, VanguardNetServices.Session.CloudStatusBanner);
                if (VanguardNetServices.Session.CloudState == CloudServiceState.Uninitialized)
                {
                    await VanguardNetServices.Session.InitializeServicesAsync(VanguardSettings.Callsign);
                }
            }
            else
            {
                VanguardNetServices.OnSessionServiceRegistered += HandleSessionServiceRegistered;
                UpdateCloudBadge(CloudServiceState.Initializing, "Initializing Tactical Network Services...");
            }
        }

        private void OnDestroy()
        {
            VanguardNetServices.OnSessionServiceRegistered -= HandleSessionServiceRegistered;
            UnbindSessionEvents();
        }

        private void HandleSessionServiceRegistered(INetworkSessionService svc)
        {
            BindSessionEvents();
            UpdateCloudBadge(svc.CloudState, svc.CloudStatusBanner);
            if (svc.CloudState == CloudServiceState.Uninitialized)
            {
                _ = svc.InitializeServicesAsync(VanguardSettings.Callsign);
            }
        }

        private void BindSessionEvents()
        {
            var svc = VanguardNetServices.Session;
            if (svc == null) return;
            svc.OnCloudStateChanged -= UpdateCloudBadge;
            svc.OnCloudStateChanged += UpdateCloudBadge;
            svc.OnSessionJoined -= HandleSessionJoined;
            svc.OnSessionJoined += HandleSessionJoined;
            svc.OnSessionLeft -= HandleSessionLeft;
            svc.OnSessionLeft += HandleSessionLeft;
            svc.OnLobbyRosterChanged -= RefreshLobbyRoster;
            svc.OnLobbyRosterChanged += RefreshLobbyRoster;
        }

        private void UnbindSessionEvents()
        {
            var svc = VanguardNetServices.Session;
            if (svc == null) return;
            svc.OnCloudStateChanged -= UpdateCloudBadge;
            svc.OnSessionJoined -= HandleSessionJoined;
            svc.OnSessionLeft -= HandleSessionLeft;
            svc.OnLobbyRosterChanged -= RefreshLobbyRoster;
        }

        private void Update()
        {
            if (backdropCamera != null)
            {
                // Dramatic low-angle hero arc around ShowcaseOperator (4.2, 0, 4.6) framing full operator + Radar, Bunkers & 4K Dusk Sky
                _orbitAngle += orbitDegreesPerSec * Time.unscaledDeltaTime;
                float swing = 48f + Mathf.Sin(_orbitAngle * 0.25f) * 8f;
                float rad = swing * Mathf.Deg2Rad;
                Vector3 pos = new Vector3(
                    orbitTarget.x + Mathf.Cos(rad) * orbitRadius,
                    orbitHeight + Mathf.Sin(Time.unscaledTime * 0.45f) * 0.04f,
                    orbitTarget.z + Mathf.Sin(rad) * orbitRadius);
                backdropCamera.position = pos;
                backdropCamera.LookAt(new Vector3(2.8f, 1.42f, 2.8f));
            }
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        private void BuildMenuCanvas()
        {
            _canvas = VanguardUiFactory.CreateHudCanvas("VanguardMainMenuCanvas", 10);
            Transform root = _canvas.transform;

            // Subtle atmospheric left gradient scrim so UI is crystal clear over 3D Blacksite-09
            VanguardUiFactory.CreatePanel(
                "LeftVignetteScrim", root,
                new Color(0.03f, 0.045f, 0.068f, 0.68f),
                new Vector2(0f, 0f), new Vector2(0.42f, 1f),
                new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero, false, false);

            // Top-Right Cloud Relay Status Pill
            var statusPill = VanguardUiFactory.CreatePanel(
                "CloudStatusPill", root, VanguardPalette.HudPanelDark,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-36f, -32f), new Vector2(520f, 48f), true, false);
            var dotRt = VanguardUiFactory.CreateRect("Dot", statusPill.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(12f, 12f));
            _cloudStatusDot = dotRt.gameObject.AddComponent<UnityEngine.UI.Image>();
            _cloudStatusDot.sprite = VanguardUiFactory.SolidSprite;
            _cloudStatusDot.color = VanguardPalette.AccentAmber;
            _cloudStatusLabel = VanguardUiFactory.CreateLabel("StatusText", statusPill.transform, "INITIALIZING RELAY...", 16f, VanguardPalette.HudLight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 0.8f);
            _cloudStatusLabel.rectTransform.anchorMin = Vector2.zero;
            _cloudStatusLabel.rectTransform.anchorMax = Vector2.one;
            _cloudStatusLabel.rectTransform.offsetMin = new Vector2(38f, 0f);
            _cloudStatusLabel.rectTransform.offsetMax = new Vector2(-12f, 0f);

            // Bottom status toast banner
            var toastPanel = VanguardUiFactory.CreatePanel(
                "StatusToast", root, VanguardPalette.HudPanelDark,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 24f), new Vector2(880f, 46f), true, false);
            _statusToast = VanguardUiFactory.CreateLabel("ToastLabel", toastPanel.transform, "READY // SELECT DEPLOYMENT MODE", 16f, VanguardPalette.AccentCyan, TextAlignmentOptions.Center, FontStyles.Bold, 1.0f);
            _statusToast.rectTransform.anchorMin = Vector2.zero;
            _statusToast.rectTransform.anchorMax = Vector2.one;
            _statusToast.rectTransform.offsetMin = new Vector2(12f, 0f);
            _statusToast.rectTransform.offsetMax = new Vector2(-12f, 0f);

            BuildHomePanel(root);
            BuildHostPanel(root);
            BuildJoinPanel(root);
            BuildBrowserPanel(root);
            BuildLobbyPanel(root);
            BuildSettingsPanel(root);
        }

        private void BuildHomePanel(Transform root)
        {
            var panel = VanguardUiFactory.CreateRect("HomePanel", root, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(60f, 0f), new Vector2(520f, 0f));
            _homePanel = panel.gameObject;

            // Title Branding Block (single clean line, zero overlap!)
            var title = VanguardUiFactory.CreateLabel("Title", panel, "VANGUARD ZERO", 52f, VanguardPalette.HudLight, TextAlignmentOptions.Left, FontStyles.Bold, 3.0f);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -72f);
            title.rectTransform.sizeDelta = new Vector2(0f, 64f);

            var sub = VanguardUiFactory.CreateLabel("Subtitle", panel, "BLACKSITE-09 // TACTICAL MULTIPLAYER FPS", 17f, VanguardPalette.AccentAmber, TextAlignmentOptions.Left, FontStyles.Bold, 1.5f);
            sub.textWrappingMode = TextWrappingModes.NoWrap;
            sub.rectTransform.anchorMin = new Vector2(0f, 1f);
            sub.rectTransform.anchorMax = new Vector2(1f, 1f);
            sub.rectTransform.pivot = new Vector2(0f, 1f);
            sub.rectTransform.anchoredPosition = new Vector2(2f, -142f);
            sub.rectTransform.sizeDelta = new Vector2(0f, 28f);

            // Callsign bar
            var callsignBox = VanguardUiFactory.CreateRect("CallsignRow", panel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, -194f), new Vector2(0f, 72f));
            var csLbl = VanguardUiFactory.CreateLabel("CallsignLbl", callsignBox, "OPERATOR CALLSIGN", 14f, VanguardPalette.HudMuted, TextAlignmentOptions.Left, FontStyles.Bold, 1.2f);
            csLbl.rectTransform.anchorMin = new Vector2(0f, 1f);
            csLbl.rectTransform.anchorMax = new Vector2(1f, 1f);
            csLbl.rectTransform.pivot = new Vector2(0f, 1f);
            csLbl.rectTransform.sizeDelta = new Vector2(0f, 22f);

            _callsignInput = VanguardUiFactory.CreateTacticalInput("CallsignInput", callsignBox, "ENTER CALLSIGN...", VanguardSettings.Callsign, 46f);
            var csRt = _callsignInput.GetComponent<RectTransform>();
            csRt.anchorMin = new Vector2(0f, 0f);
            csRt.anchorMax = new Vector2(1f, 0f);
            csRt.pivot = new Vector2(0.5f, 0f);
            csRt.sizeDelta = new Vector2(0f, 46f);
            _callsignInput.onEndEdit.AddListener(val =>
            {
                VanguardSettings.Callsign = val;
                _ = VanguardNetServices.Session?.SetCallsignAsync(VanguardSettings.Callsign);
            });

            // Vertical Button Stack
            var stack = VanguardUiFactory.CreateRect("ActionStack", panel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, -290f), new Vector2(0f, 520f));
            var vlg = stack.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 12f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            VanguardUiFactory.CreateTacticalButton("BtnQuickMatch", stack, "QUICK MATCH  //  ONLINE RELAY", VanguardPalette.AccentAmber, () => _ = OnQuickMatchClicked(), 60f, 20f, true);
            VanguardUiFactory.CreateTacticalButton("BtnSoloSkirmish", stack, "SOLO SKIRMISH  //  VS TACTICAL BOTS", VanguardPalette.AccentCyan, () => _ = OnSoloSkirmishClicked(), 56f, 19f, false);
            VanguardUiFactory.CreateTacticalButton("BtnHostMatch", stack, "HOST MATCH  //  PUBLIC · PRIVATE · LAN", VanguardPalette.HudLight, () => ShowScreen(_hostPanel), 52f, 18f, false);
            VanguardUiFactory.CreateTacticalButton("BtnJoinCode", stack, "JOIN BY CODE  //  RELAY · LAN IP", VanguardPalette.HudLight, () => ShowScreen(_joinPanel), 52f, 18f, false);
            VanguardUiFactory.CreateTacticalButton("BtnServerBrowser", stack, "SERVER BROWSER  //  OPEN LOBBIES", VanguardPalette.HudLight, () => { ShowScreen(_browserPanel); _ = RefreshBrowserLobbies(); }, 52f, 18f, false);
            VanguardUiFactory.CreateTacticalButton("BtnSettings", stack, "ARSENAL & SETTINGS", VanguardPalette.HudMuted, () => ShowScreen(_settingsPanel), 50f, 17f, false);
            VanguardUiFactory.CreateTacticalButton("BtnQuit", stack, "QUIT TO DESKTOP", VanguardPalette.AccentCrimson, OnQuitClicked, 46f, 16f, false);
        }

        private void BuildHostPanel(Transform root)
        {
            var card = VanguardUiFactory.CreatePanel(
                "HostPanel", root, VanguardPalette.HudPanelBg,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(620f, 560f), true, true);
            VanguardUiFactory.AddAccentStripe(card.transform, VanguardPalette.AccentAmber, false, 4f);
            _hostPanel = card.gameObject;

            var content = VanguardUiFactory.CreateFullStretch("Content", card.transform, 32f, 28f, 32f, 28f);
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 14f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var header = VanguardUiFactory.CreateLabel("Header", content, "HOST TACTICAL OPERATION", 26f, VanguardPalette.HudLight, TextAlignmentOptions.Left, FontStyles.Bold, 3f);
            header.gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;

            _sessionNameInput = VanguardUiFactory.CreateTacticalInput("SessionName", content, "OPERATION NAME...", "BLACKSITE-09 OPS", 46f);

            var modeBtn = VanguardUiFactory.CreateTacticalButton("ModeToggle", content, "MODE: TEAM DEATHMATCH (ALPHA VS BRAVO)", VanguardPalette.AccentCyan, () =>
            {
                _selectedHostMode = _selectedHostMode == MatchMode.TeamDeathmatch ? MatchMode.FreeForAll : MatchMode.TeamDeathmatch;
                _hostModeLabel.text = _selectedHostMode == MatchMode.TeamDeathmatch
                    ? "MODE: TEAM DEATHMATCH (ALPHA VS BRAVO)"
                    : "MODE: FREE-FOR-ALL (EVERY OPERATOR FOR THEMSELVES)";
            }, 46f, 15f);
            _hostModeLabel = modeBtn.GetComponentInChildren<TextMeshProUGUI>();

            var privBtn = VanguardUiFactory.CreateTacticalButton("PrivacyToggle", content, "VISIBILITY: PUBLIC (LISTED IN BROWSER)", VanguardPalette.HudLight, () =>
            {
                _hostPrivate = !_hostPrivate;
                _hostPrivacyLabel.text = _hostPrivate ? "VISIBILITY: PRIVATE (JOIN CODE ONLY)" : "VISIBILITY: PUBLIC (LISTED IN BROWSER)";
            }, 46f, 15f);
            _hostPrivacyLabel = privBtn.GetComponentInChildren<TextMeshProUGUI>();

            var botsBtn = VanguardUiFactory.CreateTacticalButton("BotsToggle", content, "AI BOT BACKFILL: ENABLED (6 COMBATANTS)", VanguardPalette.HudLight, () =>
            {
                _hostFillBots = !_hostFillBots;
                _hostBotsLabel.text = _hostFillBots ? "AI BOT BACKFILL: ENABLED (6 COMBATANTS)" : "AI BOT BACKFILL: DISABLED (HUMANS ONLY)";
            }, 46f, 15f);
            _hostBotsLabel = botsBtn.GetComponentInChildren<TextMeshProUGUI>();

            _lanPortInput = VanguardUiFactory.CreateTacticalInput("LanPort", content, "LAN PORT (DEFAULT 7777)", "7777", 42f);

            VanguardUiFactory.CreateTacticalButton("LaunchRelayHost", content, "CREATE ONLINE RELAY SESSION", VanguardPalette.AccentAmber, () => _ = OnHostRelayClicked(), 54f, 17f, true);
            VanguardUiFactory.CreateTacticalButton("LaunchLanHost", content, "HOST LAN DIRECT (LOCAL NETWORK)", VanguardPalette.AccentCyan, () => _ = OnHostLanClicked(), 48f, 15f, false);
            VanguardUiFactory.CreateTacticalButton("BackHome", content, "BACK", VanguardPalette.HudMuted, () => ShowScreen(_homePanel), 42f, 14f, false);
        }

        private void BuildJoinPanel(Transform root)
        {
            var card = VanguardUiFactory.CreatePanel(
                "JoinPanel", root, VanguardPalette.HudPanelBg,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(560f, 440f), true, true);
            VanguardUiFactory.AddAccentStripe(card.transform, VanguardPalette.AccentCyan, false, 4f);
            _joinPanel = card.gameObject;

            var content = VanguardUiFactory.CreateFullStretch("Content", card.transform, 32f, 28f, 32f, 28f);
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 14f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var header = VanguardUiFactory.CreateLabel("Header", content, "JOIN OPERATION", 26f, VanguardPalette.HudLight, TextAlignmentOptions.Left, FontStyles.Bold, 3f);
            header.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;

            _joinCodeInput = VanguardUiFactory.CreateTacticalInput("JoinCode", content, "ENTER 6-CHAR RELAY JOIN CODE...", "", 48f);
            VanguardUiFactory.CreateTacticalButton("JoinRelayBtn", content, "CONNECT VIA UNITY RELAY CODE", VanguardPalette.AccentAmber, () => _ = OnJoinByCodeClicked(), 52f, 16f, true);

            _lanIpInput = VanguardUiFactory.CreateTacticalInput("LanIp", content, "LAN HOST IP (e.g. 127.0.0.1)", "127.0.0.1", 46f);
            VanguardUiFactory.CreateTacticalButton("JoinLanBtn", content, "CONNECT VIA LAN DIRECT IP", VanguardPalette.AccentCyan, () => _ = OnJoinLanClicked(), 48f, 15f, false);

            VanguardUiFactory.CreateTacticalButton("BackHome", content, "BACK", VanguardPalette.HudMuted, () => ShowScreen(_homePanel), 42f, 14f, false);
        }

        private void BuildBrowserPanel(Transform root)
        {
            var card = VanguardUiFactory.CreatePanel(
                "BrowserPanel", root, VanguardPalette.HudPanelBg,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(780f, 560f), true, true);
            VanguardUiFactory.AddAccentStripe(card.transform, VanguardPalette.AccentCyan, false, 4f);
            _browserPanel = card.gameObject;

            var content = VanguardUiFactory.CreateFullStretch("Content", card.transform, 28f, 24f, 28f, 24f);
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 12f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var header = VanguardUiFactory.CreateLabel("Header", content, "OPEN RELAY SESSIONS // SERVER BROWSER", 24f, VanguardPalette.HudLight, TextAlignmentOptions.Left, FontStyles.Bold, 3f);
            header.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

            var listBox = VanguardUiFactory.CreatePanel("ListBox", content, VanguardPalette.HudPanelDark, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, true, false);
            var listLe = listBox.gameObject.AddComponent<LayoutElement>();
            listLe.preferredHeight = 360f;
            listLe.flexibleHeight = 1f;

            var listContent = VanguardUiFactory.CreateFullStretch("Rows", listBox.transform, 12f, 12f, 12f, 12f);
            var lvlg = listContent.gameObject.AddComponent<VerticalLayoutGroup>();
            lvlg.spacing = 8f;
            lvlg.childControlWidth = true;
            lvlg.childControlHeight = true;
            lvlg.childForceExpandWidth = true;
            lvlg.childForceExpandHeight = false;
            _browserListContainer = listContent;

            _browserEmptyLabel = VanguardUiFactory.CreateLabel("Empty", listBox.transform, "SCANNING FOR ACTIVE OPERATIONS...", 16f, VanguardPalette.HudMuted, TextAlignmentOptions.Center, FontStyles.Italic, 1.5f);
            _browserEmptyLabel.rectTransform.anchorMin = Vector2.zero;
            _browserEmptyLabel.rectTransform.anchorMax = Vector2.one;

            var foot = new GameObject("Footer", typeof(RectTransform));
            foot.transform.SetParent(content, false);
            foot.AddComponent<LayoutElement>().preferredHeight = 48f;
            var hlg = foot.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;

            VanguardUiFactory.CreateTacticalButton("RefreshBtn", foot.transform, "REFRESH LOBBIES", VanguardPalette.AccentCyan, () => _ = RefreshBrowserLobbies(), 46f, 15f, false);
            VanguardUiFactory.CreateTacticalButton("BackBtn", foot.transform, "BACK", VanguardPalette.HudMuted, () => ShowScreen(_homePanel), 46f, 15f, false);
        }

        private void BuildLobbyPanel(Transform root)
        {
            var card = VanguardUiFactory.CreatePanel(
                "LobbyPanel", root, VanguardPalette.HudPanelBg,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(780f, 590f), true, true);
            VanguardUiFactory.AddAccentStripe(card.transform, VanguardPalette.AccentAmber, false, 4f);
            _lobbyPanel = card.gameObject;

            var content = VanguardUiFactory.CreateFullStretch("Content", card.transform, 30f, 24f, 30f, 24f);
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 12f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            _lobbyHeaderLabel = VanguardUiFactory.CreateLabel("Header", content, "TACTICAL STAGING LOBBY", 25f, VanguardPalette.HudLight, TextAlignmentOptions.Left, FontStyles.Bold, 3f);
            _lobbyHeaderLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

            _lobbyModeLabel = VanguardUiFactory.CreateLabel("SubInfo", content, "BLACKSITE-09 // TEAM DEATHMATCH", 14f, VanguardPalette.AccentCyan, TextAlignmentOptions.Left, FontStyles.Bold, 2f);
            _lobbyModeLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

            // Join code row + copy button
            var codeRow = new GameObject("CodeRow", typeof(RectTransform));
            codeRow.transform.SetParent(content, false);
            codeRow.AddComponent<LayoutElement>().preferredHeight = 46f;
            var chlg = codeRow.AddComponent<HorizontalLayoutGroup>();
            chlg.spacing = 12f;
            chlg.childControlWidth = true;
            chlg.childControlHeight = true;

            _lobbyJoinCodeLabel = VanguardUiFactory.CreateLabel("JoinCodeLbl", codeRow.transform, "JOIN CODE: ------", 20f, VanguardPalette.AccentAmber, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 4f);
            var cle = _lobbyJoinCodeLabel.gameObject.AddComponent<LayoutElement>();
            cle.flexibleWidth = 1f;

            var copyBtn = VanguardUiFactory.CreateTacticalButton("CopyCodeBtn", codeRow.transform, "COPY CODE", VanguardPalette.AccentCyan, () =>
            {
                string code = VanguardNetServices.Session?.CurrentJoinCode ?? "";
                if (!string.IsNullOrEmpty(code))
                {
                    GUIUtility.systemCopyBuffer = code;
                    SetStatus($"JOIN CODE '{code}' COPIED TO CLIPBOARD");
                }
            }, 44f, 14f, false);
            copyBtn.GetComponent<LayoutElement>().preferredWidth = 170f;

            // Roster Container
            var rosterPanel = VanguardUiFactory.CreatePanel("RosterBox", content, VanguardPalette.HudPanelDark, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, true, false);
            var rle = rosterPanel.gameObject.AddComponent<LayoutElement>();
            rle.preferredHeight = 280f;
            rle.flexibleHeight = 1f;

            var rContent = VanguardUiFactory.CreateFullStretch("Rows", rosterPanel.transform, 14f, 12f, 14f, 12f);
            var rvlg = rContent.gameObject.AddComponent<VerticalLayoutGroup>();
            rvlg.spacing = 6f;
            rvlg.childControlWidth = true;
            rvlg.childControlHeight = true;
            rvlg.childForceExpandWidth = true;
            rvlg.childForceExpandHeight = false;
            _lobbyRosterContainer = rContent;

            // Controls Row
            var controls = new GameObject("LobbyControls", typeof(RectTransform));
            controls.transform.SetParent(content, false);
            controls.AddComponent<LayoutElement>().preferredHeight = 50f;
            var lhlg = controls.AddComponent<HorizontalLayoutGroup>();
            lhlg.spacing = 10f;
            lhlg.childControlWidth = true;
            lhlg.childControlHeight = true;
            lhlg.childForceExpandWidth = true;

            VanguardUiFactory.CreateTacticalButton("SwitchTeamBtn", controls.transform, "SWITCH TEAM", VanguardPalette.AccentCyan, () =>
            {
                _localPreferredTeam = _localPreferredTeam == TeamId.Alpha ? TeamId.Bravo : TeamId.Alpha;
                _ = VanguardNetServices.Session?.SetLocalPreferredTeamAsync(_localPreferredTeam);
                SetStatus($"PREFERRED TEAM: {_localPreferredTeam.ToString().ToUpper()}");
            }, 48f, 14f, false);

            VanguardUiFactory.CreateTacticalButton("ReadyBtn", controls.transform, "TOGGLE READY", VanguardPalette.AccentEmerald, () =>
            {
                _localReady = !_localReady;
                _ = VanguardNetServices.Session?.SetLocalReadyAsync(_localReady);
                SetStatus(_localReady ? "STATUS: READY FOR DEPLOYMENT" : "STATUS: STANDBY");
            }, 48f, 14f, false);

            _lobbyStartButton = VanguardUiFactory.CreateTacticalButton("StartMatchBtn", controls.transform, "DEPLOY MATCH", VanguardPalette.AccentAmber, () => _ = OnStartMatchFromLobby(), 48f, 15f, true);

            VanguardUiFactory.CreateTacticalButton("LeaveLobbyBtn", content, "LEAVE LOBBY", VanguardPalette.AccentCrimson, () => _ = OnLeaveLobbyClicked(), 42f, 14f, false);
        }

        private void BuildSettingsPanel(Transform root)
        {
            var card = VanguardUiFactory.CreatePanel(
                "SettingsPanel", root, VanguardPalette.HudPanelBg,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(680f, 600f), true, true);
            VanguardUiFactory.AddAccentStripe(card.transform, VanguardPalette.AccentAmber, false, 4f);
            _settingsPanel = card.gameObject;

            var content = VanguardUiFactory.CreateFullStretch("Content", card.transform, 32f, 24f, 32f, 24f);
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 10f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var header = VanguardUiFactory.CreateLabel("Header", content, "ARSENAL & TACTICAL SETTINGS", 24f, VanguardPalette.HudLight, TextAlignmentOptions.Left, FontStyles.Bold, 3f);
            header.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;

            var arsenal = VanguardUiFactory.CreateLabel(
                "ArsenalSpec",
                content,
                "[1] VX-47 ASSAULT RIFLE (30 RD · 630 RPM)   |   [2] M890 SHOTGUN (8 RD · 8 PELLETS)   |   [3] M19 SIDEARM (15 RD)",
                13f,
                VanguardPalette.AccentAmber,
                TextAlignmentOptions.Left,
                FontStyles.Bold,
                1f);
            arsenal.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;

            VanguardUiFactory.CreateTacticalSlider("SensSlider", content, "MOUSE SENSITIVITY", 0.2f, 4.0f, VanguardSettings.MouseSensitivity, v => VanguardSettings.MouseSensitivity = v, out _);
            VanguardUiFactory.CreateTacticalSlider("AdsSlider", content, "ADS SENSITIVITY SCALE", 0.25f, 1.0f, VanguardSettings.AdsSensitivityMultiplier, v => VanguardSettings.AdsSensitivityMultiplier = v, out _);
            VanguardUiFactory.CreateTacticalSlider("FovSlider", content, "FIELD OF VIEW (DEG)", 65f, 100f, VanguardSettings.FieldOfView, v => VanguardSettings.FieldOfView = v, out _);
            VanguardUiFactory.CreateTacticalSlider("MasterSlider", content, "MASTER VOLUME", 0f, 1f, VanguardSettings.MasterVolume, v => VanguardSettings.MasterVolume = v, out _);
            VanguardUiFactory.CreateTacticalSlider("MusicSlider", content, "MUSIC VOLUME", 0f, 1f, VanguardSettings.MusicVolume, v => VanguardSettings.MusicVolume = v, out _);
            VanguardUiFactory.CreateTacticalSlider("SfxSlider", content, "COMBAT SFX VOLUME", 0f, 1f, VanguardSettings.SfxVolume, v => VanguardSettings.SfxVolume = v, out _);

            string[] qNames = QualitySettings.names;
            TextMeshProUGUI qualLabel = null;
            var qualBtn = VanguardUiFactory.CreateTacticalButton("QualityBtn", content, $"GRAPHICS QUALITY: {(qNames.Length > 0 ? qNames[VanguardSettings.QualityLevel % qNames.Length].ToUpper() : "HIGH")}", VanguardPalette.AccentCyan, () =>
            {
                if (qNames.Length == 0) return;
                VanguardSettings.QualityLevel = (VanguardSettings.QualityLevel + 1) % qNames.Length;
                if (qualLabel != null) qualLabel.text = $"GRAPHICS QUALITY: {qNames[VanguardSettings.QualityLevel].ToUpper()}";
            }, 44f, 14f, false);
            qualLabel = qualBtn.GetComponentInChildren<TextMeshProUGUI>();

            VanguardUiFactory.CreateTacticalButton("CloseSettings", content, "APPLY & BACK", VanguardPalette.AccentAmber, () => ShowScreen(_homePanel), 46f, 15f, true);
        }

        private void ShowScreen(GameObject target)
        {
            if (_homePanel != null) _homePanel.SetActive(target == _homePanel);
            if (_hostPanel != null) _hostPanel.SetActive(target == _hostPanel);
            if (_joinPanel != null) _joinPanel.SetActive(target == _joinPanel);
            if (_browserPanel != null) _browserPanel.SetActive(target == _browserPanel);
            if (_lobbyPanel != null) _lobbyPanel.SetActive(target == _lobbyPanel);
            if (_settingsPanel != null) _settingsPanel.SetActive(target == _settingsPanel);
        }

        private void UpdateCloudBadge(CloudServiceState state, string banner)
        {
            if (_cloudStatusLabel == null || _cloudStatusDot == null) return;
            switch (state)
            {
                case CloudServiceState.Ready:
                    _cloudStatusDot.color = VanguardPalette.AccentEmerald;
                    _cloudStatusLabel.text = "UNITY RELAY ONLINE  //  CROSS-PLATFORM READY";
                    break;
                case CloudServiceState.UnavailableFallback:
                    _cloudStatusDot.color = VanguardPalette.AccentAmber;
                    _cloudStatusLabel.text = "OFFLINE FALLBACK  //  LAN DIRECT & SOLO ACTIVE";
                    break;
                default:
                    _cloudStatusDot.color = VanguardPalette.AccentCyan;
                    _cloudStatusLabel.text = "INITIALIZING TACTICAL NETWORK...";
                    break;
            }
            if (!string.IsNullOrEmpty(banner)) SetStatus(banner);
        }

        private void SetStatus(string msg)
        {
            if (_statusToast != null) _statusToast.text = msg.ToUpperInvariant();
        }

        private async Task OnQuickMatchClicked()
        {
            if (_busy || VanguardNetServices.Session == null) return;
            _busy = true;
            SetStatus("MATCHMAKING VIA UNITY RELAY...");
            var res = await VanguardNetServices.Session.QuickPlayAsync(MatchMode.TeamDeathmatch);
            _busy = false;
            if (res.Success)
            {
                VanguardAudioManager.Instance?.Play2D("ui_match_found", AudioBus.UI, 0.9f);
                SetStatus($"SESSION JOINED ({res.Mode}) // CODE: {res.JoinCode}");
                ShowLobbyView();
            }
            else
            {
                SetStatus($"QUICK MATCH FAILED: {res.ErrorMessage}");
            }
        }

        private async Task OnSoloSkirmishClicked()
        {
            if (_busy || VanguardNetServices.Session == null) return;
            _busy = true;
            SetStatus("DEPLOYING SOLO SKIRMISH VS TACTICAL BOTS...");
            var rules = MatchRulesConfig.CreateDefault(MatchMode.SoloSkirmish);
            rules.FillWithBots = true;
            rules.TargetCombatantCount = 6;
            var res = await VanguardNetServices.Session.StartSoloSkirmishAsync(rules);
            _busy = false;
            if (!res.Success)
            {
                SetStatus($"SOLO SKIRMISH FAILED: {res.ErrorMessage}");
            }
        }

        private async Task OnHostRelayClicked()
        {
            if (_busy || VanguardNetServices.Session == null) return;
            _busy = true;
            SetStatus("ALLOCATING UNITY RELAY SESSION...");
            var req = HostSessionRequest.CreateDefault(_selectedHostMode, _hostPrivate);
            req.SessionName = string.IsNullOrWhiteSpace(_sessionNameInput.text) ? "BLACKSITE-09 OPS" : _sessionNameInput.text.Trim();
            req.FillWithBots = _hostFillBots;
            var res = await VanguardNetServices.Session.HostSessionAsync(req);
            _busy = false;
            if (res.Success)
            {
                VanguardAudioManager.Instance?.Play2D("ui_confirm", AudioBus.UI, 0.85f);
                ShowLobbyView();
            }
            else
            {
                SetStatus($"HOST FAILED: {res.ErrorMessage}");
            }
        }

        private async Task OnHostLanClicked()
        {
            if (_busy || VanguardNetServices.Session == null) return;
            _busy = true;
            ushort port = ushort.TryParse(_lanPortInput.text, out var p) ? p : (ushort)7777;
            var req = HostSessionRequest.CreateDefault(_selectedHostMode, false);
            req.SessionName = string.IsNullOrWhiteSpace(_sessionNameInput.text) ? "BLACKSITE-09 LAN" : _sessionNameInput.text.Trim();
            req.FillWithBots = _hostFillBots;
            var res = await VanguardNetServices.Session.HostLanDirectAsync(req, port);
            _busy = false;
            if (res.Success)
            {
                ShowLobbyView();
            }
            else
            {
                SetStatus($"LAN HOST FAILED: {res.ErrorMessage}");
            }
        }

        private async Task OnJoinByCodeClicked()
        {
            if (_busy || VanguardNetServices.Session == null) return;
            string code = _joinCodeInput.text?.Trim().ToUpperInvariant() ?? "";
            if (string.IsNullOrEmpty(code))
            {
                SetStatus("ENTER A VALID RELAY JOIN CODE");
                return;
            }
            _busy = true;
            SetStatus($"JOINING RELAY CODE {code}...");
            var res = await VanguardNetServices.Session.JoinByCodeAsync(code);
            _busy = false;
            if (res.Success)
            {
                VanguardAudioManager.Instance?.Play2D("ui_match_found", AudioBus.UI, 0.85f);
                ShowLobbyView();
            }
            else
            {
                SetStatus($"JOIN FAILED: {res.ErrorMessage}");
            }
        }

        private async Task OnJoinLanClicked()
        {
            if (_busy || VanguardNetServices.Session == null) return;
            string ip = string.IsNullOrWhiteSpace(_lanIpInput.text) ? "127.0.0.1" : _lanIpInput.text.Trim();
            _busy = true;
            SetStatus($"CONNECTING TO LAN {ip}:7777...");
            var res = await VanguardNetServices.Session.JoinLanDirectAsync(ip, 7777);
            _busy = false;
            if (res.Success) ShowLobbyView();
            else SetStatus($"LAN JOIN FAILED: {res.ErrorMessage}");
        }

        private async Task RefreshBrowserLobbies()
        {
            if (_browserListContainer == null || VanguardNetServices.Session == null) return;
            for (int i = _browserListContainer.childCount - 1; i >= 0; i--)
                Destroy(_browserListContainer.GetChild(i).gameObject);

            _browserEmptyLabel.gameObject.SetActive(true);
            _browserEmptyLabel.text = "QUERYING UNITY MULTIPLAYER SERVICES...";

            var lobbies = await VanguardNetServices.Session.QueryOpenLobbiesAsync();
            if (lobbies == null || lobbies.Count == 0)
            {
                _browserEmptyLabel.text = "NO OPEN PUBLIC OPERATIONS FOUND — HOST A MATCH OR LAUNCH QUICK MATCH";
                return;
            }

            _browserEmptyLabel.gameObject.SetActive(false);
            foreach (var lobby in lobbies)
            {
                var captured = lobby;
                string rowText = $"{lobby.SessionName}   |   {lobby.Mode}   |   {lobby.CurrentPlayers}/{lobby.MaxPlayers}   |   HOST: {lobby.HostName}";
                VanguardUiFactory.CreateTacticalButton($"Lobby_{lobby.SessionId}", _browserListContainer, rowText, VanguardPalette.AccentCyan, async () =>
                {
                    SetStatus($"JOINING {captured.SessionName}...");
                    var r = await VanguardNetServices.Session.JoinSessionByIdAsync(captured.SessionId);
                    if (r.Success) ShowLobbyView();
                    else SetStatus($"JOIN FAILED: {r.ErrorMessage}");
                }, 46f, 14f, false);
            }
        }

        private void HandleSessionJoined(LobbySessionInfo info)
        {
            ShowLobbyView();
        }

        private void HandleSessionLeft(string reason)
        {
            ShowScreen(_homePanel);
            if (!string.IsNullOrEmpty(reason)) SetStatus(reason);
        }

        private void ShowLobbyView()
        {
            var svc = VanguardNetServices.Session;
            if (svc == null) return;
            _lobbyJoinCodeLabel.text = string.IsNullOrEmpty(svc.CurrentJoinCode)
                ? $"CONNECTION: {svc.ActiveConnectionMode.ToString().ToUpper()}"
                : $"JOIN CODE: {svc.CurrentJoinCode}";
            _lobbyModeLabel.text = $"BLACKSITE-09  //  {svc.ActiveRules.Mode.ToString().ToUpper()}  //  {(svc.IsHost ? "HOST AUTHORITY" : "CLIENT")}";
            if (_lobbyStartButton != null) _lobbyStartButton.gameObject.SetActive(svc.IsHost);
            ShowScreen(_lobbyPanel);
        }

        private void RefreshLobbyRoster(IReadOnlyList<PlayerCombatStats> roster)
        {
            if (_lobbyRosterContainer == null) return;
            for (int i = _lobbyRosterContainer.childCount - 1; i >= 0; i--)
                Destroy(_lobbyRosterContainer.GetChild(i).gameObject);

            if (roster == null) return;
            foreach (var p in roster)
            {
                var row = VanguardUiFactory.CreatePanel($"Row_{p.Callsign}", _lobbyRosterContainer, VanguardPalette.HudButtonNormal, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, true, false);
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
                VanguardUiFactory.AddAccentStripe(row.transform, VanguardPalette.GetTeamColor((int)p.Team), true, 4f);

                string tag = p.IsHost ? "[HOST]" : (p.IsBot ? "[BOT]" : "[OP]");
                string readyStr = p.IsReady ? "READY" : "STANDBY";
                var lbl = VanguardUiFactory.CreateLabel("Txt", row.transform, $"{tag}  {p.Callsign}   //   TEAM {p.Team.ToString().ToUpper()}   //   {readyStr}", 15f, VanguardPalette.HudLight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 1.5f);
                lbl.rectTransform.anchorMin = Vector2.zero;
                lbl.rectTransform.anchorMax = Vector2.one;
                lbl.rectTransform.offsetMin = new Vector2(18f, 0f);
                lbl.rectTransform.offsetMax = new Vector2(-12f, 0f);
            }
        }

        private async Task OnStartMatchFromLobby()
        {
            if (_busy || VanguardNetServices.Session == null) return;
            _busy = true;
            SetStatus("LAUNCHING BLACKSITE-09 DEPLOYMENT...");
            await VanguardNetServices.Session.StartMatchFromLobbyAsync();
            _busy = false;
        }

        private async Task OnLeaveLobbyClicked()
        {
            if (VanguardNetServices.Session != null)
                await VanguardNetServices.Session.LeaveSessionAsync("Returned to Main Menu");
            ShowScreen(_homePanel);
        }

        private static void OnQuitClicked()
        {
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}
