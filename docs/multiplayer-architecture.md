# VANGUARD ZERO — Multiplayer & Networking Architecture Specification

> **Owner:** `unity-netcode` (Unity Multiplayer & Relay Networking Lead)  
> **Target Engine:** Unity 6 (`6000.3.22f1`, URP 3D)  
> **Target Platforms:** Desktop (`macOS` Apple Silicon/Universal, `Windows` x64)  
> **Namespace:** `VanguardZero.Networking` *(never uses `Netcode` as a namespace segment to avoid shadowing Unity's `Netcode` facade)*

---

## 1. Decision Record (Verification Gate)

### 1.1 Selected Stack, Topology & Authority Model

| Dimension | Choice | Deciding Requirement |
|---|---|---|
| **Session & Connectivity Layer** | **Unity Multiplayer Services** (`com.unity.services.multiplayer`) + **Unity Authentication** (`com.unity.services.authentication`) | Sessions-first requirement: Public/Private rooms with Join Codes (`CreateSessionAsync`, `JoinSessionByCodeAsync`), Lobby Browser (`QuerySessionsAsync`), Quick Play (`MatchmakeSessionAsync` + `QuickJoinOptions`), and NAT traversal via `WithRelayNetwork()`. |
| **Gameplay Netcode Stack** | **Netcode for GameObjects (NGO)** (`com.unity.netcode.gameobjects`) | Project uses a GameObject/MonoBehaviour/URP/CharacterController/NavMeshAgent pipeline and explicitly specifies NGO + Relay. Keeps 100% single-codebase parity with Solo Skirmish without a DOTS/Unity Physics rewrite. |
| **Topology** | **Client-Hosted Listen Server** (Host + Clients) over **Unity Relay**, with **LAN Direct (`WithDirectNetwork` / `UnityTransport`)** and **Offline Solo Skirmish (Local NGO Host on `127.0.0.1`)** fallbacks | Zero dedicated server hosting budget; trusted cross-platform desktop matches (up to 8 players) across macOS and Windows; builds must remain playable if Unity Cloud / Relay is unconfigured or offline. |
| **Authority Model** | **Server/Host-Authoritative Combat, Match State, Identity & Bots**; **Owner-Driven Local FPS Movement** with server validation & authoritative respawn teleport override | Satisfies Invariant 7 (one writer per value) and Invariant 8 (authority assigns slot, team, spawn). Because NGO has no built-in rollback-and-replay loop, owner-driven `CharacterController` guarantees 0 ms local movement feel, while a custom **Server-Side Lag-Compensated Hitbox Rewind Buffer** delivers fair hit registration under simulated/real latency. |
| **Host Loss Policy** | **Clean Match Termination & Return to Main Menu** (no experimental host migration) | Listen-server games end when the host exits; experimental host migration is unsupported on this NGO/listen-server setup. Clients cleanly dispose `NetworkManager`, release cursor lock, and display an actionable disconnect banner with zero stuck states. |
| **Cheat-Safety Scope** | **Trusted Casual / Community Sessions** (not ranked anti-cheat) | Per Invariant 6, client-hosted listen servers cannot protect against a malicious host. Clients still never report damage, kills, or scores—only shot requests—so non-host client tampering is mitigated by host validation (fire rate, range, alive state, wall occlusion, rewind cap). |

### 1.2 Installed Package Verification (Confirmed from Project Files & `Library/PackageCache/`)

> **Gate Status:** Verified directly from `ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json`, `Packages/packages-lock.json`, and `Library/PackageCache/` source files.

| Package | Confirmed Version | Quoted Source Citation & Verified APIs |
|---|---|---|
| Unity Editor | `6000.3.22f1 (1c726e1fb402)` | `ProjectSettings/ProjectVersion.txt:1-2`: `m_EditorVersion: 6000.3.22f1` |
| `com.unity.netcode.gameobjects` | `2.14.0` (NGO 2.x; `com.unity.netcode` NOT installed) | `Packages/packages-lock.json:109-111`; confirmed `NetworkVariable<T>` (`Runtime/NetworkVariable/NetworkVariable.cs:12`), `NetworkList<T> where T : unmanaged, IEquatable<T>` (`Runtime/NetworkVariable/Collections/NetworkList.cs:14`), `[Rpc(SendTo.*)]` with `RpcInvokePermission` (`Runtime/Messaging/RpcAttributes.cs:47-127`, avoiding obsolete `[ServerRpc(RequireOwnership=...)]`), `RpcTarget.Single` (`Runtime/Messaging/RpcTargets/RpcTarget.cs:257`), `NetworkBehaviour.OnDestroy` auto-disposing NetworkVariables (`Runtime/Core/NetworkBehaviour.cs:1673-1706`), `UnityTransport.SetConnectionData` & `GetCurrentRtt` (`Runtime/Transports/UTP/UnityTransport.cs:907,1522`), `FixedString` `BufferSerializer` support (`Runtime/Serialization/BufferSerializer.cs:307`). |
| `com.unity.transport` | `2.7.4` | `Packages/packages-lock.json` (`com.unity.transport`) |
| `com.unity.services.multiplayer` | `2.5.0` | `Packages/manifest.json:17`; confirmed `IMultiplayerService` (`CreateSessionAsync`, `JoinSessionByCodeAsync`, `JoinSessionByIdAsync`, `MatchmakeSessionAsync(QuickJoinOptions, SessionOptions)`, `QuerySessionsAsync` in `Runtime/Multiplayer/MultiplayerService.cs:54-423`), `SessionOptionsExtensions.WithRelayNetwork` & `WithDirectNetwork` (`Runtime/Multiplayer/Modules/Network/SessionOptionsExtensions.cs:56,127`), `GameObjectsNetcodeNetworkHandler` automatically managing `NetworkManager.Singleton` + `UnityTransport` (`Runtime/Multiplayer/Modules/Network/Handlers/NetworkHandler/GameObjectsNetcodeNetworkHandler.cs:49-172`), `ISession`/`IHostSession` (`PlayerLeaving`/`PlayerHasLeft` instead of obsolete `PlayerLeft` in `Runtime/Multiplayer/Session/SessionHandler.cs:244-263`), `SessionProperty` & `PropertyIndex` (`Runtime/Multiplayer/Session/Models/SessionProperty.cs:29`, `PropertyIndex.cs:7-53`). |
| `com.unity.services.authentication` | `3.9.0` | `Packages/manifest.json:15`; confirmed `AuthenticationPackageInitializer.GetProfile` (`Runtime/Core/AuthenticationPackageInitializer.cs:152-190`) automatically isolates MPPM virtual-player profiles via CLI args, so scaffolded `Assets/PlayerAuthentication/PlayerAuthentication.cs` (`PlayerAuthentication.SignedIn`) yields unique anonymous Player IDs per MPPM clone without conflict. |
| `com.unity.multiplayer.playmode` | `2.0.2` | `Packages/packages-lock.json:100-102` |
| Unity Cloud Link | Project `70de3ace-9a2f-4e7a-af87-d3ad651fd2b6` (Org `f3cef7a0-5dc0-43f9-0000-0c4026d62876`) | Linked during `unity projects create`; `PlayerSettings.runInBackground = true` (`runInBackground: 1`) verified. |

> [!NOTE]
> Because `com.unity.netcode.gameobjects` resolved to **`2.14.0`** (NGO 2.x), `com.unity.netcode` is **not** transitively installed, so there is no competing automatic Entities bootstrap on port `7979`.

---

## 2. Performance & Scale Budgets

All numeric budgets below are concrete engineering targets verified in MPPM under emulated latency (`80–120 ms RTT`, `15 ms jitter`, `2% packet loss`).

| Budget | Target | Rationale & Enforcement Symbol |
|---|---|---|
| **Max Players per Session** | **8 human players** (up to **8 total active combatants** including AI bot backfill) | Keeps Relay allocation, host CPU, and arena pacing tight in BLACKSITE-09 (`MatchRulesConfig.MaxPlayers = 8`, `SessionOptions.MaxPlayers`). |
| **Max Relevant NetworkObjects per Client** | **<= 24 `NetworkObject`s** (1 `NetworkMatchManager`, up to 8 `Player_Networked`, up to 8 `Bot_Networked`, up to 7 map interactables) | Well below the ~100–300 relevance threshold. Tracers, muzzle flashes, shell casings, bullet decals, and ragdolls are strictly **local presentation** (never `NetworkObject`s). |
| **Tick Rate & Send Rate** | **Simulation Tick: 60 Hz** (`NetworkConfig.TickRate = 60`)<br>**Player Pose Send Rate: 30 Hz** (`NetworkPlayerBridge.PoseSendInterval = 1f / 30f`)<br>**Bot Pose Send Rate: 20 Hz** (`NetworkBotSync.PoseSendInterval = 1f / 20f`)<br>**Rewind History: 64 samples @ 60 Hz (~1.06 s), Rewind Cap: 250 ms** (`LagCompensatedHitValidator.MaxRewindSeconds = 0.25f`) | 60 Hz tick gives crisp 16.67 ms lag-comp granularity and fast RPC dispatch while 30 Hz quantized pose updates cut bandwidth in half; client interpolation smooths remote motion. |
| **Per-Client Bandwidth Ceiling** | **<= 60 KB/s (~480 kbps) down**, **<= 25 KB/s (~200 kbps) up** | Well within desktop broadband band (`50–150 KB/s`). At 8 active combatants × ~32 B/pose × 30 Hz ≈ 7.7 KB/s steady state + ~4–8 KB/s combat/state RPCs + Relay framing ≈ ~20–25 KB/s typical. |

---

## 3. Session, Lobby & Connectivity Flows

### 3.1 Netcode Start-Up Ownership (Single Starter Invariant)

| Mode | Who Starts `NetworkManager`? | Transport Configuration |
|---|---|---|
| **Online Relay (`WithRelayNetwork`)** | **Multiplayer Services Default Network Handler** (never double-call `NetworkManager.StartHost`/`StartClient`) | Managed automatically by `com.unity.services.multiplayer` (`RelayUnityTransport` / `UnityTransport` relay allocation). |
| **LAN Direct (Fallback / Local)** | **`VanguardSessionManager`** (no UGS session active) | Configures `UnityTransport.SetConnectionData(ip, port, "0.0.0.0")` and starts `NetworkManager.StartHost()` / `StartClient()`. |
| **Solo Skirmish (Offline vs Bots)** | **`VanguardSessionManager`** (no UGS session active) | Configures `UnityTransport.SetConnectionData("127.0.0.1", 0)` (ephemeral port) and starts `NetworkManager.StartHost()`. Exact same gameplay `NetworkBehaviour`s run with zero cloud dependency! |
| **Direct Scene Play (Editor Dev)** | **`OfflineFallbackAutoHost`** (optional helper in `Blacksite09`) | If `Blacksite09` is launched directly in Editor Play Mode with `NetworkManager.IsListening == false`, auto-starts Solo Skirmish local host so `@unity-core` can test immediately. |

### 3.2 Cloud / Relay Unconfigured Handling (Zero Soft-Lock Guarantee)

1. On startup, `VanguardSessionManager.InitializeServicesAsync()` initializes `UnityServices` and signs in anonymously via `AuthenticationService.Instance.SignInAnonymouslyAsync()`.
2. If Unity Cloud Project ID is missing/unlinked, authentication fails, the host machine is offline, or Relay allocation throws `SessionException` / `RequestFailedException`:
   - `CloudState` becomes `CloudServiceState.UnavailableFallback`.
   - `CloudStatusBanner` surfaces a clear, actionable UI message (e.g., *"Online Relay unavailable (offline or Cloud Relay not configured) — LAN Direct & Solo Skirmish are active."*).
   - Online buttons gracefully guide to **LAN Direct (Host/Join IP)** and **Solo Skirmish vs Bots** — the game never freezes, loops infinitely, or throws unhandled exceptions.

### 3.3 Session Operations Matrix

| User Flow | Multiplayer Services API | Indexed / Player Properties |
|---|---|---|
| **Player Identity** | `AuthenticationService.Instance.SignInAnonymouslyAsync()` | `PlayerProperty("callsign")`, `PlayerProperty("team")`, `PlayerProperty("ready")` |
| **Host Public / Private** | `MultiplayerService.Instance.CreateSessionAsync(options)` with `WithRelayNetwork()` and `MaxPlayers = rules.MaxPlayers` (> 0) | Indexed `SessionProperty` keys: `"mode"` (S1), `"map"` (S2), `"phase"` (S3), `"host"` (S4), `"bots"` (S5); `IsPrivate` toggle; exposes `ISession.Code` for display & clipboard copy |
| **Browse Open Lobbies** | `MultiplayerService.Instance.QuerySessionsAsync(queryOptions)` | Filters `AvailableSlots > 0` and optional `"mode"` filter; sorts by available slots / creation |
| **Join by Code** | `MultiplayerService.Instance.JoinSessionByCodeAsync(code, joinOptions)` | Passes local player's `callsign` property into join request |
| **Quick Play Matchmaking** | `MultiplayerService.Instance.MatchmakeSessionAsync(quickJoinOptions, sessionOptions)` with `QuickJoinOptions { CreateSession = true }` (no custom `Timeout`) | Matches open public sessions for the preferred mode across macOS & Windows, or creates a new Relay session automatically |
| **Pre-Match Lobby** | `ISession` properties + `NetworkMatchManager` replicated lobby roster | Connected player list, Alpha/Bravo team assignment or FFA, Host map/mode selector, per-player Ready toggle, Host Start Match + late-join drop-in support |

---

## 4. Replication Design Table

Every replicated value has **exactly one writer** (Invariant 7). Persistent / late-join data is stored in `NetworkVariable` / `NetworkList` (Invariant 11); one-shot presentation events use RPCs (Invariant 10).

| System / Entity | Field / Channel | Type & Container | Sole Writer | Delivery / Rate | Late-Join Behavior | Local Presentation Derived |
|---|---|---|---|---|---|---|
| **Player Avatar** (`NetworkPlayerBridge`) | Quantized Pose (`Position`, `Yaw`, `Pitch`, `Velocity`, `Stance`, `Weapon`) | `NetworkVariable<NetPlayerPose>` (`INetworkSerializable`, Owner write) | **Owner** (with server bounds check) | Unreliable/state sync @ **30 Hz** | Receives latest pose immediately | Local owner runs `CharacterController` + FP rig; remote clients smoothly interpolate/extrapolate 3P `TacticalOperator` + Upper-Body Aim IK + footsteps + stance anims |
| **Player Avatar** | `TeleportNonce`, `AuthoritativeSpawnPos`, `AuthoritativeSpawnYaw` | `NetworkVariable<NetSpawnCommand>` (Server write) | **Server/Host** | On spawn/respawn | Receives current spawn state | Owner snaps `CharacterController` cleanly and resets velocity/camera pitch |
| **Player Avatar** | `Health` (0–100), `Armor` (0–50), `IsAlive`, `RespawnEndTime` | `NetworkVariable<NetVitalsState>` (Server write) | **Server/Host** | On change | Exact current HP/Armor/Alive state | HUD bars, low-health vignette/heartbeat, ragdoll/death camera, respawn countdown |
| **Player Avatar** | `SlotIndex`, `Team`, `Callsign`, `PlayerId`, `Kills`, `Deaths`, `Assists`, `Score`, `PingMs`, `IsReady` | `NetworkVariable<NetPlayerIdentity>` & `NetworkList<NetScoreboardEntry>` (Server write) | **Server/Host** | On join/score/1 Hz ping | Full roster & scoreboard synced | Nameplates, Alpha (amber) / Bravo (cyan) visor tint, HUD scoreboard, lobby roster |
| **Weapon Combat** | Shot Request (`WeaponId`, `MuzzlePos`, `AimOrigin`, `AimDir`, `ShotSeq`, `ClientTime`) | `FireWeaponServerRpc` (Client -> Server, Reliable) | **Owner -> Server validates** | On trigger pull (rate-limited by server) | Not needed (one-shot) | Local shooter plays immediate recoil, muzzle flash, sound, tracer; Server validates with lag compensation |
| **Weapon Combat** | Shot Visual Broadcast (`ShooterClientId`, `WeaponId`, `MuzzlePos`, `HitPoint`, `HitNormal`, `Surface`, `DidHit`) | `BroadcastShotVisualClientRpc` (Server -> All) | **Server/Host** | On validated shot | Not needed (one-shot) | Remote peers (skipping `IsLocalShooter` to avoid double-render on both Host and Clients!) render tracer beam, muzzle flash, 3D gunshot audio, surface impact VFX/decal |
| **Weapon Combat** | Hit Confirm (`IsHeadshot`, `IsKill`, `Damage`, `HitPoint`, `VictimName`) | `HitConfirmClientRpc` (Targeted to Shooter `ClientRpcParams`) | **Server/Host** | On validated hit | Not needed | Crosshair hitmarker (body/headshot/kill), hitmarker SFX, floating damage confirmation |
| **Weapon Combat** | Damage Indicator (`AttackerOrigin`, `Damage`) | `DamageFeedbackClientRpc` (Targeted to Victim) | **Server/Host** | On taking damage | Not needed | Directional red HUD hit arc, screen flinch, hurt SFX |
| **Match & Round** (`NetworkMatchManager`) | `Phase`, `Mode`, `MapName`, `AlphaScore`, `BravoScore`, `ScoreLimit`, `PhaseEndServerTime`, `WinningTeam`, `WinnerCallsign` | `NetworkVariable<NetMatchState>` (Server write) | **Server/Host** | On change (stores authoritative `EndServerTime`, never ticks countdown every frame!) | Exact phase, scores, remaining time calculated from `NetworkManager.ServerTime.Time` | Countdown overlay, HUD timer/score, victory/defeat banner, music stinger, cursor unlock |
| **Kill Feed** | Kill event (`Seq`, `Killer`, `KillerTeam`, `Victim`, `VictimTeam`, `Weapon`, `Headshot`) | `BroadcastKillFeedClientRpc` + bounded ring cache on `NetworkMatchManager` | **Server/Host** | On elimination | Recent kills buffer (last 5) synced or populated on state sync | Tactical kill feed UI rows + announcer callout (Headshot / Double Kill) |
| **Tactical AI Bot** (`NetworkBotSync`) | `NetBotState` (`Position`, `Yaw`, `Pitch`, `Velocity`, `Stance`, `Weapon`, `Health`, `Armor`, `IsAlive`, `Team`, `Callsign`) | `NetworkVariable<NetBotPose>` + `NetworkVariable<NetBotVitals>` (Server write) | **Server/Host** (`NavMeshAgent` runs ONLY when `ShouldRunBotAI == true`) | **20 Hz** pose, on-change vitals | Full bot presence & state | Remote clients disable `NavMeshAgent`, interpolate 3P operator rig, play bot tracers/impacts via shared `BroadcastShotVisualClientRpc` |

---

## 5. Server-Authoritative Lag-Compensated Hit Validation

Because NGO has no built-in server rewind, `LagCompensatedHitValidator` implements deterministic hitbox history rewind on the Host/Server:

1. **History Recording (Server/Host Fixed/Network Tick @ 60 Hz)**:
   - Every spawned `ILagCompensatedTarget` (both human `NetworkPlayerBridge` and AI `NetworkBotSync`) registers automatically.
   - Each tick, the host appends a compact `TargetPoseSample` (`double ServerTime`, `Vector3 RootPos`, `float Yaw`, `bool IsCrouching`, `bool IsAlive`) to a fixed-size **64-entry ring buffer** per target (zero GC allocations).
2. **Authoritative Shot Validation (`ValidateShot`)**:
   - Shooter sends `FireWeaponServerRpc(WeaponFireInput)` with `AimOrigin`, `AimDirection`, `Weapon`, `ShotSeq`.
   - Host validates:
     1. Shooter is alive and `MatchPhase == InProgress`.
     2. Fire rate guard (`serverTime - lastFireTime >= weaponSpec.FireInterval * 0.85f`).
     3. Origin proximity guard (`Vector3.Distance(aimOrigin, shooterEyePos) <= 1.75f`) to block teleport-origin exploits.
   - **Rewind Time Calculation**:
     - For remote clients: `rttSeconds = GetClientRttSeconds(shooterClientId)`; `rewindOffset = Mathf.Clamp(rttSeconds * 0.5f + interpDelay, 0f, 0.25f)`; `evalTime = serverTime - rewindOffset`.
     - For Listen-Server Host & AI Bots: `evalTime = serverTime` (0 ms rewind).
   - **Occlusion + Rewound Capsule/Sphere Hit Test**:
     - First raycast against static world geometry (`IVanguardSpawnProvider.WorldGeometryMask`) up to `weaponSpec.MaxRange` to find `wallDistance`.
     - For each living enemy target (respecting TDM team rules or FFA), interpolate their historical `RootPos` & `IsCrouching` at `evalTime` from the ring buffer.
     - Test ray against exact mathematical **Head Sphere** (higher priority, headshot multiplier) and **Body Capsule** (adjusted for standing vs crouching height) closer than `wallDistance`.
     - For `M890` Shotgun: deterministically generate 8 pellets using `Hash(ShotSeq, shooterClientId, pelletIndex)` within the weapon's spread cone and validate each pellet (accumulating total damage and headshot flag cleanly).
   - **Authoritative Outcome**:
     - Apply armor absorption (35% absorbed by armor until armor = 0, remainder to health), clamp health, check elimination.
     - On kill: increment attacker kills/score & team score, increment victim deaths, trigger respawn timer, emit Kill Feed RPC, check score limit for `MatchOver`.

---

## 6. Lifecycle: Identity, Spawn, Late Join, Disconnect & Host Loss

1. **Identity & Slot Assignment (Invariant 8)**:
   - On connection approval / `OnClientConnectedCallback`, the Host assigns a unique `SlotIndex` (0..7), reconciles the player's requested `Callsign` (sanitized, unique fallback `"OPERATOR-0X"`), and assigns balanced `TeamId` (`Alpha` vs `Bravo` in TDM, `None` in FFA) before spawning the avatar.
2. **Spawn & Respawn**:
   - Host queries `IVanguardSpawnProvider.GetBestSpawnPoint(team, enemyPositions)` (choosing the furthest spawn point with clear line-of-sight away from active enemies) and sets `AuthoritativeSpawnPos/Yaw` + increments `TeleportNonce`.
   - On death, `IsAlive = false`, `RespawnEndTime = serverTime + RespawnDelaySeconds` (3.5 s). When timer elapses on Host, Host resets HP=100, Armor=50, `IsAlive = true`, picks a fresh safe spawn point, and increments `TeleportNonce`.
3. **Late Join (Drop-In Support)**:
   - If a client joins while `MatchPhase == InProgress`, NGO's `NetworkSceneManager` automatically loads `Blacksite09` for the joiner, synchronizes `NetworkMatchManager`'s `NetMatchState`, scoreboard, and all active players/bots, and Host immediately assigns a balanced team slot and spawns the joiner's player avatar (despawning one backfill AI bot if the room is at target combatant count!).
4. **Client Disconnect**:
   - Host removes the disconnected player's scoreboard slot, despawns their `NetworkObject`, and backfills an AI bot if `FillWithBots` is active and match is in progress.
5. **Host Disconnect / Graceful Exit**:
   - Host leaving calls `ISession.LeaveAsync()` (or `IHostSession.DeleteAsync()`), shuts down `NetworkManager`, and transitions cleanly back to `MainMenu`.
   - Remote clients receive `NetworkManager.OnClientDisconnectCallback` / `ISession.Deleted`, call `LeaveSessionAsync("Host ended the match")`, unlock `Cursor.lockState = CursorLockMode.None`, load `MainMenu`, and show a clear non-blocking notification banner.

---

## 7. C# Interface Contract for `@unity-core`

All networking scripts live exclusively in `Assets/Scripts/Networking/` under namespace **`VanguardZero.Networking`**.  
`@unity-core` builds Gameplay, Weapons, AI, Arena, and UI against these exact types & interfaces (zero hard coupling to NGO internals inside `Assets/Scripts/UI/`, `Weapons/`, `Gameplay/`, or `AI/`!).

### 7.1 Service Locator (`VanguardNetServices`)

```csharp
namespace VanguardZero.Networking
{
    public static class VanguardNetServices
    {
        public static INetworkSessionService Session { get; }
        public static INetworkMatchService Match { get; }
        public static IVanguardSpawnProvider SpawnProvider { get; }

        // Called by unity-core's Blacksite09 arena bootstrap on Awake/OnDestroy
        public static void RegisterSpawnProvider(IVanguardSpawnProvider provider);
        public static void UnregisterSpawnProvider(IVanguardSpawnProvider provider);

        // Authoritative shared weapon specs (damage, RPM, spread, mag size, reload)
        public static WeaponBallisticsSpec GetWeaponSpec(WeaponId weapon);
    }
}
```

### 7.2 Shared Enums & DTOs

```csharp
namespace VanguardZero.Networking
{
    public enum MatchMode : byte { TeamDeathmatch = 0, FreeForAll = 1, SoloSkirmish = 2 }
    public enum TeamId : byte { None = 0, Alpha = 1, Bravo = 2 }
    public enum MatchPhase : byte { MainMenu = 0, Lobby = 1, Loading = 2, Countdown = 3, InProgress = 4, RoundOver = 5, MatchOver = 6 }
    public enum WeaponId : byte { VX47_AssaultRifle = 0, M890_TacticalShotgun = 1, M19_TacticalPistol = 2 }
    public enum HitSurfaceType : byte { Concrete = 0, Metal = 1, Flesh = 2, Armor = 3, Dirt = 4 }
    public enum ConnectionMode : byte { OfflineSolo = 0, UnityRelay = 1, LanDirect = 2 }
    public enum CloudServiceState : byte { Uninitialized = 0, Initializing = 1, Ready = 2, UnavailableFallback = 3 }

    [System.Flags]
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

    public struct SessionOpResult
    {
        public bool Success;
        public string ErrorMessage;
        public string JoinCode;
        public ConnectionMode Mode;
    }

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
    }

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
    }

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

    public struct PlayerPoseSnapshot
    {
        public UnityEngine.Vector3 Position;
        public float Yaw;
        public float Pitch;
        public UnityEngine.Vector3 Velocity;
        public PlayerStanceFlags Stance;
        public WeaponId ActiveWeapon;
    }

    public struct WeaponFireInput
    {
        public WeaponId Weapon;
        public UnityEngine.Vector3 MuzzlePosition;
        public UnityEngine.Vector3 AimOrigin;
        public UnityEngine.Vector3 AimDirection;
        public uint ShotSequence;
        public float ClientTimeSeconds;
    }

    public struct WeaponShotVisualEvent
    {
        public ulong ShooterNetworkObjectId;
        public WeaponId Weapon;
        public UnityEngine.Vector3 MuzzlePosition;
        public UnityEngine.Vector3 HitPoint;
        public UnityEngine.Vector3 HitNormal;
        public HitSurfaceType Surface;
        public bool DidHit;
        public bool IsLocalShooter;
    }

    public struct HitConfirmEvent
    {
        public bool IsHeadshot;
        public bool IsKill;
        public int DamageDealt;
        public UnityEngine.Vector3 HitPoint;
        public string VictimCallsign;
    }

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

    public struct WeaponBallisticsSpec
    {
        public WeaponId Id;
        public string DisplayName;
        public int BaseDamage;
        public float HeadshotMultiplier;
        public int PelletsPerShot;
        public float SpreadDegrees;
        public float MaxRange;
        public float FireIntervalSeconds;
        public int MagazineSize;
        public float ReloadDurationSeconds;
    }
}
```

### 7.3 Core Interfaces

```csharp
namespace VanguardZero.Networking
{
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

        System.Threading.Tasks.Task<SessionOpResult> InitializeServicesAsync(string callsign = null);
        System.Threading.Tasks.Task SetCallsignAsync(string callsign);
        System.Threading.Tasks.Task<SessionOpResult> HostSessionAsync(HostSessionRequest request);
        System.Threading.Tasks.Task<SessionOpResult> HostLanDirectAsync(HostSessionRequest request, ushort port = 7777);
        System.Threading.Tasks.Task<SessionOpResult> JoinByCodeAsync(string joinCode);
        System.Threading.Tasks.Task<SessionOpResult> JoinSessionByIdAsync(string sessionId);
        System.Threading.Tasks.Task<SessionOpResult> JoinLanDirectAsync(string ipAddress, ushort port = 7777);
        System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<LobbySessionInfo>> QueryOpenLobbiesAsync(MatchMode? modeFilter = null);
        System.Threading.Tasks.Task<SessionOpResult> QuickPlayAsync(MatchMode preferredMode = MatchMode.TeamDeathmatch);
        System.Threading.Tasks.Task<SessionOpResult> StartSoloSkirmishAsync(MatchRulesConfig rules);
        System.Threading.Tasks.Task SetLocalReadyAsync(bool isReady);
        System.Threading.Tasks.Task SetLocalPreferredTeamAsync(TeamId team);
        System.Threading.Tasks.Task UpdateLobbyRulesAsync(MatchRulesConfig rules);
        System.Threading.Tasks.Task StartMatchFromLobbyAsync();
        System.Threading.Tasks.Task LeaveSessionAsync(string reason = null);

        event System.Action<CloudServiceState, string> OnCloudStateChanged;
        event System.Action<LobbySessionInfo> OnSessionJoined;
        event System.Action<string> OnSessionLeft;
        event System.Action<System.Collections.Generic.IReadOnlyList<PlayerCombatStats>> OnLobbyRosterChanged;
        event System.Action<MatchRulesConfig> OnLobbyRulesChanged;
    }

    public interface INetworkMatchService
    {
        MatchStateSnapshot CurrentState { get; }
        System.Collections.Generic.IReadOnlyList<PlayerCombatStats> Scoreboard { get; }
        System.Collections.Generic.IReadOnlyList<KillFeedEntry> RecentKills { get; }

        void RequestTeamSwitch(TeamId requestedTeam);
        void RequestToggleReady();
        void RequestStartMatch();
        void RequestRematch();
        bool TryGetLocalPlayerStats(out PlayerCombatStats stats);

        event System.Action<MatchStateSnapshot> OnMatchStateChanged;
        event System.Action<System.Collections.Generic.IReadOnlyList<PlayerCombatStats>> OnScoreboardUpdated;
        event System.Action<KillFeedEntry> OnKillFeedEntryAdded;
        event System.Action<string, WeaponId, bool, float> OnLocalPlayerEliminated;
        event System.Action<UnityEngine.Vector3, UnityEngine.Quaternion> OnLocalPlayerRespawned;
        event System.Action<MatchStateSnapshot, System.Collections.Generic.IReadOnlyList<PlayerCombatStats>> OnMatchEnded;
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

        event System.Action<PlayerPoseSnapshot> OnRemotePoseReceived;
        event System.Action<int, int, UnityEngine.Vector3> OnHealthArmorChanged;
        event System.Action<HitConfirmEvent> OnHitConfirmed;
        event System.Action<WeaponShotVisualEvent> OnShotVisualReceived;
        event System.Action<UnityEngine.Vector3, UnityEngine.Quaternion> OnAuthoritativeTeleport;
        event System.Action<bool> OnAliveStateChanged;
        event System.Action<TeamId> OnTeamAssigned;
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

        event System.Action<PlayerPoseSnapshot> OnBotPoseUpdated;
        event System.Action<bool> OnBotAliveStateChanged;
        event System.Action<TeamId> OnBotTeamAssigned;
        event System.Action<WeaponShotVisualEvent> OnBotShotVisual;
    }

    /// <summary>Implemented by unity-core's Blacksite09 Arena/Spawn manager.</summary>
    public interface IVanguardSpawnProvider
    {
        UnityEngine.LayerMask WorldGeometryMask { get; }
        UnityEngine.Transform GetBestSpawnPoint(TeamId team, System.Collections.Generic.IReadOnlyList<UnityEngine.Vector3> activeEnemyPositions);
        System.Collections.Generic.IReadOnlyList<UnityEngine.Transform> GetAllSpawnPoints(TeamId team);
    }
}
```

---

## 8. Planned Scripts under `Assets/Scripts/Networking/`

| File | Responsibility |
|---|---|
| `NetworkContracts.cs` | Namespace `VanguardZero.Networking` enums, DTOs, weapon ballistics specs, interfaces, and `VanguardNetServices` locator (zero NGO/MPS dependencies). |
| `VanguardNetworkBootstrap.cs` | Persistent `DontDestroyOnLoad` singleton hosting `NetworkManager` wiring, `Application.runInBackground = true` guard, scene-transition hooks, and Editor direct-scene auto-solo fallback. |
| `VanguardSessionManager.cs` | UGS Initialization, Anonymous Auth + Callsign persistence, `ISession` Host/Public/Private/JoinByCode/Browse/QuickPlay via `WithRelayNetwork()`, LAN Direct fallback, Offline Solo Skirmish host, and graceful error/disconnect cleanup. |
| `LagCompensatedHitValidator.cs` | Server/Host 60 Hz ring-buffer hitbox history (Head sphere + Body capsule), RTT-bounded rewind (<= 250 ms), static world occlusion raycast, deterministic shotgun pellet spread, armor/health damage resolution. |
| `NetworkMatchManager.cs` | Authoritative `NetworkBehaviour` owning `MatchPhase`, scores, round timer (`PhaseEndServerTime`), slot/team assignment, scoreboard `NetworkList`, kill feed RPC + recent ring, respawn queue, and dynamic AI bot backfill spawning/despawning. |
| `NetworkPlayerBridge.cs` | `NetworkBehaviour` implementing `IPlayerCombatAuthority` & `ILagCompensatedTarget`: 30 Hz quantized pose sync + remote interpolation/extrapolation, authoritative vitals/spawn teleport, weapon fire RPCs, hit confirm & damage feedback RPCs. |
| `NetworkBotSync.cs` | `NetworkBehaviour` implementing `IBotNetworkBridge` & `ILagCompensatedTarget`: server-only AI authority flag, 20 Hz pose sync, client interpolation, authoritative vitals/respawn, unified weapon fire validation & tracer broadcast. |

---

## 9. Scene & Prefab Wiring Checklist for `@unity-core`

1. **Build Settings Scene Order**:
   - Index `0`: `Assets/Scenes/MainMenu.unity`
   - Index `1`: `Assets/Scenes/Blacksite09.unity`
2. **`MainMenu.unity`**:
   - Add GameObject `VanguardNetworkRoot` (`DontDestroyOnLoad`) with: `NetworkManager` (`TickRate = 60`, `EnableSceneManagement = true`), `UnityTransport`, `VanguardNetworkBootstrap`, `VanguardSessionManager`, `LagCompensatedHitValidator`.
   - Register `Player_Networked.prefab`, `Bot_Networked.prefab`, and `NetworkMatchManager.prefab` in `NetworkManager`'s `DefaultNetworkPrefabs` list (with `Player_Networked` NOT set to auto-spawn before lobby/arena unless managed by `NetworkMatchManager` — set `PlayerPrefab = null` on `NetworkManager` so `NetworkMatchManager` controls team-balanced spawning on match start & late join!).
3. **`Blacksite09.unity`**:
   - Place `BlacksiteSpawnProvider` (implementing `IVanguardSpawnProvider`) with Alpha, Bravo, and FFA spawn points and accurate `WorldGeometryMask`.
   - Place `NetworkMatchManager` (`NetworkObject` + `NetworkMatchManager` component) with references to `Player_Networked.prefab` and `Bot_Networked.prefab`.
4. **Prefabs**:
   - `Player_Networked.prefab`: `NetworkObject` + `NetworkPlayerBridge` + FPS controller (active only when `IPlayerCombatAuthority.IsLocalPlayer == true`) + 3P `TacticalOperator` visual rig (hidden for local owner, driven by `OnRemotePoseReceived` for remotes).
   - `Bot_Networked.prefab`: `NetworkObject` + `NetworkBotSync` + `NavMeshAgent` Tactical Bot AI (active only when `IBotNetworkBridge.ShouldRunBotAI == true`) + 3P `TacticalOperator` visual rig.

---

## 10. Verification Plan (MPPM Gate)

Before declaring networking complete, `@unity-core` and `@verifier` will run cross-process verification in **Multiplayer Play Mode (MPPM)** (Main Editor as Host + Virtual Player Clone as Client):

1. **Pre-flight**: Confirm zero console errors, `PlayerSettings.runInBackground == true`, `netcode.gameobjects audit` passes (and if NGO 3.x installed `com.unity.netcode`, `enable_client_server_bootstrap == DisableAutomaticBootstrap`).
2. **Cross-Process Connect**: Main Editor hosts session (Relay if Cloud linked, plus LAN Direct check); Clone joins from separate process. Quote Clone's `netcode.gameobjects status` (`isConnectedClient: true`, `localClientId`, transport) and Host's `netcode.gameobjects list_clients` showing the clone with live RTT.
3. **Active Gameplay Parity (Host vs Clone)**:
   - Drive player movement on both instances and verify synchronized positions/stances.
   - Fire weapon from Clone at Host (and Host at Clone) -> verify `Health`/`Armor`/`Kills`/`Deaths`/`AlphaScore` `NetworkVariable`s match identically on both processes via `script eval` + Kill Feed entry delivered.
   - Verify no double tracers / no double audio / no duplicate avatars on Listen-Server Host.
4. **Simulated Latency & Lag Compensation Test**:
   - Apply emulated network conditions on Clone (`netcode.gameobjects.simulator set`: `100 ms` delay, `15 ms` jitter, `2%` packet loss), confirm RTT rises, fire at moving target, and verify lag-compensated hits register cleanly without rubber-banding or console errors.
5. **Lifecycle & Unhappy-Path Checks**:
   - Late join during `InProgress` receives full scoreboard, timer, and spawns cleanly.
   - Host exit returns Clone cleanly to `MainMenu` with cursor unlocked and disconnect banner (no stuck state).
   - Offline / Solo Skirmish launches local host + bots with zero UGS errors.

### Verification Evidence Log

*(To be populated with timestamped command outputs after implementation and MPPM runs.)*
