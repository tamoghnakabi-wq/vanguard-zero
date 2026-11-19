using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VanguardZero.Networking
{
    /// <summary>
    /// Manages Unity Gaming Services initialization, Anonymous Authentication, Player Callsign,
    /// Unity Multiplayer Services Sessions (Host Public/Private, Browse Open Lobbies, Join by Code, Quick Play Matchmaking)
    /// over Unity Relay (WithRelayNetwork), plus LAN Direct and Offline Solo Skirmish fallbacks.
    /// </summary>
    public class VanguardSessionManager : MonoBehaviour, INetworkSessionService
    {
        private const string CallsignPrefsKey = "VanguardZero_PlayerCallsign";
        private const string PropKeyMode = "mode";
        private const string PropKeyMap = "map";
        private const string PropKeyPhase = "phase";
        private const string PropKeyHost = "host";
        private const string PropKeyBots = "bots";

        private const string PlayerPropCallsign = "callsign";
        private const string PlayerPropTeam = "team";
        private const string PlayerPropReady = "ready";

        public static VanguardSessionManager Instance { get; private set; }

        public CloudServiceState CloudState { get; private set; } = CloudServiceState.Uninitialized;
        public string CloudStatusBanner { get; private set; } = "Initializing tactical network services...";
        public ConnectionMode ActiveConnectionMode { get; private set; } = ConnectionMode.OfflineSolo;
        public string LocalPlayerId { get; private set; } = string.Empty;
        public string LocalCallsign { get; private set; } = "VANGUARD-01";
        public string CurrentJoinCode { get; private set; } = string.Empty;
        public bool IsInSession => m_CurrentSession != null || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening);
        public bool IsHost => (m_CurrentSession != null && m_CurrentSession.IsHost) || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost);
        public bool IsOfflineSolo => ActiveConnectionMode == ConnectionMode.OfflineSolo && IsInSession;
        public MatchRulesConfig ActiveRules => m_ActiveRules;

        public event Action<CloudServiceState, string> OnCloudStateChanged;
        public event Action<LobbySessionInfo> OnSessionJoined;
        public event Action<string> OnSessionLeft;
        public event Action<IReadOnlyList<PlayerCombatStats>> OnLobbyRosterChanged;
        public event Action<MatchRulesConfig> OnLobbyRulesChanged;

        private ISession m_CurrentSession;
        private IHostSession m_CurrentHostSession;
        private MatchRulesConfig m_ActiveRules = MatchRulesConfig.CreateDefault(MatchMode.TeamDeathmatch);
        private TeamId m_LocalPreferredTeam = TeamId.Alpha;
        private bool m_LocalReady = true;
        private bool m_IsLeaving;
        private readonly List<PlayerCombatStats> m_LobbyRosterCache = new List<PlayerCombatStats>(8);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticInstance()
        {
            Instance = null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            VanguardNetServices.RegisterSessionService(this);
            LoadOrGenerateCallsign();
        }

        private async void Start()
        {
            await InitializeServicesAsync();
        }

        private void OnDestroy()
        {
            UnsubscribeSessionEvents();
            if (Instance == this)
            {
                VanguardNetServices.UnregisterSessionService(this);
                Instance = null;
            }
        }

        private void LoadOrGenerateCallsign()
        {
            string saved = PlayerPrefs.GetString(CallsignPrefsKey, string.Empty);
            if (string.IsNullOrWhiteSpace(saved))
            {
                int suffix = UnityEngine.Random.Range(100, 999);
                saved = $"OPERATOR-{suffix}";
            }
            LocalCallsign = SanitizeCallsign(saved);
        }

        public static string SanitizeCallsign(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return $"OPERATOR-{UnityEngine.Random.Range(100, 999)}";
            }

            string trimmed = raw.Trim().ToUpperInvariant();
            if (trimmed.Length > 16)
            {
                trimmed = trimmed.Substring(0, 16);
            }
            return trimmed;
        }

        public async Task<SessionOpResult> InitializeServicesAsync(string callsign = null)
        {
            if (!string.IsNullOrWhiteSpace(callsign))
            {
                LocalCallsign = SanitizeCallsign(callsign);
                PlayerPrefs.SetString(CallsignPrefsKey, LocalCallsign);
            }

            if (CloudState == CloudServiceState.Ready && AuthenticationService.Instance.IsSignedIn)
            {
                LocalPlayerId = AuthenticationService.Instance.PlayerId;
                return SessionOpResult.Ok(ActiveConnectionMode, CurrentJoinCode);
            }

            SetCloudState(CloudServiceState.Initializing, "Connecting to Unity Cloud & Relay Services...");

            try
            {
                // Coordinate with scaffolded Assets/PlayerAuthentication/PlayerAuthentication.cs
                Task<bool> authTask = global::PlayerAuthentication.SignedIn;
                Task completed = await Task.WhenAny(authTask, Task.Delay(12000));
                bool signedIn = completed == authTask && await authTask;

                if (!signedIn)
                {
                    if (UnityServices.State != ServicesInitializationState.Initialized)
                    {
                        await UnityServices.InitializeAsync();
                    }
                    if (!AuthenticationService.Instance.IsSignedIn)
                    {
                        await AuthenticationService.Instance.SignInAnonymouslyAsync();
                    }
                    signedIn = AuthenticationService.Instance.IsSignedIn;
                }

                if (signedIn && AuthenticationService.Instance.IsSignedIn)
                {
                    LocalPlayerId = AuthenticationService.Instance.PlayerId;
                    SetCloudState(
                        CloudServiceState.Ready,
                        $"UNITY RELAY ONLINE — OPERATOR: {LocalCallsign} [{LocalPlayerId.Substring(0, Mathf.Min(6, LocalPlayerId.Length))}]");
                    return SessionOpResult.Ok(ConnectionMode.UnityRelay);
                }

                LocalPlayerId = $"LOCAL-{SystemInfo.deviceUniqueIdentifier.GetHashCode():X6}";
                SetCloudState(
                    CloudServiceState.UnavailableFallback,
                    "Unity Cloud Relay offline — LAN Direct & Solo Skirmish modes active.");
                return SessionOpResult.Fail("Unity Cloud authentication unavailable; using local fallback.", ConnectionMode.OfflineSolo);
            }
            catch (Exception ex)
            {
                LocalPlayerId = $"LOCAL-{UnityEngine.Random.Range(100000, 999999)}";
                SetCloudState(
                    CloudServiceState.UnavailableFallback,
                    $"Unity Cloud Relay unconfigured ({ex.GetType().Name}) — LAN Direct & Solo Skirmish active.");
                return SessionOpResult.Fail(ex.Message, ConnectionMode.OfflineSolo);
            }
        }

        public async Task SetCallsignAsync(string callsign)
        {
            LocalCallsign = SanitizeCallsign(callsign);
            PlayerPrefs.SetString(CallsignPrefsKey, LocalCallsign);
            PlayerPrefs.Save();

            if (m_CurrentSession?.CurrentPlayer != null)
            {
                try
                {
                    m_CurrentSession.CurrentPlayer.SetProperty(PlayerPropCallsign, new PlayerProperty(LocalCallsign, VisibilityPropertyOptions.Member));
                    await m_CurrentSession.SaveCurrentPlayerDataAsync();
                    RebuildAndBroadcastLobbyRoster();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[VanguardSessionManager] Failed to update session player callsign: {ex.Message}");
                }
            }

            if (CloudState == CloudServiceState.Ready)
            {
                SetCloudState(CloudServiceState.Ready, $"UNITY RELAY ONLINE — OPERATOR: {LocalCallsign}");
            }
        }

        public async Task<SessionOpResult> HostSessionAsync(HostSessionRequest request)
        {
            await LeaveSessionIfActiveAsync();

            if (CloudState != CloudServiceState.Ready)
            {
                await InitializeServicesAsync();
            }

            ApplyRulesFromHostRequest(request);

            if (CloudState != CloudServiceState.Ready)
            {
                SetCloudState(
                    CloudServiceState.UnavailableFallback,
                    "Relay unavailable — automatically hosting on LAN Direct (127.0.0.1:7777).");
                return await HostLanDirectAsync(request, 7777);
            }

            try
            {
                EnsureNetworkManagerPreparedForSession();

                var options = BuildSessionOptions(request).WithRelayNetwork();
                m_CurrentHostSession = await MultiplayerService.Instance.CreateSessionAsync(options);
                m_CurrentSession = m_CurrentHostSession;
                ActiveConnectionMode = ConnectionMode.UnityRelay;
                CurrentJoinCode = m_CurrentSession.Code ?? string.Empty;

                SubscribeSessionEvents(m_CurrentSession);
                NotifySessionJoinedFromCurrent();
                RebuildAndBroadcastLobbyRoster();

                return SessionOpResult.Ok(ConnectionMode.UnityRelay, CurrentJoinCode);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VanguardSessionManager] Relay HostSessionAsync failed ({ex.Message}). Falling back to LAN Direct.");
                SetCloudState(
                    CloudServiceState.UnavailableFallback,
                    $"Relay allocation unavailable ({ex.Message}) — hosted via LAN Direct (port 7777).");
                return await HostLanDirectAsync(request, 7777);
            }
        }

        public async Task<SessionOpResult> HostLanDirectAsync(HostSessionRequest request, ushort port = 7777)
        {
            await LeaveSessionIfActiveAsync();
            ApplyRulesFromHostRequest(request);

            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                return SessionOpResult.Fail("NetworkManager.Singleton is not present in the scene.", ConnectionMode.LanDirect);
            }

            if (nm.IsListening)
            {
                nm.Shutdown();
            }

            EnsureNetworkManagerPreparedForSession();
            var transport = nm.GetComponent<UnityTransport>();
            if (transport != null)
            {
                ushort actualPort = port > 0 ? port : GetAvailableUdpPort();
                transport.SetConnectionData("127.0.0.1", actualPort, "0.0.0.0");
                CurrentJoinCode = $"127.0.0.1:{actualPort}";
            }
            else
            {
                CurrentJoinCode = $"127.0.0.1:{port}";
            }

            ActiveConnectionMode = ConnectionMode.LanDirect;
            bool started = nm.StartHost();
            if (!started)
            {
                return SessionOpResult.Fail("Failed to start LAN Direct Host.", ConnectionMode.LanDirect);
            }

            var info = new LobbySessionInfo
            {
                SessionId = "LAN-DIRECT",
                SessionName = string.IsNullOrWhiteSpace(request.SessionName) ? $"{LocalCallsign} LAN" : request.SessionName,
                JoinCode = CurrentJoinCode,
                HostName = LocalCallsign,
                Mode = m_ActiveRules.Mode,
                MapName = m_ActiveRules.MapName,
                CurrentPlayers = 1,
                MaxPlayers = m_ActiveRules.MaxPlayers,
                IsPrivate = request.IsPrivate,
                InProgress = false,
                BotsEnabled = m_ActiveRules.FillWithBots
            };

            OnSessionJoined?.Invoke(info);
            RebuildAndBroadcastLobbyRoster();
            return SessionOpResult.Ok(ConnectionMode.LanDirect, CurrentJoinCode);
        }

        public async Task<SessionOpResult> JoinByCodeAsync(string joinCode)
        {
            if (string.IsNullOrWhiteSpace(joinCode))
            {
                return SessionOpResult.Fail("Enter a valid session Join Code.");
            }

            string cleaned = joinCode.Trim();

            // Support direct IP:Port passed into the Join Code box for seamless LAN fallback!
            if (cleaned.Contains(":") || cleaned.Contains("."))
            {
                string ip = "127.0.0.1";
                ushort port = 7777;
                string[] parts = cleaned.Split(':');
                if (parts.Length >= 1 && !string.IsNullOrWhiteSpace(parts[0]))
                {
                    ip = parts[0].Trim();
                }
                if (parts.Length >= 2 && ushort.TryParse(parts[1].Trim(), out ushort parsedPort))
                {
                    port = parsedPort;
                }
                return await JoinLanDirectAsync(ip, port);
            }

            await LeaveSessionIfActiveAsync();

            if (CloudState != CloudServiceState.Ready)
            {
                await InitializeServicesAsync();
            }

            if (CloudState != CloudServiceState.Ready)
            {
                return SessionOpResult.Fail("Unity Cloud Relay is offline. Enter IP:Port (e.g. 127.0.0.1:7777) for LAN Direct.");
            }

            try
            {
                EnsureNetworkManagerPreparedForSession();
                var joinOptions = BuildJoinSessionOptions();
                m_CurrentSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(cleaned.ToUpperInvariant(), joinOptions);
                m_CurrentHostSession = m_CurrentSession.IsHost ? m_CurrentSession.AsHost() : null;
                ActiveConnectionMode = ConnectionMode.UnityRelay;
                CurrentJoinCode = m_CurrentSession.Code ?? cleaned.ToUpperInvariant();

                ReadRulesFromSessionProperties(m_CurrentSession.Properties);
                SubscribeSessionEvents(m_CurrentSession);
                NotifySessionJoinedFromCurrent();
                RebuildAndBroadcastLobbyRoster();

                return SessionOpResult.Ok(ConnectionMode.UnityRelay, CurrentJoinCode);
            }
            catch (Exception ex)
            {
                return SessionOpResult.Fail($"Failed to join code '{cleaned}': {ex.Message}", ConnectionMode.UnityRelay);
            }
        }

        public async Task<SessionOpResult> JoinSessionByIdAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return SessionOpResult.Fail("Invalid Session ID.");
            }

            await LeaveSessionIfActiveAsync();

            if (CloudState != CloudServiceState.Ready)
            {
                await InitializeServicesAsync();
            }

            try
            {
                EnsureNetworkManagerPreparedForSession();
                var joinOptions = BuildJoinSessionOptions();
                m_CurrentSession = await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId.Trim(), joinOptions);
                m_CurrentHostSession = m_CurrentSession.IsHost ? m_CurrentSession.AsHost() : null;
                ActiveConnectionMode = ConnectionMode.UnityRelay;
                CurrentJoinCode = m_CurrentSession.Code ?? string.Empty;

                ReadRulesFromSessionProperties(m_CurrentSession.Properties);
                SubscribeSessionEvents(m_CurrentSession);
                NotifySessionJoinedFromCurrent();
                RebuildAndBroadcastLobbyRoster();

                return SessionOpResult.Ok(ConnectionMode.UnityRelay, CurrentJoinCode);
            }
            catch (Exception ex)
            {
                return SessionOpResult.Fail($"Failed to join session: {ex.Message}", ConnectionMode.UnityRelay);
            }
        }

        public async Task<SessionOpResult> JoinLanDirectAsync(string ipAddress, ushort port = 7777)
        {
            await LeaveSessionIfActiveAsync();

            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                return SessionOpResult.Fail("NetworkManager.Singleton is missing.", ConnectionMode.LanDirect);
            }

            if (nm.IsListening)
            {
                nm.Shutdown();
            }

            string targetIp = string.IsNullOrWhiteSpace(ipAddress) ? "127.0.0.1" : ipAddress.Trim();
            EnsureNetworkManagerPreparedForSession();

            var transport = nm.GetComponent<UnityTransport>();
            if (transport != null)
            {
                transport.SetConnectionData(targetIp, port);
            }

            ActiveConnectionMode = ConnectionMode.LanDirect;
            CurrentJoinCode = $"{targetIp}:{port}";

            bool started = nm.StartClient();
            if (!started)
            {
                return SessionOpResult.Fail($"Failed to connect to {targetIp}:{port}.", ConnectionMode.LanDirect);
            }

            return SessionOpResult.Ok(ConnectionMode.LanDirect, CurrentJoinCode);
        }

        public async Task<IReadOnlyList<LobbySessionInfo>> QueryOpenLobbiesAsync(MatchMode? modeFilter = null)
        {
            var results = new List<LobbySessionInfo>();
            if (CloudState != CloudServiceState.Ready)
            {
                await InitializeServicesAsync();
            }

            if (CloudState != CloudServiceState.Ready)
            {
                return results;
            }

            try
            {
                var queryOptions = new QuerySessionsOptions
                {
                    Count = 25,
                    FilterOptions = new List<FilterOption>
                    {
                        new FilterOption(FilterField.AvailableSlots, "0", FilterOperation.Greater),
                        new FilterOption(FilterField.IsLocked, "0", FilterOperation.Equal)
                    },
                    SortOptions = new List<SortOption>
                    {
                        new SortOption(SortOrder.Descending, SortField.AvailableSlots),
                        new SortOption(SortOrder.Descending, SortField.CreationTime)
                    }
                };

                if (modeFilter.HasValue)
                {
                    queryOptions.FilterOptions.Add(new FilterOption(
                        FilterField.StringIndex1,
                        modeFilter.Value.ToString(),
                        FilterOperation.Equal));
                }

                QuerySessionsResults queryResults = await MultiplayerService.Instance.QuerySessionsAsync(queryOptions);
                if (queryResults?.Sessions != null)
                {
                    foreach (ISessionInfo s in queryResults.Sessions)
                    {
                        results.Add(ConvertSessionInfo(s));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VanguardSessionManager] QueryOpenLobbiesAsync failed: {ex.Message}");
            }

            return results;
        }

        public async Task<SessionOpResult> QuickPlayAsync(MatchMode preferredMode = MatchMode.TeamDeathmatch)
        {
            await LeaveSessionIfActiveAsync();

            if (CloudState != CloudServiceState.Ready)
            {
                await InitializeServicesAsync();
            }

            var hostReq = HostSessionRequest.CreateDefault(preferredMode, isPrivate: false);
            hostReq.SessionName = $"{LocalCallsign} QUICK OPS";
            ApplyRulesFromHostRequest(hostReq);

            if (CloudState != CloudServiceState.Ready)
            {
                SetCloudState(
                    CloudServiceState.UnavailableFallback,
                    "Cloud Matchmaking unavailable — launching Solo Skirmish vs Tactical AI Bots.");
                return await StartSoloSkirmishAsync(m_ActiveRules);
            }

            try
            {
                EnsureNetworkManagerPreparedForSession();

                // Per entrypoints.md: QuickJoinOptions with CreateSession = true and NO unrequested Timeout override
                var quickJoinOptions = new QuickJoinOptions
                {
                    CreateSession = true,
                    Filters = new List<FilterOption>
                    {
                        new FilterOption(FilterField.StringIndex1, preferredMode.ToString(), FilterOperation.Equal)
                    }
                };

                var sessionOptions = BuildSessionOptions(hostReq).WithRelayNetwork();
                m_CurrentSession = await MultiplayerService.Instance.MatchmakeSessionAsync(quickJoinOptions, sessionOptions);
                m_CurrentHostSession = m_CurrentSession.IsHost ? m_CurrentSession.AsHost() : null;
                ActiveConnectionMode = ConnectionMode.UnityRelay;
                CurrentJoinCode = m_CurrentSession.Code ?? string.Empty;

                ReadRulesFromSessionProperties(m_CurrentSession.Properties);
                SubscribeSessionEvents(m_CurrentSession);
                NotifySessionJoinedFromCurrent();
                RebuildAndBroadcastLobbyRoster();

                return SessionOpResult.Ok(ConnectionMode.UnityRelay, CurrentJoinCode);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VanguardSessionManager] QuickPlay Relay Matchmaking failed ({ex.Message}). Launching Solo Skirmish fallback.");
                SetCloudState(
                    CloudServiceState.UnavailableFallback,
                    $"Quick Play Relay unavailable ({ex.Message}) — launching Solo Skirmish vs Bots.");
                return await StartSoloSkirmishAsync(m_ActiveRules);
            }
        }

        public async Task<SessionOpResult> StartSoloSkirmishAsync(MatchRulesConfig rules)
        {
            await LeaveSessionIfActiveAsync();

            if (string.IsNullOrWhiteSpace(rules.MapName))
            {
                rules.MapName = VanguardNetServices.GameplaySceneName;
            }
            if (rules.ScoreLimit <= 0)
            {
                rules.ScoreLimit = rules.Mode == MatchMode.FreeForAll ? 15 : 25;
            }
            if (rules.MatchDurationSeconds <= 0f)
            {
                rules.MatchDurationSeconds = 300f;
            }
            if (rules.RespawnDelaySeconds <= 0f)
            {
                rules.RespawnDelaySeconds = 3.5f;
            }
            if (rules.MaxPlayers <= 0)
            {
                rules.MaxPlayers = 8;
            }
            if (rules.TargetCombatantCount <= 0)
            {
                rules.TargetCombatantCount = 6;
            }
            rules.FillWithBots = true;

            m_ActiveRules = rules;
            ActiveConnectionMode = ConnectionMode.OfflineSolo;
            CurrentJoinCode = "SOLO-SKIRMISH";

            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                return SessionOpResult.Fail("NetworkManager.Singleton is missing from scene.", ConnectionMode.OfflineSolo);
            }

            if (nm.IsListening)
            {
                nm.Shutdown();
            }

            EnsureNetworkManagerPreparedForSession();
            var transport = nm.GetComponent<UnityTransport>();
            if (transport != null)
            {
                ushort dynamicPort = GetAvailableUdpPort();
                transport.SetConnectionData("127.0.0.1", dynamicPort);
            }

            bool started = nm.StartHost();
            if (!started)
            {
                return SessionOpResult.Fail("Failed to start local Solo Skirmish host.", ConnectionMode.OfflineSolo);
            }

            LoadGameplaySceneAuthoritatively();
            return SessionOpResult.Ok(ConnectionMode.OfflineSolo, CurrentJoinCode);
        }

        public async Task SetLocalReadyAsync(bool isReady)
        {
            m_LocalReady = isReady;
            if (m_CurrentSession?.CurrentPlayer != null)
            {
                try
                {
                    m_CurrentSession.CurrentPlayer.SetProperty(PlayerPropReady, new PlayerProperty(isReady ? "1" : "0", VisibilityPropertyOptions.Member));
                    await m_CurrentSession.SaveCurrentPlayerDataAsync();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[VanguardSessionManager] SetLocalReadyAsync failed: {ex.Message}");
                }
            }

            VanguardNetServices.Match?.RequestToggleReady();
            RebuildAndBroadcastLobbyRoster();
        }

        public async Task SetLocalPreferredTeamAsync(TeamId team)
        {
            m_LocalPreferredTeam = team;
            if (m_CurrentSession?.CurrentPlayer != null)
            {
                try
                {
                    m_CurrentSession.CurrentPlayer.SetProperty(PlayerPropTeam, new PlayerProperty(team.ToString(), VisibilityPropertyOptions.Member));
                    await m_CurrentSession.SaveCurrentPlayerDataAsync();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[VanguardSessionManager] SetLocalPreferredTeamAsync failed: {ex.Message}");
                }
            }

            VanguardNetServices.Match?.RequestTeamSwitch(team);
            RebuildAndBroadcastLobbyRoster();
        }

        public async Task UpdateLobbyRulesAsync(MatchRulesConfig rules)
        {
            m_ActiveRules = rules;
            OnLobbyRulesChanged?.Invoke(m_ActiveRules);

            if (m_CurrentHostSession != null)
            {
                try
                {
                    m_CurrentHostSession.SetProperty(PropKeyMode, new SessionProperty(rules.Mode.ToString(), VisibilityPropertyOptions.Public, PropertyIndex.String1));
                    m_CurrentHostSession.SetProperty(PropKeyMap, new SessionProperty(rules.MapName ?? VanguardNetServices.GameplaySceneName, VisibilityPropertyOptions.Public, PropertyIndex.String2));
                    m_CurrentHostSession.SetProperty(PropKeyBots, new SessionProperty(rules.FillWithBots ? "1" : "0", VisibilityPropertyOptions.Public, PropertyIndex.String5));
                    await m_CurrentHostSession.SavePropertiesAsync();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[VanguardSessionManager] UpdateLobbyRulesAsync failed: {ex.Message}");
                }
            }
        }

        public async Task StartMatchFromLobbyAsync()
        {
            if (!IsHost)
            {
                return;
            }

            if (m_CurrentHostSession != null)
            {
                try
                {
                    m_CurrentHostSession.SetProperty(PropKeyPhase, new SessionProperty(MatchPhase.InProgress.ToString(), VisibilityPropertyOptions.Public, PropertyIndex.String3));
                    await m_CurrentHostSession.SavePropertiesAsync();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[VanguardSessionManager] StartMatchFromLobbyAsync property save failed: {ex.Message}");
                }
            }

            LoadGameplaySceneAuthoritatively();
        }

        public async Task LeaveSessionAsync(string reason = null)
        {
            if (m_IsLeaving)
            {
                return;
            }
            m_IsLeaving = true;

            try
            {
                await LeaveSessionIfActiveAsync();

                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                Scene activeScene = SceneManager.GetActiveScene();
                if (!string.Equals(activeScene.name, VanguardNetServices.MainMenuSceneName, StringComparison.OrdinalIgnoreCase)
                    && Application.CanStreamedLevelBeLoaded(VanguardNetServices.MainMenuSceneName))
                {
                    SceneManager.LoadScene(VanguardNetServices.MainMenuSceneName, LoadSceneMode.Single);
                }

                OnSessionLeft?.Invoke(reason ?? "Left match session.");
            }
            finally
            {
                m_IsLeaving = false;
            }
        }

        private async Task LeaveSessionIfActiveAsync()
        {
            UnsubscribeSessionEvents();

            ISession sessionToClose = m_CurrentSession;
            IHostSession hostToClose = m_CurrentHostSession;
            m_CurrentSession = null;
            m_CurrentHostSession = null;
            CurrentJoinCode = string.Empty;

            if (hostToClose != null)
            {
                try
                {
                    await hostToClose.DeleteAsync();
                }
                catch (Exception)
                {
                    // Session may already be closed
                }
            }
            else if (sessionToClose != null)
            {
                try
                {
                    await sessionToClose.LeaveAsync();
                }
                catch (Exception)
                {
                    // Session may already be closed
                }
            }

            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
            {
                nm.Shutdown();
            }
        }

        private void LoadGameplaySceneAuthoritatively()
        {
            string targetScene = string.IsNullOrWhiteSpace(m_ActiveRules.MapName)
                ? VanguardNetServices.GameplaySceneName
                : m_ActiveRules.MapName;

            Scene active = SceneManager.GetActiveScene();
            if (string.Equals(active.name, targetScene, StringComparison.OrdinalIgnoreCase))
            {
                VanguardNetServices.Match?.RequestStartMatch();
                return;
            }

            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsServer && nm.SceneManager != null && Application.CanStreamedLevelBeLoaded(targetScene))
            {
                nm.SceneManager.LoadScene(targetScene, LoadSceneMode.Single);
            }
            else if (Application.CanStreamedLevelBeLoaded(targetScene))
            {
                SceneManager.LoadScene(targetScene, LoadSceneMode.Single);
            }
        }

        private void EnsureNetworkManagerPreparedForSession()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.NetworkConfig == null)
            {
                return;
            }

            nm.NetworkConfig.TickRate = 60;
            nm.NetworkConfig.EnableSceneManagement = true;
            // NetworkMatchManager controls team-balanced player spawning
            nm.NetworkConfig.PlayerPrefab = null;
        }

        private void ApplyRulesFromHostRequest(in HostSessionRequest request)
        {
            m_ActiveRules = new MatchRulesConfig
            {
                Mode = request.Mode,
                MapName = string.IsNullOrWhiteSpace(request.MapName) ? VanguardNetServices.GameplaySceneName : request.MapName,
                MaxPlayers = Mathf.Clamp(request.MaxPlayers > 0 ? request.MaxPlayers : 8, 2, 16),
                FillWithBots = request.FillWithBots,
                TargetCombatantCount = Mathf.Clamp(request.TargetCombatantCount > 0 ? request.TargetCombatantCount : 6, 2, 12),
                ScoreLimit = request.ScoreLimit > 0 ? request.ScoreLimit : (request.Mode == MatchMode.FreeForAll ? 15 : 25),
                MatchDurationSeconds = request.MatchDurationSeconds > 30f ? request.MatchDurationSeconds : 300f,
                RespawnDelaySeconds = 3.5f
            };
        }

        private SessionOptions BuildSessionOptions(in HostSessionRequest request)
        {
            int maxPlayers = Mathf.Clamp(request.MaxPlayers > 0 ? request.MaxPlayers : 8, 2, 16);
            string sessionName = string.IsNullOrWhiteSpace(request.SessionName)
                ? $"{LocalCallsign} BLACKSITE-09"
                : request.SessionName.Trim();
            string mapName = string.IsNullOrWhiteSpace(request.MapName)
                ? VanguardNetServices.GameplaySceneName
                : request.MapName.Trim();

            return new SessionOptions
            {
                Name = sessionName,
                MaxPlayers = maxPlayers,
                IsPrivate = request.IsPrivate,
                IsLocked = false,
                SessionProperties = new Dictionary<string, SessionProperty>
                {
                    { PropKeyMode, new SessionProperty(request.Mode.ToString(), VisibilityPropertyOptions.Public, PropertyIndex.String1) },
                    { PropKeyMap, new SessionProperty(mapName, VisibilityPropertyOptions.Public, PropertyIndex.String2) },
                    { PropKeyPhase, new SessionProperty(MatchPhase.Lobby.ToString(), VisibilityPropertyOptions.Public, PropertyIndex.String3) },
                    { PropKeyHost, new SessionProperty(LocalCallsign, VisibilityPropertyOptions.Public, PropertyIndex.String4) },
                    { PropKeyBots, new SessionProperty(request.FillWithBots ? "1" : "0", VisibilityPropertyOptions.Public, PropertyIndex.String5) }
                },
                PlayerProperties = BuildLocalPlayerProperties()
            };
        }

        private JoinSessionOptions BuildJoinSessionOptions()
        {
            return new JoinSessionOptions
            {
                PlayerProperties = BuildLocalPlayerProperties()
            };
        }

        private Dictionary<string, PlayerProperty> BuildLocalPlayerProperties()
        {
            return new Dictionary<string, PlayerProperty>
            {
                { PlayerPropCallsign, new PlayerProperty(LocalCallsign, VisibilityPropertyOptions.Member) },
                { PlayerPropTeam, new PlayerProperty(m_LocalPreferredTeam.ToString(), VisibilityPropertyOptions.Member) },
                { PlayerPropReady, new PlayerProperty(m_LocalReady ? "1" : "0", VisibilityPropertyOptions.Member) }
            };
        }

        private void ReadRulesFromSessionProperties(IReadOnlyDictionary<string, SessionProperty> props)
        {
            if (props == null)
            {
                return;
            }

            if (props.TryGetValue(PropKeyMode, out var modeProp) && Enum.TryParse(modeProp?.Value, true, out MatchMode parsedMode))
            {
                m_ActiveRules.Mode = parsedMode;
            }
            if (props.TryGetValue(PropKeyMap, out var mapProp) && !string.IsNullOrWhiteSpace(mapProp?.Value))
            {
                m_ActiveRules.MapName = mapProp.Value;
            }
            if (props.TryGetValue(PropKeyBots, out var botsProp))
            {
                m_ActiveRules.FillWithBots = botsProp?.Value != "0";
            }

            OnLobbyRulesChanged?.Invoke(m_ActiveRules);
        }

        private static LobbySessionInfo ConvertSessionInfo(ISessionInfo s)
        {
            MatchMode mode = MatchMode.TeamDeathmatch;
            string map = VanguardNetServices.GameplaySceneName;
            string hostName = "HOST";
            bool inProgress = false;
            bool bots = true;

            if (s.Properties != null)
            {
                if (s.Properties.TryGetValue(PropKeyMode, out var mp) && Enum.TryParse(mp?.Value, true, out MatchMode pm))
                {
                    mode = pm;
                }
                if (s.Properties.TryGetValue(PropKeyMap, out var mapP) && !string.IsNullOrWhiteSpace(mapP?.Value))
                {
                    map = mapP.Value;
                }
                if (s.Properties.TryGetValue(PropKeyHost, out var hp) && !string.IsNullOrWhiteSpace(hp?.Value))
                {
                    hostName = hp.Value;
                }
                if (s.Properties.TryGetValue(PropKeyPhase, out var php))
                {
                    inProgress = string.Equals(php?.Value, MatchPhase.InProgress.ToString(), StringComparison.OrdinalIgnoreCase);
                }
                if (s.Properties.TryGetValue(PropKeyBots, out var bp))
                {
                    bots = bp?.Value != "0";
                }
            }

            return new LobbySessionInfo
            {
                SessionId = s.Id,
                SessionName = s.Name,
                JoinCode = string.Empty,
                HostName = hostName,
                Mode = mode,
                MapName = map,
                CurrentPlayers = Mathf.Max(0, s.MaxPlayers - s.AvailableSlots),
                MaxPlayers = s.MaxPlayers,
                IsPrivate = false,
                InProgress = inProgress,
                BotsEnabled = bots
            };
        }

        private void SubscribeSessionEvents(ISession session)
        {
            if (session == null)
            {
                return;
            }

            UnsubscribeSessionEvents();
            session.Changed += HandleSessionChanged;
            session.PlayerJoined += HandleSessionPlayerJoined;
            session.PlayerHasLeft += HandleSessionPlayerHasLeft;
            session.SessionPropertiesChanged += HandleSessionPropertiesChanged;
            session.PlayerPropertiesChanged += HandlePlayerPropertiesChanged;
            session.RemovedFromSession += HandleRemovedFromSession;
            session.Deleted += HandleSessionDeleted;
        }

        private void UnsubscribeSessionEvents()
        {
            if (m_CurrentSession == null)
            {
                return;
            }

            m_CurrentSession.Changed -= HandleSessionChanged;
            m_CurrentSession.PlayerJoined -= HandleSessionPlayerJoined;
            m_CurrentSession.PlayerHasLeft -= HandleSessionPlayerHasLeft;
            m_CurrentSession.SessionPropertiesChanged -= HandleSessionPropertiesChanged;
            m_CurrentSession.PlayerPropertiesChanged -= HandlePlayerPropertiesChanged;
            m_CurrentSession.RemovedFromSession -= HandleRemovedFromSession;
            m_CurrentSession.Deleted -= HandleSessionDeleted;
        }

        private void HandleSessionChanged()
        {
            RebuildAndBroadcastLobbyRoster();
        }

        private void HandleSessionPlayerJoined(string playerId)
        {
            RebuildAndBroadcastLobbyRoster();
        }

        private void HandleSessionPlayerHasLeft(string playerId)
        {
            RebuildAndBroadcastLobbyRoster();
        }

        private void HandleSessionPropertiesChanged()
        {
            if (m_CurrentSession != null)
            {
                ReadRulesFromSessionProperties(m_CurrentSession.Properties);
            }
        }

        private void HandlePlayerPropertiesChanged()
        {
            RebuildAndBroadcastLobbyRoster();
        }

        private async void HandleRemovedFromSession()
        {
            await LeaveSessionAsync("Removed from session by host.");
        }

        private async void HandleSessionDeleted()
        {
            await LeaveSessionAsync("Host closed the session.");
        }

        private void NotifySessionJoinedFromCurrent()
        {
            if (m_CurrentSession == null)
            {
                return;
            }

            string hostName = m_CurrentSession.IsHost ? LocalCallsign : "HOST";
            if (m_CurrentSession.Properties != null
                && m_CurrentSession.Properties.TryGetValue(PropKeyHost, out var hostProp)
                && !string.IsNullOrWhiteSpace(hostProp?.Value))
            {
                hostName = hostProp.Value;
            }

            var info = new LobbySessionInfo
            {
                SessionId = m_CurrentSession.Id,
                SessionName = m_CurrentSession.Name,
                JoinCode = m_CurrentSession.Code ?? string.Empty,
                HostName = hostName,
                Mode = m_ActiveRules.Mode,
                MapName = m_ActiveRules.MapName,
                CurrentPlayers = m_CurrentSession.PlayerCount,
                MaxPlayers = m_CurrentSession.MaxPlayers,
                IsPrivate = m_CurrentSession.IsPrivate,
                InProgress = false,
                BotsEnabled = m_ActiveRules.FillWithBots
            };

            OnSessionJoined?.Invoke(info);
        }

        public void RebuildAndBroadcastLobbyRoster()
        {
            m_LobbyRosterCache.Clear();

            if (m_CurrentSession?.Players != null && m_CurrentSession.Players.Count > 0)
            {
                byte slot = 0;
                foreach (IReadOnlyPlayer p in m_CurrentSession.Players)
                {
                    string callsign = $"OPERATOR-{slot + 1}";
                    TeamId team = (slot % 2 == 0) ? TeamId.Alpha : TeamId.Bravo;
                    bool ready = true;

                    if (p.Properties != null)
                    {
                        if (p.Properties.TryGetValue(PlayerPropCallsign, out var cp) && !string.IsNullOrWhiteSpace(cp?.Value))
                        {
                            callsign = cp.Value;
                        }
                        if (p.Properties.TryGetValue(PlayerPropTeam, out var tp) && Enum.TryParse(tp?.Value, true, out TeamId parsedTeam))
                        {
                            team = parsedTeam;
                        }
                        if (p.Properties.TryGetValue(PlayerPropReady, out var rp))
                        {
                            ready = rp?.Value != "0";
                        }
                    }

                    if (m_ActiveRules.Mode == MatchMode.FreeForAll)
                    {
                        team = TeamId.None;
                    }

                    m_LobbyRosterCache.Add(new PlayerCombatStats
                    {
                        ClientId = slot,
                        PlayerId = p.Id ?? string.Empty,
                        Callsign = callsign,
                        Team = team,
                        IsReady = ready,
                        IsHost = string.Equals(p.Id, m_CurrentSession.Host, StringComparison.Ordinal),
                        IsAlive = true,
                        IsBot = false,
                        SlotIndex = slot
                    });
                    slot++;
                }
            }
            else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                var ids = NetworkManager.Singleton.ConnectedClientsIds;
                for (int i = 0; i < ids.Count; i++)
                {
                    ulong cid = ids[i];
                    bool isLocal = cid == NetworkManager.Singleton.LocalClientId;
                    m_LobbyRosterCache.Add(new PlayerCombatStats
                    {
                        ClientId = cid,
                        PlayerId = isLocal ? LocalPlayerId : $"PEER-{cid}",
                        Callsign = isLocal ? LocalCallsign : $"OPERATOR-{cid + 1}",
                        Team = m_ActiveRules.Mode == MatchMode.FreeForAll ? TeamId.None : ((i % 2 == 0) ? TeamId.Alpha : TeamId.Bravo),
                        IsReady = true,
                        IsHost = cid == NetworkManager.ServerClientId,
                        IsAlive = true,
                        IsBot = false,
                        SlotIndex = (byte)i
                    });
                }
            }

            OnLobbyRosterChanged?.Invoke(m_LobbyRosterCache);
        }

        public bool TryResolveSessionPlayerInfo(int indexHint, out string callsign, out TeamId preferredTeam)
        {
            callsign = string.Empty;
            preferredTeam = TeamId.None;

            if (m_CurrentSession?.Players != null && indexHint >= 0 && indexHint < m_CurrentSession.Players.Count)
            {
                var p = m_CurrentSession.Players[indexHint];
                if (p.Properties != null)
                {
                    if (p.Properties.TryGetValue(PlayerPropCallsign, out var cp) && !string.IsNullOrWhiteSpace(cp?.Value))
                    {
                        callsign = cp.Value;
                    }
                    if (p.Properties.TryGetValue(PlayerPropTeam, out var tp) && Enum.TryParse(tp?.Value, true, out TeamId pt))
                    {
                        preferredTeam = pt;
                    }
                }
                return !string.IsNullOrWhiteSpace(callsign);
            }

            return false;
        }

        private void SetCloudState(CloudServiceState state, string banner)
        {
            CloudState = state;
            CloudStatusBanner = banner ?? string.Empty;
            OnCloudStateChanged?.Invoke(CloudState, CloudStatusBanner);
        }

        private static ushort GetAvailableUdpPort()
        {
            try
            {
                using var udp = new UdpClient(0);
                if (udp.Client.LocalEndPoint is IPEndPoint ep && ep.Port > 0)
                {
                    return (ushort)ep.Port;
                }
            }
            catch (Exception)
            {
                // Fallback to random high port
            }
            return (ushort)UnityEngine.Random.Range(18000, 28000);
        }
    }
}
