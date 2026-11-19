using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace VanguardZero.Networking
{
    public enum MatchMode : byte
    {
        TeamDeathmatch = 0,
        FreeForAll = 1,
        SoloSkirmish = 2
    }

    public enum TeamId : byte
    {
        None = 0,
        Alpha = 1,
        Bravo = 2
    }

    public enum MatchPhase : byte
    {
        MainMenu = 0,
        Lobby = 1,
        Loading = 2,
        Countdown = 3,
        InProgress = 4,
        RoundOver = 5,
        MatchOver = 6
    }

    public enum WeaponId : byte
    {
        VX47_AssaultRifle = 0,
        M890_TacticalShotgun = 1,
        M19_TacticalPistol = 2
    }

    public enum HitSurfaceType : byte
    {
        Concrete = 0,
        Metal = 1,
        Flesh = 2,
        Armor = 3,
        Dirt = 4
    }

    public enum ConnectionMode : byte
    {
        OfflineSolo = 0,
        UnityRelay = 1,
        LanDirect = 2
    }

    public enum CloudServiceState : byte
    {
        Uninitialized = 0,
        Initializing = 1,
        Ready = 2,
        UnavailableFallback = 3
    }

    [Flags]
    public enum PlayerStanceFlags : byte
    {
        None = 0,
        Grounded = 1 << 0,
        Sprinting = 1 << 1,
        Crouching = 1 << 2,
        Sliding = 1 << 3,
        AimingDownSights = 1 << 4,
        Reloading = 1 << 5,
        Dead = 1 << 6
    }

    public enum HitZone : byte
    {
        Body = 0,
        Head = 1,
        Limb = 2
    }

    [Serializable]
    public struct SessionOpResult
    {
        public bool Success;
        public string ErrorMessage;
        public string JoinCode;
        public ConnectionMode Mode;

        public static SessionOpResult Ok(ConnectionMode mode, string joinCode = "")
        {
            return new SessionOpResult
            {
                Success = true,
                ErrorMessage = string.Empty,
                JoinCode = joinCode ?? string.Empty,
                Mode = mode
            };
        }

        public static SessionOpResult Fail(string error, ConnectionMode mode = ConnectionMode.OfflineSolo)
        {
            return new SessionOpResult
            {
                Success = false,
                ErrorMessage = error ?? "Unknown session error",
                JoinCode = string.Empty,
                Mode = mode
            };
        }
    }

    [Serializable]
    public struct HostSessionRequest
    {
        public string SessionName;
        public MatchMode Mode;
        public string MapName;
        public int MaxPlayers;
        public bool IsPrivate;
        public bool FillWithBots;
        public int TargetCombatantCount;
        public int ScoreLimit;
        public float MatchDurationSeconds;

        public static HostSessionRequest CreateDefault(MatchMode mode = MatchMode.TeamDeathmatch, bool isPrivate = false)
        {
            return new HostSessionRequest
            {
                SessionName = "BLACKSITE-09 OPS",
                Mode = mode,
                MapName = "Blacksite09",
                MaxPlayers = 8,
                IsPrivate = isPrivate,
                FillWithBots = true,
                TargetCombatantCount = 6,
                ScoreLimit = mode == MatchMode.FreeForAll ? 15 : 25,
                MatchDurationSeconds = 300f
            };
        }
    }

    [Serializable]
    public struct MatchRulesConfig
    {
        public MatchMode Mode;
        public string MapName;
        public int ScoreLimit;
        public float MatchDurationSeconds;
        public float RespawnDelaySeconds;
        public int MaxPlayers;
        public bool FillWithBots;
        public int TargetCombatantCount;

        public static MatchRulesConfig CreateDefault(MatchMode mode = MatchMode.TeamDeathmatch)
        {
            return new MatchRulesConfig
            {
                Mode = mode,
                MapName = "Blacksite09",
                ScoreLimit = mode == MatchMode.FreeForAll ? 15 : 25,
                MatchDurationSeconds = 300f,
                RespawnDelaySeconds = 3.5f,
                MaxPlayers = 8,
                FillWithBots = true,
                TargetCombatantCount = 6
            };
        }
    }

    [Serializable]
    public struct LobbySessionInfo
    {
        public string SessionId;
        public string SessionName;
        public string JoinCode;
        public string HostName;
        public MatchMode Mode;
        public string MapName;
        public int CurrentPlayers;
        public int MaxPlayers;
        public bool IsPrivate;
        public bool InProgress;
        public bool BotsEnabled;
    }

    [Serializable]
    public struct PlayerCombatStats
    {
        public ulong ClientId;
        public ulong NetworkObjectId;
        public string PlayerId;
        public string Callsign;
        public TeamId Team;
        public int Kills;
        public int Deaths;
        public int Assists;
        public int Score;
        public int PingMs;
        public bool IsBot;
        public bool IsAlive;
        public bool IsReady;
        public bool IsHost;
        public byte SlotIndex;
    }

    [Serializable]
    public struct MatchStateSnapshot
    {
        public MatchPhase Phase;
        public MatchMode Mode;
        public string MapName;
        public int AlphaScore;
        public int BravoScore;
        public int ScoreLimit;
        public float RemainingSeconds;
        public TeamId WinningTeam;
        public string WinnerCallsign;
        public string JoinCode;
        public ConnectionMode ConnectionMode;
    }

    [Serializable]
    public struct PlayerPoseSnapshot
    {
        public Vector3 Position;
        public float Yaw;
        public float Pitch;
        public Vector3 Velocity;
        public PlayerStanceFlags Stance;
        public WeaponId ActiveWeapon;
    }

    [Serializable]
    public struct WeaponFireInput
    {
        public WeaponId Weapon;
        public Vector3 MuzzlePosition;
        public Vector3 AimOrigin;
        public Vector3 AimDirection;
        public uint ShotSequence;
        public float ClientTimeSeconds;
    }

    [Serializable]
    public struct WeaponShotVisualEvent
    {
        public ulong ShooterNetworkObjectId;
        public WeaponId Weapon;
        public Vector3 MuzzlePosition;
        public Vector3 HitPoint;
        public Vector3 HitNormal;
        public HitSurfaceType Surface;
        public bool DidHit;
        public bool IsLocalShooter;
    }

    [Serializable]
    public struct HitConfirmEvent
    {
        public bool IsHeadshot;
        public bool IsKill;
        public int DamageDealt;
        public Vector3 HitPoint;
        public string VictimCallsign;
    }

    [Serializable]
    public struct KillFeedEntry
    {
        public uint SequenceId;
        public string KillerName;
        public TeamId KillerTeam;
        public string VictimName;
        public TeamId VictimTeam;
        public WeaponId Weapon;
        public bool IsHeadshot;
        public double ServerTime;
    }

    [Serializable]
    public struct WeaponBallisticsSpec
    {
        public WeaponId Id;
        public string DisplayName;
        public int BaseDamage;
        public float HeadshotMultiplier;
        public int PelletsPerShot;
        public float SpreadDegrees;
        public float MaxRange;
        public float FalloffStartRange;
        public float MinFalloffMultiplier;
        public float FireIntervalSeconds;
        public int MagazineSize;
        public float ReloadDurationSeconds;
    }

    /// <summary>
    /// Authoritative deterministic weapon specifications shared by gameplay presentation and server hit validation.
    /// </summary>
    public static class WeaponBallisticsRegistry
    {
        private static readonly WeaponBallisticsSpec[] s_Specs =
        {
            // VX-47 Assault Rifle
            new WeaponBallisticsSpec
            {
                Id = WeaponId.VX47_AssaultRifle,
                DisplayName = "VX-47",
                BaseDamage = 26,
                HeadshotMultiplier = 2.0f,
                PelletsPerShot = 1,
                SpreadDegrees = 0.65f,
                MaxRange = 160f,
                FalloffStartRange = 45f,
                MinFalloffMultiplier = 0.72f,
                FireIntervalSeconds = 0.095f, // ~630 RPM
                MagazineSize = 30,
                ReloadDurationSeconds = 2.15f
            },
            // M890 Tactical Shotgun
            new WeaponBallisticsSpec
            {
                Id = WeaponId.M890_TacticalShotgun,
                DisplayName = "M890",
                BaseDamage = 18, // 8 pellets * 18 = 144 point-blank max
                HeadshotMultiplier = 1.5f,
                PelletsPerShot = 8,
                SpreadDegrees = 4.2f,
                MaxRange = 42f,
                FalloffStartRange = 12f,
                MinFalloffMultiplier = 0.35f,
                FireIntervalSeconds = 0.78f,
                MagazineSize = 8,
                ReloadDurationSeconds = 2.4f
            },
            // M19 Tactical Sidearm Pistol
            new WeaponBallisticsSpec
            {
                Id = WeaponId.M19_TacticalPistol,
                DisplayName = "M19",
                BaseDamage = 24,
                HeadshotMultiplier = 2.1f,
                PelletsPerShot = 1,
                SpreadDegrees = 0.9f,
                MaxRange = 95f,
                FalloffStartRange = 25f,
                MinFalloffMultiplier = 0.6f,
                FireIntervalSeconds = 0.16f,
                MagazineSize = 15,
                ReloadDurationSeconds = 1.55f
            }
        };

        public static WeaponBallisticsSpec Get(WeaponId id)
        {
            int idx = (int)id;
            if (idx < 0 || idx >= s_Specs.Length)
            {
                idx = 0;
            }
            return s_Specs[idx];
        }
    }

    public interface INetworkSessionService
    {
        CloudServiceState CloudState { get; }
        string CloudStatusBanner { get; }
        ConnectionMode ActiveConnectionMode { get; }
        string LocalPlayerId { get; }
        string LocalCallsign { get; }
        string CurrentJoinCode { get; }
        bool IsInSession { get; }
        bool IsHost { get; }
        bool IsOfflineSolo { get; }
        MatchRulesConfig ActiveRules { get; }

        Task<SessionOpResult> InitializeServicesAsync(string callsign = null);
        Task SetCallsignAsync(string callsign);
        Task<SessionOpResult> HostSessionAsync(HostSessionRequest request);
        Task<SessionOpResult> HostLanDirectAsync(HostSessionRequest request, ushort port = 7777);
        Task<SessionOpResult> JoinByCodeAsync(string joinCode);
        Task<SessionOpResult> JoinSessionByIdAsync(string sessionId);
        Task<SessionOpResult> JoinLanDirectAsync(string ipAddress, ushort port = 7777);
        Task<IReadOnlyList<LobbySessionInfo>> QueryOpenLobbiesAsync(MatchMode? modeFilter = null);
        Task<SessionOpResult> QuickPlayAsync(MatchMode preferredMode = MatchMode.TeamDeathmatch);
        Task<SessionOpResult> StartSoloSkirmishAsync(MatchRulesConfig rules);
        Task SetLocalReadyAsync(bool isReady);
        Task SetLocalPreferredTeamAsync(TeamId team);
        Task UpdateLobbyRulesAsync(MatchRulesConfig rules);
        Task StartMatchFromLobbyAsync();
        Task LeaveSessionAsync(string reason = null);

        event Action<CloudServiceState, string> OnCloudStateChanged;
        event Action<LobbySessionInfo> OnSessionJoined;
        event Action<string> OnSessionLeft;
        event Action<IReadOnlyList<PlayerCombatStats>> OnLobbyRosterChanged;
        event Action<MatchRulesConfig> OnLobbyRulesChanged;
    }

    public interface INetworkMatchService
    {
        MatchStateSnapshot CurrentState { get; }
        IReadOnlyList<PlayerCombatStats> Scoreboard { get; }
        IReadOnlyList<KillFeedEntry> RecentKills { get; }

        void RequestTeamSwitch(TeamId requestedTeam);
        void RequestToggleReady();
        void RequestStartMatch();
        void RequestRematch();
        bool TryGetLocalPlayerStats(out PlayerCombatStats stats);

        event Action<MatchStateSnapshot> OnMatchStateChanged;
        event Action<IReadOnlyList<PlayerCombatStats>> OnScoreboardUpdated;
        event Action<KillFeedEntry> OnKillFeedEntryAdded;
        event Action<string, WeaponId, bool, float> OnLocalPlayerEliminated;
        event Action<Vector3, Quaternion> OnLocalPlayerRespawned;
        event Action<MatchStateSnapshot, IReadOnlyList<PlayerCombatStats>> OnMatchEnded;
    }

    /// <summary>Attached to Player_Networked prefab by NetworkPlayerBridge.</summary>
    public interface IPlayerCombatAuthority
    {
        bool IsLocalPlayer { get; }
        bool IsAlive { get; }
        int Health { get; }
        int MaxHealth { get; }
        int Armor { get; }
        int MaxArmor { get; }
        TeamId Team { get; }
        WeaponId EquippedWeapon { get; }
        string Callsign { get; }
        int PingMs { get; }
        ulong OwnerClientId { get; }

        void SubmitLocalPose(in PlayerPoseSnapshot pose);
        void SubmitWeaponFire(in WeaponFireInput fireInput);
        void SubmitWeaponSwitch(WeaponId weaponId);
        void SubmitReloadStart();

        event Action<PlayerPoseSnapshot> OnRemotePoseReceived;
        event Action<int, int, Vector3> OnHealthArmorChanged;
        event Action<HitConfirmEvent> OnHitConfirmed;
        event Action<WeaponShotVisualEvent> OnShotVisualReceived;
        event Action<Vector3, Quaternion> OnAuthoritativeTeleport;
        event Action<bool> OnAliveStateChanged;
        event Action<TeamId> OnTeamAssigned;
    }

    /// <summary>Attached to Bot_Networked prefab by NetworkBotSync.</summary>
    public interface IBotNetworkBridge
    {
        bool ShouldRunBotAI { get; }
        bool IsAlive { get; }
        int Health { get; }
        int Armor { get; }
        TeamId Team { get; }
        string Callsign { get; }
        WeaponId EquippedWeapon { get; }

        void PushBotPose(in PlayerPoseSnapshot pose);
        void FireBotWeapon(in WeaponFireInput fireInput);

        event Action<PlayerPoseSnapshot> OnBotPoseUpdated;
        event Action<bool> OnBotAliveStateChanged;
        event Action<TeamId> OnBotTeamAssigned;
        event Action<WeaponShotVisualEvent> OnBotShotVisual;
    }

    /// <summary>Implemented by unity-core's Blacksite09 arena/spawn manager.</summary>
    public interface IVanguardSpawnProvider
    {
        LayerMask WorldGeometryMask { get; }
        Transform GetBestSpawnPoint(TeamId team, IReadOnlyList<Vector3> activeEnemyPositions);
        IReadOnlyList<Transform> GetAllSpawnPoints(TeamId team);
    }

    /// <summary>Internal contract implemented by NetworkPlayerBridge and NetworkBotSync for server-side hitbox rewind.</summary>
    public interface ILagCompensatedTarget
    {
        ulong TargetNetworkObjectId { get; }
        ulong OwnerClientIdOrBot { get; }
        bool IsBotTarget { get; }
        TeamId Team { get; }
        bool IsAlive { get; }
        string Callsign { get; }
        Vector3 CurrentRootPosition { get; }
        float CurrentYaw { get; }
        bool IsCrouching { get; }

        /// <summary>
        /// Applies authoritative server-calculated damage. Returns true if this hit eliminated the target.
        /// </summary>
        bool ApplyAuthoritativeDamage(
            int damage,
            ulong attackerClientId,
            ulong attackerNetObjectId,
            string attackerName,
            TeamId attackerTeam,
            WeaponId weapon,
            bool isHeadshot,
            Vector3 shotOrigin,
            out int actualDamageDealt);
    }

    /// <summary>
    /// Decoupled service locator bridging Gameplay/UI scripts (unity-core) with Networking (unity-netcode).
    /// </summary>
    public static class VanguardNetServices
    {
        public const string MainMenuSceneName = "MainMenu";
        public const string GameplaySceneName = "Blacksite09";

        public static INetworkSessionService Session { get; private set; }
        public static INetworkMatchService Match { get; private set; }
        public static IVanguardSpawnProvider SpawnProvider { get; private set; }

        public static event Action<INetworkSessionService> OnSessionServiceRegistered;
        public static event Action<INetworkMatchService> OnMatchServiceRegistered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Session = null;
            Match = null;
            SpawnProvider = null;
            OnSessionServiceRegistered = null;
            OnMatchServiceRegistered = null;
        }

        public static void RegisterSessionService(INetworkSessionService service)
        {
            Session = service;
            if (service != null)
            {
                OnSessionServiceRegistered?.Invoke(service);
            }
        }

        public static void UnregisterSessionService(INetworkSessionService service)
        {
            if (ReferenceEquals(Session, service))
            {
                Session = null;
            }
        }

        public static void RegisterMatchService(INetworkMatchService service)
        {
            Match = service;
            if (service != null)
            {
                OnMatchServiceRegistered?.Invoke(service);
            }
        }

        public static void UnregisterMatchService(INetworkMatchService service)
        {
            if (ReferenceEquals(Match, service))
            {
                Match = null;
            }
        }

        public static void RegisterSpawnProvider(IVanguardSpawnProvider provider)
        {
            SpawnProvider = provider;
        }

        public static void UnregisterSpawnProvider(IVanguardSpawnProvider provider)
        {
            if (ReferenceEquals(SpawnProvider, provider))
            {
                SpawnProvider = null;
            }
        }

        public static WeaponBallisticsSpec GetWeaponSpec(WeaponId weapon)
        {
            return WeaponBallisticsRegistry.Get(weapon);
        }
    }
}
