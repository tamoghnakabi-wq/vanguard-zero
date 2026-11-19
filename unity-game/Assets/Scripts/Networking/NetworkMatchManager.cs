using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace VanguardZero.Networking
{
    /// <summary>
    /// Authoritative Server/Host Match & Round Manager (scene-placed NetworkObject in Blacksite09).
    /// Owns MatchPhase, authoritative round timer (PhaseEndServerTime), team/FFA scores,
    /// player slot/team assignment, player & AI bot spawning/respawning, NetworkList scoreboard,
    /// kill feed RPCs (with late-join sync), and rematch flow.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkMatchManager : NetworkBehaviour, INetworkMatchService
    {
        public static NetworkMatchManager Instance { get; private set; }

        private static readonly string[] s_BotCallsigns =
        {
            "GHOST-ACTUAL", "REAPER-02", "SPECTRE-03", "NOMAD-04",
            "VIPER-05", "TITAN-06", "ECHO-07", "FALCON-08"
        };

        private const int MaxRecentKills = 8;

        [Header("Networked Combatant Prefabs")]
        [SerializeField] private GameObject m_PlayerPrefab;
        [SerializeField] private GameObject m_BotPrefab;

        [Header("Match Timing")]
        [SerializeField] private float m_CountdownDurationSeconds = 3.0f;

        private readonly NetworkVariable<NetMatchState> m_NetMatchState = new NetworkVariable<NetMatchState>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkList<NetScoreboardEntry> m_NetScoreboard = new NetworkList<NetScoreboardEntry>(
            null,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly List<PlayerCombatStats> m_ScoreboardCache = new List<PlayerCombatStats>(16);
        private readonly List<KillFeedEntry> m_RecentKills = new List<KillFeedEntry>(MaxRecentKills);
        private readonly Dictionary<ulong, NetworkPlayerBridge> m_ActivePlayers = new Dictionary<ulong, NetworkPlayerBridge>(8);
        private readonly List<NetworkBotSync> m_ActiveBots = new List<NetworkBotSync>(8);
        private readonly List<Vector3> m_EnemyPositionScratch = new List<Vector3>(16);

        private MatchRulesConfig m_Rules;
        private uint m_NextKillSequence = 1;
        private byte m_NextSlotIndex = 0;
        private int m_NextBotNameIndex = 0;
        private float m_LastPingUpdateTime;
        private float m_LastTimerBroadcastTime;
        private bool m_MatchInitializedOnServer;
        private bool m_InitialCombatantsSpawned;
        private float m_ServerSpawnWaitElapsed;

        public MatchStateSnapshot CurrentState => m_NetMatchState.Value.ToSnapshot(GetServerTime());
        public IReadOnlyList<PlayerCombatStats> Scoreboard => m_ScoreboardCache;
        public IReadOnlyList<KillFeedEntry> RecentKills => m_RecentKills;

        public event Action<MatchStateSnapshot> OnMatchStateChanged;
        public event Action<IReadOnlyList<PlayerCombatStats>> OnScoreboardUpdated;
        public event Action<KillFeedEntry> OnKillFeedEntryAdded;
        public event Action<string, WeaponId, bool, float> OnLocalPlayerEliminated;
        public event Action<Vector3, Quaternion> OnLocalPlayerRespawned;
        public event Action<MatchStateSnapshot, IReadOnlyList<PlayerCombatStats>> OnMatchEnded;

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
            VanguardNetServices.RegisterMatchService(this);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            m_NetMatchState.OnValueChanged += HandleMatchStateChanged;
            m_NetScoreboard.OnListChanged += HandleScoreboardListChanged;

            if (IsServer)
            {
                NetworkManager.OnClientConnectedCallback += ServerHandleClientConnected;
                NetworkManager.OnClientDisconnectCallback += ServerHandleClientDisconnected;
                if (NetworkManager.SceneManager != null)
                {
                    NetworkManager.SceneManager.OnLoadEventCompleted += ServerHandleSceneLoadEventCompleted;
                    NetworkManager.SceneManager.OnLoadComplete += ServerHandleClientSceneLoadComplete;
                    NetworkManager.SceneManager.OnSynchronizeComplete += ServerHandleClientSynchronizeComplete;
                }
                ServerInitializeMatch();
            }
            else
            {
                RebuildScoreboardCache();
                OnMatchStateChanged?.Invoke(CurrentState);
                RequestRecentKillsRpc();
            }
        }

        public override void OnNetworkDespawn()
        {
            m_NetMatchState.OnValueChanged -= HandleMatchStateChanged;
            m_NetScoreboard.OnListChanged -= HandleScoreboardListChanged;

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= ServerHandleClientConnected;
                NetworkManager.OnClientDisconnectCallback -= ServerHandleClientDisconnected;
                if (NetworkManager.SceneManager != null)
                {
                    NetworkManager.SceneManager.OnLoadEventCompleted -= ServerHandleSceneLoadEventCompleted;
                    NetworkManager.SceneManager.OnLoadComplete -= ServerHandleClientSceneLoadComplete;
                    NetworkManager.SceneManager.OnSynchronizeComplete -= ServerHandleClientSynchronizeComplete;
                }
            }

            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            if (Instance == this)
            {
                VanguardNetServices.UnregisterMatchService(this);
                Instance = null;
            }
            // Required by NGO 2.14.0 to dispose NetworkList<T> native memory cleanly
            base.OnDestroy();
        }

        private void Update()
        {
            if (!IsSpawned)
            {
                return;
            }

            // Periodic client-side UI countdown refresh without writing NetworkVariables every frame
            if (Time.unscaledTime - m_LastTimerBroadcastTime >= 0.25f)
            {
                m_LastTimerBroadcastTime = Time.unscaledTime;
                MatchPhase phase = m_NetMatchState.Value.Phase;
                if (phase == MatchPhase.Countdown || phase == MatchPhase.InProgress)
                {
                    OnMatchStateChanged?.Invoke(CurrentState);
                }
            }

            if (!IsServer)
            {
                return;
            }

            // Fallback spawn trigger for direct-in-Editor Blacksite09 Play Mode (where SceneManager.LoadScene wasn't invoked)
            if (m_MatchInitializedOnServer && !m_InitialCombatantsSpawned)
            {
                m_ServerSpawnWaitElapsed += Time.unscaledDeltaTime;
                bool soloLocalOnly = NetworkManager.ConnectedClientsIds.Count <= 1;
                float timeout = soloLocalOnly ? 0.35f : 6.0f;
                if (m_ServerSpawnWaitElapsed >= timeout)
                {
                    ServerSpawnInitialCombatants(NetworkManager.ConnectedClientsIds);
                }
            }

            double now = GetServerTime();
            ServerTickPhaseTransitions(now);
            ServerTickRespawns(now);
            ServerTickPings();
        }

        // =====================================================================
        // INetworkMatchService Client Requests
        // =====================================================================

        public void RequestTeamSwitch(TeamId requestedTeam)
        {
            if (IsSpawned)
            {
                RequestTeamSwitchRpc((byte)requestedTeam);
            }
        }

        public void RequestToggleReady()
        {
            if (IsSpawned)
            {
                RequestToggleReadyRpc();
            }
        }

        public void RequestStartMatch()
        {
            if (IsSpawned && IsServer)
            {
                ServerBeginCountdown();
            }
        }

        public void RequestRematch()
        {
            if (IsSpawned)
            {
                RequestRematchRpc();
            }
        }

        public bool TryGetLocalPlayerStats(out PlayerCombatStats stats)
        {
            ulong localId = NetworkManager != null ? NetworkManager.LocalClientId : 0UL;
            for (int i = 0; i < m_ScoreboardCache.Count; i++)
            {
                if (!m_ScoreboardCache[i].IsBot && m_ScoreboardCache[i].ClientId == localId)
                {
                    stats = m_ScoreboardCache[i];
                    return true;
                }
            }
            stats = default;
            return false;
        }

        public void SetCombatantPrefabs(GameObject playerPrefab, GameObject botPrefab)
        {
            m_PlayerPrefab = playerPrefab;
            m_BotPrefab = botPrefab;
        }

        internal void NotifyLocalPlayerRespawnedClient(Vector3 pos, Quaternion rot)
        {
            OnLocalPlayerRespawned?.Invoke(pos, rot);
        }

        // =====================================================================
        // Server Match Initialization & Combatant Lifecycle
        // =====================================================================

        private void ServerInitializeMatch()
        {
            if (m_MatchInitializedOnServer)
            {
                return;
            }
            m_MatchInitializedOnServer = true;

            var session = VanguardNetServices.Session;
            m_Rules = session != null
                ? session.ActiveRules
                : MatchRulesConfig.CreateDefault(MatchMode.TeamDeathmatch);

            if (m_Rules.ScoreLimit <= 0)
            {
                m_Rules.ScoreLimit = m_Rules.Mode == MatchMode.FreeForAll ? 15 : 25;
            }
            if (m_Rules.MatchDurationSeconds <= 10f)
            {
                m_Rules.MatchDurationSeconds = 300f;
            }
            if (m_Rules.RespawnDelaySeconds <= 0.5f)
            {
                m_Rules.RespawnDelaySeconds = 3.5f;
            }
            if (m_Rules.TargetCombatantCount <= 0)
            {
                m_Rules.TargetCombatantCount = 6;
            }

            m_NetScoreboard.Clear();
            m_ActivePlayers.Clear();
            m_ActiveBots.Clear();
            m_NextSlotIndex = 0;

            string joinCode = session != null ? session.CurrentJoinCode : string.Empty;
            ConnectionMode connMode = session != null ? session.ActiveConnectionMode : ConnectionMode.OfflineSolo;

            m_NetMatchState.Value = new NetMatchState
            {
                PhaseRaw = (byte)MatchPhase.Countdown,
                ModeRaw = (byte)m_Rules.Mode,
                WinningTeamRaw = (byte)TeamId.None,
                ConnectionModeRaw = (byte)connMode,
                AlphaScore = 0,
                BravoScore = 0,
                ScoreLimit = (short)m_Rules.ScoreLimit,
                PhaseEndServerTime = GetServerTime() + m_CountdownDurationSeconds,
                MapName = NetWireHelpers.ToFixed32(m_Rules.MapName ?? VanguardNetServices.GameplaySceneName),
                WinnerCallsign = default,
                JoinCode = NetWireHelpers.ToFixed32(joinCode ?? string.Empty)
            };

            // Defer player/bot spawning until NetworkSceneManager.OnLoadEventCompleted so remote clients
            // have finished loading Blacksite09 before receiving CreateObjectMessage for dynamically spawned combatants.
            // (Direct-in-Editor Blacksite09 Play Mode triggers ServerSpawnInitialCombatants via Update fallback.)
            m_InitialCombatantsSpawned = false;
            m_ServerSpawnWaitElapsed = 0f;
            RebuildScoreboardCache();
        }

        private void ServerSpawnInitialCombatants(IReadOnlyList<ulong> readyClientIds)
        {
            if (!IsServer || m_InitialCombatantsSpawned)
            {
                return;
            }
            m_InitialCombatantsSpawned = true;

            if (readyClientIds != null)
            {
                for (int i = 0; i < readyClientIds.Count; i++)
                {
                    ServerSpawnPlayerForClient(readyClientIds[i]);
                }
            }

            // Ensure every currently connected client has a spawned avatar
            var connectedIds = NetworkManager.ConnectedClientsIds;
            for (int i = 0; i < connectedIds.Count; i++)
            {
                ServerSpawnPlayerForClient(connectedIds[i]);
            }

            ServerReconcileBotBackfill();

            // Start countdown cleanly once all peers have entered the arena
            var st = m_NetMatchState.Value;
            if (st.Phase == MatchPhase.Countdown)
            {
                st.PhaseEndServerTime = GetServerTime() + m_CountdownDurationSeconds;
                m_NetMatchState.Value = st;
            }

            RebuildScoreboardCache();
        }

        private void ServerHandleSceneLoadEventCompleted(
            string sceneName,
            UnityEngine.SceneManagement.LoadSceneMode loadSceneMode,
            List<ulong> clientsCompleted,
            List<ulong> clientsTimedOut)
        {
            if (!IsServer)
            {
                return;
            }

            if (!m_InitialCombatantsSpawned)
            {
                ServerSpawnInitialCombatants(clientsCompleted);
                return;
            }

            if (clientsCompleted != null)
            {
                for (int i = 0; i < clientsCompleted.Count; i++)
                {
                    ServerSpawnPlayerForClient(clientsCompleted[i]);
                }
                ServerReconcileBotBackfill();
            }
        }

        private void ServerHandleClientSceneLoadComplete(
            ulong clientId,
            string sceneName,
            UnityEngine.SceneManagement.LoadSceneMode loadSceneMode)
        {
            if (!IsServer || !m_InitialCombatantsSpawned)
            {
                return;
            }

            if (!m_ActivePlayers.ContainsKey(clientId))
            {
                ServerSpawnPlayerForClient(clientId);
                ServerReconcileBotBackfill();
            }
        }

        private void ServerHandleClientSynchronizeComplete(ulong clientId)
        {
            if (!IsServer)
            {
                return;
            }

            // Late-joining client finished loading Blacksite09 and synchronizing scene NetworkObjects
            if (m_InitialCombatantsSpawned && !m_ActivePlayers.ContainsKey(clientId))
            {
                ServerSpawnPlayerForClient(clientId);
                ServerReconcileBotBackfill();
            }
        }

        private void ServerBeginCountdown()
        {
            if (!IsServer)
            {
                return;
            }

            var st = m_NetMatchState.Value;
            st.Phase = MatchPhase.Countdown;
            st.AlphaScore = 0;
            st.BravoScore = 0;
            st.WinningTeam = TeamId.None;
            st.WinnerCallsign = default;
            st.PhaseEndServerTime = GetServerTime() + m_CountdownDurationSeconds;
            m_NetMatchState.Value = st;
        }

        private void ServerHandleClientConnected(ulong clientId)
        {
            if (!IsServer)
            {
                return;
            }

            // When EnableSceneManagement is active, wait for OnSynchronizeComplete / OnLoadEventCompleted
            // before spawning remote clients' dynamic NetworkObjects so CreateObjectMessage isn't dropped mid-load.
            if (NetworkManager.NetworkConfig != null && NetworkManager.NetworkConfig.EnableSceneManagement && clientId != NetworkManager.ServerClientId)
            {
                return;
            }

            if (m_InitialCombatantsSpawned && !m_ActivePlayers.ContainsKey(clientId))
            {
                ServerSpawnPlayerForClient(clientId);
                ServerReconcileBotBackfill();
            }
        }

        private void ServerHandleClientDisconnected(ulong clientId)
        {
            if (!IsServer)
            {
                return;
            }

            if (m_ActivePlayers.TryGetValue(clientId, out var bridge))
            {
                m_ActivePlayers.Remove(clientId);
                if (bridge != null && bridge.IsSpawned)
                {
                    bridge.NetworkObject.Despawn(true);
                }
            }

            for (int i = m_NetScoreboard.Count - 1; i >= 0; i--)
            {
                if (!m_NetScoreboard[i].IsBot && m_NetScoreboard[i].ClientId == clientId)
                {
                    m_NetScoreboard.RemoveAt(i);
                }
            }

            ServerReconcileBotBackfill();
        }

        private void ServerSpawnPlayerForClient(ulong clientId)
        {
            if (m_PlayerPrefab == null)
            {
                Debug.LogWarning("[NetworkMatchManager] m_PlayerPrefab is not assigned; skipping player avatar spawn.");
                return;
            }

            if (m_ActivePlayers.ContainsKey(clientId))
            {
                return;
            }

            byte slot = m_NextSlotIndex++;
            TeamId assignedTeam = ChooseBalancedTeamForHuman();
            string callsign = clientId == NetworkManager.LocalClientId && VanguardNetServices.Session != null
                ? VanguardNetServices.Session.LocalCallsign
                : $"OPERATOR-{slot + 1:00}";
            string playerId = clientId == NetworkManager.LocalClientId && VanguardNetServices.Session != null
                ? VanguardNetServices.Session.LocalPlayerId
                : $"CLIENT-{clientId}";

            if (VanguardSessionManager.Instance != null
                && VanguardSessionManager.Instance.TryResolveSessionPlayerInfo(slot, out string sessionCallsign, out TeamId prefTeam))
            {
                if (!string.IsNullOrWhiteSpace(sessionCallsign))
                {
                    callsign = sessionCallsign;
                }
                if (m_Rules.Mode == MatchMode.TeamDeathmatch && prefTeam != TeamId.None && ShouldHonorPreferredTeam(prefTeam))
                {
                    assignedTeam = prefTeam;
                }
            }

            GetSafeSpawnTransform(assignedTeam, out Vector3 spawnPos, out float spawnYaw);

            GameObject go = Instantiate(m_PlayerPrefab, spawnPos, Quaternion.Euler(0f, spawnYaw, 0f));
            var netObj = go.GetComponent<NetworkObject>();
            var bridge = go.GetComponent<NetworkPlayerBridge>();
            if (netObj == null || bridge == null)
            {
                Debug.LogError("[NetworkMatchManager] Player prefab must have NetworkObject and NetworkPlayerBridge components.");
                Destroy(go);
                return;
            }

            netObj.SpawnAsPlayerObject(clientId, destroyWithScene: true);
            bridge.ServerInitializePlayer(slot, assignedTeam, callsign, playerId, spawnPos, spawnYaw);
            m_ActivePlayers[clientId] = bridge;

            m_NetScoreboard.Add(new NetScoreboardEntry
            {
                ClientId = clientId,
                NetworkObjectId = netObj.NetworkObjectId,
                PlayerId = NetWireHelpers.ToFixed64(playerId ?? string.Empty),
                Callsign = NetWireHelpers.ToFixed32(VanguardSessionManager.SanitizeCallsign(callsign)),
                TeamRaw = (byte)assignedTeam,
                Kills = 0,
                Deaths = 0,
                Assists = 0,
                Score = 0,
                PingMs = 0,
                IsBot = false,
                IsAlive = true,
                IsReady = true,
                IsHost = clientId == NetworkManager.ServerClientId,
                SlotIndex = slot
            });
        }

        private void ServerReconcileBotBackfill()
        {
            if (!IsServer || !m_Rules.FillWithBots || m_BotPrefab == null)
            {
                return;
            }

            int desiredTotal = Mathf.Clamp(m_Rules.TargetCombatantCount, 2, 12);
            int humanCount = m_ActivePlayers.Count;
            int desiredBots = Mathf.Max(0, desiredTotal - humanCount);

            // Despawn excess bots if real players joined
            while (m_ActiveBots.Count > desiredBots)
            {
                int lastIdx = m_ActiveBots.Count - 1;
                var bot = m_ActiveBots[lastIdx];
                m_ActiveBots.RemoveAt(lastIdx);

                if (bot != null)
                {
                    ulong botNetId = bot.IsSpawned ? bot.NetworkObjectId : 0UL;
                    for (int i = m_NetScoreboard.Count - 1; i >= 0; i--)
                    {
                        if (m_NetScoreboard[i].IsBot && m_NetScoreboard[i].NetworkObjectId == botNetId)
                        {
                            m_NetScoreboard.RemoveAt(i);
                        }
                    }

                    if (bot.IsSpawned)
                    {
                        bot.NetworkObject.Despawn(true);
                    }
                    else
                    {
                        Destroy(bot.gameObject);
                    }
                }
            }

            // Spawn additional bots to reach target combatant count
            while (m_ActiveBots.Count < desiredBots)
            {
                ServerSpawnBackfillBot();
            }
        }

        private void ServerSpawnBackfillBot()
        {
            byte slot = m_NextSlotIndex++;
            TeamId botTeam = ChooseBalancedTeamForBot();
            string botName = s_BotCallsigns[m_NextBotNameIndex % s_BotCallsigns.Length];
            m_NextBotNameIndex++;

            WeaponId botWeapon = (WeaponId)(m_NextBotNameIndex % 3);
            GetSafeSpawnTransform(botTeam, out Vector3 spawnPos, out float spawnYaw);

            GameObject go = Instantiate(m_BotPrefab, spawnPos, Quaternion.Euler(0f, spawnYaw, 0f));
            var netObj = go.GetComponent<NetworkObject>();
            var botSync = go.GetComponent<NetworkBotSync>();
            if (netObj == null || botSync == null)
            {
                Debug.LogError("[NetworkMatchManager] Bot prefab must have NetworkObject and NetworkBotSync components.");
                Destroy(go);
                return;
            }

            netObj.Spawn(destroyWithScene: true);
            botSync.ServerInitializeBot(slot, botTeam, botName, botWeapon, spawnPos, spawnYaw);
            m_ActiveBots.Add(botSync);

            m_NetScoreboard.Add(new NetScoreboardEntry
            {
                ClientId = botSync.OwnerClientIdOrBot,
                NetworkObjectId = netObj.NetworkObjectId,
                PlayerId = NetWireHelpers.ToFixed64($"BOT-{slot}"),
                Callsign = NetWireHelpers.ToFixed32(botName),
                TeamRaw = (byte)botTeam,
                Kills = 0,
                Deaths = 0,
                Assists = 0,
                Score = 0,
                PingMs = 0,
                IsBot = true,
                IsAlive = true,
                IsReady = true,
                IsHost = false,
                SlotIndex = slot
            });
        }

        public void ServerUpdatePlayerCallsign(ulong clientId, string cleanCallsign, string playerId)
        {
            if (!IsServer)
            {
                return;
            }

            for (int i = 0; i < m_NetScoreboard.Count; i++)
            {
                if (!m_NetScoreboard[i].IsBot && m_NetScoreboard[i].ClientId == clientId)
                {
                    var entry = m_NetScoreboard[i];
                    entry.Callsign = NetWireHelpers.ToFixed32(cleanCallsign);
                    if (!string.IsNullOrEmpty(playerId))
                    {
                        entry.PlayerId = NetWireHelpers.ToFixed64(playerId);
                    }
                    m_NetScoreboard[i] = entry;
                    break;
                }
            }
        }

        public void ServerNotifyCombatantEliminated(
            ulong attackerClientId,
            ulong attackerNetObjectId,
            string attackerName,
            TeamId attackerTeam,
            ulong victimClientId,
            ulong victimNetObjectId,
            string victimName,
            TeamId victimTeam,
            WeaponId weapon,
            bool isHeadshot,
            bool victimIsBot)
        {
            if (!IsServer)
            {
                return;
            }

            var st = m_NetMatchState.Value;
            if (st.Phase != MatchPhase.InProgress)
            {
                return;
            }

            bool isSelfKill = attackerNetObjectId == victimNetObjectId;
            int killerKills = 0;

            for (int i = 0; i < m_NetScoreboard.Count; i++)
            {
                var entry = m_NetScoreboard[i];
                bool modified = false;

                if (!isSelfKill && entry.NetworkObjectId == attackerNetObjectId)
                {
                    entry.Kills++;
                    entry.Score += isHeadshot ? 125 : 100;
                    killerKills = entry.Kills;
                    modified = true;
                }
                else if (entry.NetworkObjectId == victimNetObjectId)
                {
                    entry.Deaths++;
                    entry.IsAlive = false;
                    modified = true;
                }

                if (modified)
                {
                    m_NetScoreboard[i] = entry;
                }
            }

            // Update team score in TDM / SoloSkirmish
            if (!isSelfKill && st.Mode != MatchMode.FreeForAll)
            {
                if (attackerTeam == TeamId.Alpha)
                {
                    st.AlphaScore++;
                }
                else if (attackerTeam == TeamId.Bravo)
                {
                    st.BravoScore++;
                }
                m_NetMatchState.Value = st;
            }

            // Emit Kill Feed RPC
            var killPayload = new NetKillFeedPayload
            {
                SequenceId = m_NextKillSequence++,
                KillerName = NetWireHelpers.ToFixed32(attackerName ?? "OPERATOR"),
                KillerTeamRaw = (byte)attackerTeam,
                VictimName = NetWireHelpers.ToFixed32(victimName ?? "OPERATOR"),
                VictimTeamRaw = (byte)victimTeam,
                WeaponRaw = (byte)weapon,
                IsHeadshot = isHeadshot,
                ServerTime = GetServerTime()
            };
            BroadcastKillFeedRpc(killPayload);

            // Notify eliminated human player client for death/respawn HUD
            if (!victimIsBot)
            {
                NotifyEliminatedRpc(
                    NetWireHelpers.ToFixed32(attackerName ?? "OPERATOR"),
                    (byte)weapon,
                    isHeadshot,
                    m_Rules.RespawnDelaySeconds,
                    RpcTarget.Single(victimClientId, RpcTargetUse.Temp));
            }

            // Check win condition
            if (st.Mode == MatchMode.FreeForAll)
            {
                if (killerKills >= st.ScoreLimit)
                {
                    ServerEndMatch(TeamId.None, attackerName ?? "OPERATOR");
                }
            }
            else
            {
                if (st.AlphaScore >= st.ScoreLimit)
                {
                    ServerEndMatch(TeamId.Alpha, "ALPHA SQUAD");
                }
                else if (st.BravoScore >= st.ScoreLimit)
                {
                    ServerEndMatch(TeamId.Bravo, "BRAVO SQUAD");
                }
            }
        }

        private void ServerTickPhaseTransitions(double now)
        {
            var st = m_NetMatchState.Value;
            if (st.Phase == MatchPhase.Countdown && now >= st.PhaseEndServerTime)
            {
                st.Phase = MatchPhase.InProgress;
                st.PhaseEndServerTime = now + m_Rules.MatchDurationSeconds;
                m_NetMatchState.Value = st;
            }
            else if (st.Phase == MatchPhase.InProgress && now >= st.PhaseEndServerTime)
            {
                if (st.Mode == MatchMode.FreeForAll)
                {
                    string topName = "DRAW";
                    int topScore = -1;
                    for (int i = 0; i < m_NetScoreboard.Count; i++)
                    {
                        if (m_NetScoreboard[i].Score > topScore)
                        {
                            topScore = m_NetScoreboard[i].Score;
                            topName = m_NetScoreboard[i].Callsign.ToString();
                        }
                    }
                    ServerEndMatch(TeamId.None, topName);
                }
                else
                {
                    TeamId winner = st.AlphaScore > st.BravoScore
                        ? TeamId.Alpha
                        : (st.BravoScore > st.AlphaScore ? TeamId.Bravo : TeamId.None);
                    string winnerLabel = winner == TeamId.Alpha ? "ALPHA SQUAD" : (winner == TeamId.Bravo ? "BRAVO SQUAD" : "DRAW");
                    ServerEndMatch(winner, winnerLabel);
                }
            }
        }

        private void ServerEndMatch(TeamId winningTeam, string winnerCallsign)
        {
            var st = m_NetMatchState.Value;
            if (st.Phase == MatchPhase.MatchOver)
            {
                return;
            }

            st.Phase = MatchPhase.MatchOver;
            st.WinningTeam = winningTeam;
            st.WinnerCallsign = NetWireHelpers.ToFixed32(winnerCallsign ?? string.Empty);
            st.PhaseEndServerTime = GetServerTime();
            m_NetMatchState.Value = st;

            BroadcastMatchEndedRpc();
        }

        private void ServerTickRespawns(double now)
        {
            if (m_NetMatchState.Value.Phase != MatchPhase.InProgress)
            {
                return;
            }

            foreach (var kvp in m_ActivePlayers)
            {
                var p = kvp.Value;
                if (p != null && p.IsSpawned && !p.IsAlive && now >= p.RespawnEndServerTime)
                {
                    GetSafeSpawnTransform(p.Team, out Vector3 pos, out float yaw);
                    p.ServerRespawnAt(pos, yaw);
                    ServerSetScoreboardAlive(p.NetworkObjectId, true);
                }
            }

            for (int i = 0; i < m_ActiveBots.Count; i++)
            {
                var b = m_ActiveBots[i];
                if (b != null && b.IsSpawned && !b.IsAlive && now >= b.RespawnEndServerTime)
                {
                    GetSafeSpawnTransform(b.Team, out Vector3 pos, out float yaw);
                    b.ServerRespawnAt(pos, yaw);
                    ServerSetScoreboardAlive(b.NetworkObjectId, true);
                }
            }
        }

        private void ServerSetScoreboardAlive(ulong netObjId, bool alive)
        {
            for (int i = 0; i < m_NetScoreboard.Count; i++)
            {
                if (m_NetScoreboard[i].NetworkObjectId == netObjId)
                {
                    var e = m_NetScoreboard[i];
                    if (e.IsAlive != alive)
                    {
                        e.IsAlive = alive;
                        m_NetScoreboard[i] = e;
                    }
                    break;
                }
            }
        }

        private void ServerTickPings()
        {
            if (Time.unscaledTime - m_LastPingUpdateTime < 1.0f)
            {
                return;
            }
            m_LastPingUpdateTime = Time.unscaledTime;

            var transport = NetworkManager?.NetworkConfig?.NetworkTransport;
            if (transport == null)
            {
                return;
            }

            for (int i = 0; i < m_NetScoreboard.Count; i++)
            {
                var entry = m_NetScoreboard[i];
                if (entry.IsBot)
                {
                    continue;
                }

                short rttMs = entry.ClientId == NetworkManager.LocalClientId
                    ? (short)0
                    : (short)Mathf.Clamp((int)transport.GetCurrentRtt(entry.ClientId), 0, 999);

                if (entry.PingMs != rttMs)
                {
                    entry.PingMs = rttMs;
                    m_NetScoreboard[i] = entry;
                }

                if (m_ActivePlayers.TryGetValue(entry.ClientId, out var bridge) && bridge != null)
                {
                    bridge.ServerSetPingMs(rttMs);
                }
            }
        }

        private TeamId ChooseBalancedTeamForHuman()
        {
            if (m_Rules.Mode == MatchMode.FreeForAll)
            {
                return TeamId.None;
            }
            if (m_Rules.Mode == MatchMode.SoloSkirmish)
            {
                return TeamId.Alpha;
            }

            int alphaHumans = 0;
            int bravoHumans = 0;
            foreach (var kvp in m_ActivePlayers)
            {
                if (kvp.Value == null) continue;
                if (kvp.Value.Team == TeamId.Alpha) alphaHumans++;
                else if (kvp.Value.Team == TeamId.Bravo) bravoHumans++;
            }
            return alphaHumans <= bravoHumans ? TeamId.Alpha : TeamId.Bravo;
        }

        private TeamId ChooseBalancedTeamForBot()
        {
            if (m_Rules.Mode == MatchMode.FreeForAll)
            {
                return TeamId.None;
            }

            int alphaTotal = 0;
            int bravoTotal = 0;
            for (int i = 0; i < m_NetScoreboard.Count; i++)
            {
                if (m_NetScoreboard[i].Team == TeamId.Alpha) alphaTotal++;
                else if (m_NetScoreboard[i].Team == TeamId.Bravo) bravoTotal++;
            }
            return alphaTotal <= bravoTotal ? TeamId.Alpha : TeamId.Bravo;
        }

        private bool ShouldHonorPreferredTeam(TeamId pref)
        {
            int alpha = 0, bravo = 0;
            foreach (var kvp in m_ActivePlayers)
            {
                if (kvp.Value == null) continue;
                if (kvp.Value.Team == TeamId.Alpha) alpha++;
                else if (kvp.Value.Team == TeamId.Bravo) bravo++;
            }
            return pref == TeamId.Alpha ? alpha <= bravo + 1 : bravo <= alpha + 1;
        }

        private void GetSafeSpawnTransform(TeamId team, out Vector3 position, out float yaw)
        {
            m_EnemyPositionScratch.Clear();
            foreach (var kvp in m_ActivePlayers)
            {
                var p = kvp.Value;
                if (p != null && p.IsAlive && (team == TeamId.None || p.Team != team))
                {
                    m_EnemyPositionScratch.Add(p.CurrentRootPosition);
                }
            }
            for (int i = 0; i < m_ActiveBots.Count; i++)
            {
                var b = m_ActiveBots[i];
                if (b != null && b.IsAlive && (team == TeamId.None || b.Team != team))
                {
                    m_EnemyPositionScratch.Add(b.CurrentRootPosition);
                }
            }

            var provider = VanguardNetServices.SpawnProvider;
            if (provider != null)
            {
                Transform best = provider.GetBestSpawnPoint(team, m_EnemyPositionScratch);
                if (best != null)
                {
                    position = best.position;
                    yaw = best.eulerAngles.y;
                    return;
                }
            }

            // Deterministic safe fallback spawn ring if SpawnProvider not yet bound
            float angle = (m_NextSlotIndex * 53f + UnityEngine.Random.Range(0f, 360f)) * Mathf.Deg2Rad;
            float radius = 14f;
            float teamOffsetZ = team == TeamId.Alpha ? -18f : (team == TeamId.Bravo ? 18f : 0f);
            position = new Vector3(Mathf.Cos(angle) * radius, 1.0f, teamOffsetZ + Mathf.Sin(angle) * 6f);
            yaw = team == TeamId.Alpha ? 0f : 180f;
        }

        private double GetServerTime()
        {
            if (NetworkManager != null && NetworkManager.IsListening)
            {
                return NetworkManager.ServerTime.Time;
            }
            return Time.timeAsDouble;
        }

        // =====================================================================
        // NGO 2.14.0 RPCs
        // =====================================================================

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestTeamSwitchRpc(byte requestedTeamRaw, RpcParams rpcParams = default)
        {
            if (m_Rules.Mode == MatchMode.FreeForAll)
            {
                return;
            }

            ulong sender = rpcParams.Receive.SenderClientId;
            TeamId targetTeam = (TeamId)requestedTeamRaw;
            if (targetTeam != TeamId.Alpha && targetTeam != TeamId.Bravo)
            {
                return;
            }

            if (m_ActivePlayers.TryGetValue(sender, out var bridge) && bridge != null && bridge.Team != targetTeam)
            {
                bridge.ServerSetTeam(targetTeam);
                GetSafeSpawnTransform(targetTeam, out Vector3 pos, out float yaw);
                bridge.ServerRespawnAt(pos, yaw);

                for (int i = 0; i < m_NetScoreboard.Count; i++)
                {
                    if (!m_NetScoreboard[i].IsBot && m_NetScoreboard[i].ClientId == sender)
                    {
                        var e = m_NetScoreboard[i];
                        e.Team = targetTeam;
                        e.IsAlive = true;
                        m_NetScoreboard[i] = e;
                        break;
                    }
                }
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestToggleReadyRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            for (int i = 0; i < m_NetScoreboard.Count; i++)
            {
                if (!m_NetScoreboard[i].IsBot && m_NetScoreboard[i].ClientId == sender)
                {
                    var e = m_NetScoreboard[i];
                    e.IsReady = !e.IsReady;
                    m_NetScoreboard[i] = e;
                    if (m_ActivePlayers.TryGetValue(sender, out var bridge) && bridge != null)
                    {
                        bridge.ServerSetReady(e.IsReady);
                    }
                    break;
                }
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestRematchRpc(RpcParams rpcParams = default)
        {
            // Only the host may initiate a rematch
            if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId)
            {
                return;
            }

            for (int i = 0; i < m_NetScoreboard.Count; i++)
            {
                var e = m_NetScoreboard[i];
                e.Kills = 0;
                e.Deaths = 0;
                e.Assists = 0;
                e.Score = 0;
                e.IsAlive = true;
                m_NetScoreboard[i] = e;
            }

            foreach (var kvp in m_ActivePlayers)
            {
                if (kvp.Value != null)
                {
                    GetSafeSpawnTransform(kvp.Value.Team, out Vector3 pos, out float yaw);
                    kvp.Value.ServerRespawnAt(pos, yaw);
                }
            }

            for (int i = 0; i < m_ActiveBots.Count; i++)
            {
                if (m_ActiveBots[i] != null)
                {
                    GetSafeSpawnTransform(m_ActiveBots[i].Team, out Vector3 pos, out float yaw);
                    m_ActiveBots[i].ServerRespawnAt(pos, yaw);
                }
            }

            ServerBeginCountdown();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestRecentKillsRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            for (int i = 0; i < m_RecentKills.Count; i++)
            {
                var k = m_RecentKills[i];
                var payload = new NetKillFeedPayload
                {
                    SequenceId = k.SequenceId,
                    KillerName = NetWireHelpers.ToFixed32(k.KillerName ?? string.Empty),
                    KillerTeamRaw = (byte)k.KillerTeam,
                    VictimName = NetWireHelpers.ToFixed32(k.VictimName ?? string.Empty),
                    VictimTeamRaw = (byte)k.VictimTeam,
                    WeaponRaw = (byte)k.Weapon,
                    IsHeadshot = k.IsHeadshot,
                    ServerTime = k.ServerTime
                };
                SendKillFeedToClientRpc(payload, RpcTarget.Single(sender, RpcTargetUse.Temp));
            }
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void BroadcastKillFeedRpc(NetKillFeedPayload payload)
        {
            AppendKillFeedEntry(payload.ToEntry());
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void SendKillFeedToClientRpc(NetKillFeedPayload payload, RpcParams rpcParams = default)
        {
            AppendKillFeedEntry(payload.ToEntry());
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void NotifyEliminatedRpc(
            FixedString32Bytes killerName,
            byte weaponRaw,
            bool isHeadshot,
            float respawnDelay,
            RpcParams rpcParams = default)
        {
            OnLocalPlayerEliminated?.Invoke(killerName.ToString(), (WeaponId)weaponRaw, isHeadshot, respawnDelay);
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void BroadcastMatchEndedRpc()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            RebuildScoreboardCache();
            OnMatchEnded?.Invoke(CurrentState, m_ScoreboardCache);
        }

        private void AppendKillFeedEntry(KillFeedEntry entry)
        {
            // Deduplicate by SequenceId
            for (int i = 0; i < m_RecentKills.Count; i++)
            {
                if (m_RecentKills[i].SequenceId == entry.SequenceId)
                {
                    return;
                }
            }

            m_RecentKills.Add(entry);
            while (m_RecentKills.Count > MaxRecentKills)
            {
                m_RecentKills.RemoveAt(0);
            }

            OnKillFeedEntryAdded?.Invoke(entry);
        }

        private void HandleMatchStateChanged(NetMatchState previous, NetMatchState current)
        {
            OnMatchStateChanged?.Invoke(current.ToSnapshot(GetServerTime()));
        }

        private void HandleScoreboardListChanged(NetworkListEvent<NetScoreboardEntry> changeEvent)
        {
            RebuildScoreboardCache();
        }

        private void RebuildScoreboardCache()
        {
            m_ScoreboardCache.Clear();
            for (int i = 0; i < m_NetScoreboard.Count; i++)
            {
                m_ScoreboardCache.Add(m_NetScoreboard[i].ToStats());
            }

            // Sort descending by Score, then Kills
            m_ScoreboardCache.Sort((a, b) =>
            {
                int scoreCmp = b.Score.CompareTo(a.Score);
                return scoreCmp != 0 ? scoreCmp : b.Kills.CompareTo(a.Kills);
            });

            OnScoreboardUpdated?.Invoke(m_ScoreboardCache);
        }
    }
}
