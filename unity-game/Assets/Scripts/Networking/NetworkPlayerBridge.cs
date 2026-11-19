using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace VanguardZero.Networking
{
    /// <summary>
    /// Networked Player Controller bridge attached to Player_Networked.prefab.
    /// Implements IPlayerCombatAuthority (consumed by unity-core's FPS controller, weapons, 3P rig, HUD)
    /// and ILagCompensatedTarget (sampled at 60 Hz by LagCompensatedHitValidator on Server/Host).
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkPlayerBridge : NetworkBehaviour, IPlayerCombatAuthority, ILagCompensatedTarget
    {
        public const int DefaultMaxHealth = 100;
        public const int DefaultMaxArmor = 50;
        public const float PoseSendInterval = 1f / 30f; // 30 Hz owner pose updates
        public const float RemoteInterpSpeed = 16f;
        public const float RemoteSnapDistance = 4.5f;
        public const float MaxExtrapolationSeconds = 0.10f;

        private readonly NetworkVariable<NetPlayerPose> m_NetPose = new NetworkVariable<NetPlayerPose>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<NetSpawnCommand> m_NetSpawnCommand = new NetworkVariable<NetSpawnCommand>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<NetVitalsState> m_NetVitals = new NetworkVariable<NetVitalsState>(
            new NetVitalsState { Health = DefaultMaxHealth, Armor = DefaultMaxArmor, IsAlive = true },
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<NetPlayerIdentity> m_NetIdentity = new NetworkVariable<NetPlayerIdentity>(
            new NetPlayerIdentity { Callsign = "OPERATOR", TeamRaw = (byte)TeamId.Alpha, IsReady = true },
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<short> m_NetPingMs = new NetworkVariable<short>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        // IPlayerCombatAuthority properties
        public new bool IsLocalPlayer => !IsSpawned || IsOwner;
        public bool IsAlive => IsSpawned ? m_NetVitals.Value.IsAlive : m_OfflineAlive;
        public int Health => IsSpawned ? m_NetVitals.Value.Health : m_OfflineHealth;
        public int MaxHealth => DefaultMaxHealth;
        public int Armor => IsSpawned ? m_NetVitals.Value.Armor : m_OfflineArmor;
        public int MaxArmor => DefaultMaxArmor;
        public TeamId Team => IsSpawned ? m_NetIdentity.Value.Team : TeamId.Alpha;
        public WeaponId EquippedWeapon => IsSpawned ? m_NetPose.Value.Weapon : m_LocalCachedPose.ActiveWeapon;
        public string Callsign => IsSpawned ? m_NetIdentity.Value.Callsign.ToString() : "OPERATOR";
        public int PingMs => IsSpawned ? m_NetPingMs.Value : 0;
        public new ulong OwnerClientId => IsSpawned ? base.OwnerClientId : 0UL;

        // ILagCompensatedTarget properties (server records unextrapolated authoritative/received pose, zero overshoot)
        public ulong TargetNetworkObjectId => IsSpawned ? NetworkObjectId : (ulong)GetInstanceID();
        public ulong OwnerClientIdOrBot => OwnerClientId;
        public bool IsBotTarget => false;
        public Vector3 CurrentRootPosition => IsLocalPlayer ? m_LocalCachedPose.Position : (m_HasRemotePose ? m_RemoteTargetPos : transform.position);
        public float CurrentYaw => IsLocalPlayer ? m_LocalCachedPose.Yaw : (m_HasRemotePose ? m_RemoteTargetYaw : m_RemoteRenderYaw);
        public bool IsCrouching => ((IsLocalPlayer ? m_LocalCachedPose.Stance : m_NetPose.Value.Stance) & (PlayerStanceFlags.Crouching | PlayerStanceFlags.Sliding)) != 0;

        public double RespawnEndServerTime => m_NetVitals.Value.RespawnEndServerTime;
        public byte SlotIndex => m_NetIdentity.Value.SlotIndex;

        public event Action<PlayerPoseSnapshot> OnRemotePoseReceived;
        public event Action<int, int, Vector3> OnHealthArmorChanged;
        public event Action<HitConfirmEvent> OnHitConfirmed;
        public event Action<WeaponShotVisualEvent> OnShotVisualReceived;
        public event Action<Vector3, Quaternion> OnAuthoritativeTeleport;
        public event Action<bool> OnAliveStateChanged;
        public event Action<TeamId> OnTeamAssigned;

        private PlayerPoseSnapshot m_LocalCachedPose;
        private float m_LastPoseSendTime;
        private PlayerStanceFlags m_LastSentStance;
        private WeaponId m_LastSentWeapon;
        private uint m_LastAppliedTeleportNonce;

        // Remote interpolation state
        private Vector3 m_RemoteTargetPos;
        private Vector3 m_RemoteTargetVel;
        private float m_RemoteTargetYaw;
        private float m_RemoteTargetPitch;
        private float m_RemoteRenderYaw;
        private float m_RemoteRenderPitch;
        private float m_RemoteLastPacketTime;
        private bool m_HasRemotePose;

        // Offline standalone fallback state (if prefab placed directly without spawning)
        private int m_OfflineHealth = DefaultMaxHealth;
        private int m_OfflineArmor = DefaultMaxArmor;
        private bool m_OfflineAlive = true;

        private void Awake()
        {
            m_LocalCachedPose.Position = transform.position;
            m_LocalCachedPose.Yaw = transform.eulerAngles.y;
            m_RemoteTargetPos = transform.position;
            m_RemoteTargetYaw = transform.eulerAngles.y;
            m_RemoteRenderYaw = m_RemoteTargetYaw;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            m_NetPose.OnValueChanged += HandleNetPoseChanged;
            m_NetSpawnCommand.OnValueChanged += HandleSpawnCommandChanged;
            m_NetVitals.OnValueChanged += HandleVitalsChanged;
            m_NetIdentity.OnValueChanged += HandleIdentityChanged;

            LagCompensatedHitValidator.RegisterTarget(this);

            if (IsOwner)
            {
                var session = VanguardNetServices.Session;
                if (session != null)
                {
                    ReportLocalIdentityRpc(
                        NetWireHelpers.ToFixed32(VanguardSessionManager.SanitizeCallsign(session.LocalCallsign)),
                        NetWireHelpers.ToFixed64(session.LocalPlayerId ?? string.Empty));
                }

                // Apply initial authoritative spawn teleport if already populated
                NetSpawnCommand initialSpawn = m_NetSpawnCommand.Value;
                if (initialSpawn.TeleportNonce > 0 && initialSpawn.TeleportNonce != m_LastAppliedTeleportNonce)
                {
                    ApplyTeleportLocally(initialSpawn);
                }
            }
            else
            {
                NetPlayerPose initialPose = m_NetPose.Value;
                m_RemoteTargetPos = initialPose.Position != Vector3.zero ? initialPose.Position : transform.position;
                m_RemoteTargetVel = initialPose.Velocity;
                m_RemoteTargetYaw = initialPose.Yaw;
                m_RemoteTargetPitch = initialPose.Pitch;
                m_RemoteRenderYaw = initialPose.Yaw;
                m_RemoteRenderPitch = initialPose.Pitch;
                m_RemoteLastPacketTime = Time.time;
                m_HasRemotePose = true;
                transform.SetPositionAndRotation(m_RemoteTargetPos, Quaternion.Euler(0f, m_RemoteRenderYaw, 0f));
            }

            // Fire initial state callbacks for UI & visual rig parity (including late joiners)
            OnTeamAssigned?.Invoke(m_NetIdentity.Value.Team);
            OnHealthArmorChanged?.Invoke(m_NetVitals.Value.Health, m_NetVitals.Value.Armor, m_NetVitals.Value.LastDamageSourcePos);
            OnAliveStateChanged?.Invoke(m_NetVitals.Value.IsAlive);
        }

        public override void OnNetworkDespawn()
        {
            m_NetPose.OnValueChanged -= HandleNetPoseChanged;
            m_NetSpawnCommand.OnValueChanged -= HandleSpawnCommandChanged;
            m_NetVitals.OnValueChanged -= HandleVitalsChanged;
            m_NetIdentity.OnValueChanged -= HandleIdentityChanged;

            LagCompensatedHitValidator.UnregisterTarget(this);
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            LagCompensatedHitValidator.UnregisterTarget(this);
            base.OnDestroy();
        }

        private void Update()
        {
            if (!IsSpawned || IsOwner || !m_HasRemotePose)
            {
                return;
            }

            // Smooth remote avatar interpolation with bounded velocity extrapolation
            float dt = Time.deltaTime;
            float age = Mathf.Clamp(Time.time - m_RemoteLastPacketTime, 0f, MaxExtrapolationSeconds);
            Vector3 extrapolatedTarget = m_RemoteTargetPos + m_RemoteTargetVel * age;

            if (Vector3.Distance(transform.position, extrapolatedTarget) > RemoteSnapDistance)
            {
                transform.position = extrapolatedTarget;
            }
            else
            {
                float t = 1f - Mathf.Exp(-RemoteInterpSpeed * dt);
                transform.position = Vector3.Lerp(transform.position, extrapolatedTarget, t);
            }

            float rotT = 1f - Mathf.Exp(-RemoteInterpSpeed * dt);
            m_RemoteRenderYaw = Mathf.LerpAngle(m_RemoteRenderYaw, m_RemoteTargetYaw, rotT);
            m_RemoteRenderPitch = Mathf.Lerp(m_RemoteRenderPitch, m_RemoteTargetPitch, rotT);
            transform.rotation = Quaternion.Euler(0f, m_RemoteRenderYaw, 0f);

            NetPlayerPose raw = m_NetPose.Value;
            OnRemotePoseReceived?.Invoke(new PlayerPoseSnapshot
            {
                Position = transform.position,
                Yaw = m_RemoteRenderYaw,
                Pitch = m_RemoteRenderPitch,
                Velocity = m_RemoteTargetVel,
                Stance = raw.Stance,
                ActiveWeapon = raw.Weapon
            });
        }

        // =====================================================================
        // IPlayerCombatAuthority (Called by unity-core FPS controller & weapons)
        // =====================================================================

        public void SubmitLocalPose(in PlayerPoseSnapshot pose)
        {
            if (!IsLocalPlayer)
            {
                return;
            }

            m_LocalCachedPose = pose;
            if (!IsSpawned)
            {
                return;
            }

            bool importantChanged = pose.Stance != m_LastSentStance || pose.ActiveWeapon != m_LastSentWeapon;
            if (!importantChanged && (Time.unscaledTime - m_LastPoseSendTime) < PoseSendInterval)
            {
                return;
            }

            m_LastPoseSendTime = Time.unscaledTime;
            m_LastSentStance = pose.Stance;
            m_LastSentWeapon = pose.ActiveWeapon;
            m_NetPose.Value = NetPlayerPose.FromSnapshot(pose);
        }

        public void SubmitWeaponFire(in WeaponFireInput fireInput)
        {
            if (!IsLocalPlayer || !IsAlive)
            {
                return;
            }

            var req = new NetFireShotRequest
            {
                WeaponRaw = (byte)fireInput.Weapon,
                MuzzlePosition = fireInput.MuzzlePosition,
                AimOrigin = fireInput.AimOrigin,
                AimDirection = fireInput.AimDirection,
                ShotSequence = fireInput.ShotSequence,
                ClientTimeSeconds = fireInput.ClientTimeSeconds
            };

            if (IsSpawned)
            {
                SubmitFireWeaponRpc(req);
            }
            else if (LagCompensatedHitValidator.Instance != null)
            {
                // Standalone offline scene fallback
                var res = LagCompensatedHitValidator.Instance.ValidateAndResolveShot(this, req, MatchMode.SoloSkirmish);
                if (res.Validated && res.DidHitCombatant)
                {
                    OnHitConfirmed?.Invoke(new HitConfirmEvent
                    {
                        IsHeadshot = res.WasHeadshot,
                        IsKill = res.WasKill,
                        DamageDealt = res.TotalDamageDealt,
                        HitPoint = res.ImpactPoint,
                        VictimCallsign = res.VictimCallsign ?? string.Empty
                    });
                }
            }
        }

        public void SubmitWeaponSwitch(WeaponId weaponId)
        {
            if (!IsLocalPlayer)
            {
                return;
            }

            m_LocalCachedPose.ActiveWeapon = weaponId;
            if (IsSpawned)
            {
                m_LastSentWeapon = weaponId;
                NetPlayerPose current = m_NetPose.Value;
                current.Weapon = weaponId;
                m_NetPose.Value = current;
            }
        }

        public void SubmitReloadStart()
        {
            if (!IsLocalPlayer)
            {
                return;
            }

            m_LocalCachedPose.Stance |= PlayerStanceFlags.Reloading;
            if (IsSpawned)
            {
                NetPlayerPose current = m_NetPose.Value;
                current.Stance |= PlayerStanceFlags.Reloading;
                m_NetPose.Value = current;
            }
        }

        // =====================================================================
        // Server/Host Authoritative Management (called by NetworkMatchManager)
        // =====================================================================

        public void ServerInitializePlayer(byte slotIndex, TeamId team, string callsign, string playerId, Vector3 spawnPos, float spawnYaw)
        {
            if (!IsServer)
            {
                return;
            }

            m_NetIdentity.Value = new NetPlayerIdentity
            {
                SlotIndex = slotIndex,
                TeamRaw = (byte)team,
                IsReady = true,
                Callsign = NetWireHelpers.ToFixed32(VanguardSessionManager.SanitizeCallsign(callsign)),
                PlayerId = NetWireHelpers.ToFixed64(playerId ?? string.Empty)
            };

            ServerRespawnAt(spawnPos, spawnYaw);
        }

        public void ServerRespawnAt(Vector3 spawnPos, float spawnYaw)
        {
            if (!IsServer)
            {
                return;
            }

            m_NetVitals.Value = new NetVitalsState
            {
                Health = DefaultMaxHealth,
                Armor = DefaultMaxArmor,
                IsAlive = true,
                RespawnEndServerTime = 0,
                LastDamageSourcePos = Vector3.zero
            };

            uint nextNonce = m_NetSpawnCommand.Value.TeleportNonce + 1;
            var cmd = new NetSpawnCommand
            {
                TeleportNonce = nextNonce,
                Position = spawnPos,
                Yaw = spawnYaw
            };
            m_NetSpawnCommand.Value = cmd;

            transform.SetPositionAndRotation(spawnPos, Quaternion.Euler(0f, spawnYaw, 0f));
            m_LocalCachedPose.Position = spawnPos;
            m_LocalCachedPose.Yaw = spawnYaw;
        }

        public void ServerSetTeam(TeamId team)
        {
            if (!IsServer)
            {
                return;
            }
            var id = m_NetIdentity.Value;
            id.Team = team;
            m_NetIdentity.Value = id;
        }

        public void ServerSetReady(bool ready)
        {
            if (!IsServer)
            {
                return;
            }
            var id = m_NetIdentity.Value;
            id.IsReady = ready;
            m_NetIdentity.Value = id;
        }

        public void ServerSetPingMs(int pingMs)
        {
            if (!IsServer)
            {
                return;
            }
            short clamped = (short)Mathf.Clamp(pingMs, 0, 999);
            if (m_NetPingMs.Value != clamped)
            {
                m_NetPingMs.Value = clamped;
            }
        }

        public bool ApplyAuthoritativeDamage(
            int damage,
            ulong attackerClientId,
            ulong attackerNetObjectId,
            string attackerName,
            TeamId attackerTeam,
            WeaponId weapon,
            bool isHeadshot,
            Vector3 shotOrigin,
            out int actualDamageDealt)
        {
            actualDamageDealt = 0;
            if (IsSpawned && !IsServer)
            {
                return false;
            }

            if (!IsAlive || damage <= 0)
            {
                return false;
            }

            int currentHealth = Health;
            int currentArmor = Armor;
            int armorAbsorb = 0;

            if (!isHeadshot && currentArmor > 0)
            {
                armorAbsorb = Mathf.Min(currentArmor, Mathf.CeilToInt(damage * 0.35f));
                currentArmor -= armorAbsorb;
            }

            int healthDamage = Mathf.Max(1, damage - armorAbsorb);
            int prevHealth = currentHealth;
            currentHealth = Mathf.Max(0, currentHealth - healthDamage);
            actualDamageDealt = (prevHealth - currentHealth) + armorAbsorb;

            bool eliminated = currentHealth <= 0;

            if (IsSpawned)
            {
                double respawnEnd = 0;
                if (eliminated)
                {
                    float delay = VanguardNetServices.Session != null
                        ? VanguardNetServices.Session.ActiveRules.RespawnDelaySeconds
                        : 3.5f;
                    respawnEnd = NetworkManager.ServerTime.Time + (delay > 0.5f ? delay : 3.5f);
                }

                m_NetVitals.Value = new NetVitalsState
                {
                    Health = (short)currentHealth,
                    Armor = (short)currentArmor,
                    IsAlive = !eliminated,
                    DamageNonce = m_NetVitals.Value.DamageNonce + 1u,
                    RespawnEndServerTime = respawnEnd,
                    LastDamageSourcePos = shotOrigin
                };

                if (eliminated && NetworkMatchManager.Instance != null)
                {
                    NetworkMatchManager.Instance.ServerNotifyCombatantEliminated(
                        attackerClientId,
                        attackerNetObjectId,
                        attackerName,
                        attackerTeam,
                        OwnerClientId,
                        NetworkObjectId,
                        Callsign,
                        Team,
                        weapon,
                        isHeadshot,
                        victimIsBot: false);
                }
            }
            else
            {
                m_OfflineHealth = currentHealth;
                m_OfflineArmor = currentArmor;
                m_OfflineAlive = !eliminated;
                OnHealthArmorChanged?.Invoke(m_OfflineHealth, m_OfflineArmor, shotOrigin);
                if (eliminated)
                {
                    OnAliveStateChanged?.Invoke(false);
                }
            }

            return eliminated;
        }

        // =====================================================================
        // NGO 2.14.0 RPCs (using [Rpc(SendTo.*)] — zero obsolete attributes)
        // =====================================================================

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ReportLocalIdentityRpc(FixedString32Bytes callsign, FixedString64Bytes playerId)
        {
            var id = m_NetIdentity.Value;
            string clean = VanguardSessionManager.SanitizeCallsign(callsign.ToString());
            id.Callsign = NetWireHelpers.ToFixed32(clean);
            if (playerId.Length > 0)
            {
                id.PlayerId = playerId;
            }
            m_NetIdentity.Value = id;

            NetworkMatchManager.Instance?.ServerUpdatePlayerCallsign(OwnerClientId, clean, playerId.ToString());
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitFireWeaponRpc(NetFireShotRequest request)
        {
            var validator = LagCompensatedHitValidator.Instance;
            if (validator == null)
            {
                return;
            }

            MatchMode mode = NetworkMatchManager.Instance != null
                ? NetworkMatchManager.Instance.CurrentState.Mode
                : MatchMode.TeamDeathmatch;

            var res = validator.ValidateAndResolveShot(this, request, mode);
            if (!res.Validated)
            {
                return;
            }

            var visual = new NetShotVisualPayload
            {
                ShooterClientId = OwnerClientId,
                ShooterNetObjectId = NetworkObjectId,
                WeaponRaw = request.WeaponRaw,
                MuzzlePosition = request.MuzzlePosition,
                HitPoint = res.ImpactPoint,
                HitNormal = res.ImpactNormal,
                SurfaceRaw = (byte)res.Surface,
                DidHit = res.DidHitWorldOrTarget
            };

            BroadcastShotVisualRpc(visual);

            if (res.DidHitCombatant)
            {
                var confirm = new NetHitConfirmPayload
                {
                    IsHeadshot = res.WasHeadshot,
                    IsKill = res.WasKill,
                    DamageDealt = (short)Mathf.Clamp(res.TotalDamageDealt, 0, short.MaxValue),
                    HitPoint = res.ImpactPoint,
                    VictimCallsign = NetWireHelpers.ToFixed32(res.VictimCallsign ?? string.Empty)
                };
                NotifyHitConfirmRpc(confirm, RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
            }
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void BroadcastShotVisualRpc(NetShotVisualPayload payload)
        {
            ulong localId = NetworkManager != null ? NetworkManager.LocalClientId : 0UL;
            OnShotVisualReceived?.Invoke(new WeaponShotVisualEvent
            {
                ShooterNetworkObjectId = payload.ShooterNetObjectId,
                Weapon = (WeaponId)payload.WeaponRaw,
                MuzzlePosition = payload.MuzzlePosition,
                HitPoint = payload.HitPoint,
                HitNormal = payload.HitNormal,
                Surface = (HitSurfaceType)payload.SurfaceRaw,
                DidHit = payload.DidHit,
                IsLocalShooter = payload.ShooterClientId == localId
            });
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void NotifyHitConfirmRpc(NetHitConfirmPayload payload, RpcParams rpcParams = default)
        {
            OnHitConfirmed?.Invoke(new HitConfirmEvent
            {
                IsHeadshot = payload.IsHeadshot,
                IsKill = payload.IsKill,
                DamageDealt = payload.DamageDealt,
                HitPoint = payload.HitPoint,
                VictimCallsign = payload.VictimCallsign.ToString()
            });
        }

        // =====================================================================
        // NetworkVariable Change Callbacks
        // =====================================================================

        private void HandleNetPoseChanged(NetPlayerPose previous, NetPlayerPose current)
        {
            if (IsOwner)
            {
                return;
            }

            m_RemoteTargetPos = current.Position;
            m_RemoteTargetVel = current.Velocity;
            m_RemoteTargetYaw = current.Yaw;
            m_RemoteTargetPitch = current.Pitch;
            m_RemoteLastPacketTime = Time.time;
            m_HasRemotePose = true;
        }

        private void HandleSpawnCommandChanged(NetSpawnCommand previous, NetSpawnCommand current)
        {
            if (current.TeleportNonce == 0 || current.TeleportNonce == m_LastAppliedTeleportNonce)
            {
                return;
            }

            if (IsOwner)
            {
                ApplyTeleportLocally(current);
            }
            else
            {
                m_RemoteTargetPos = current.Position;
                m_RemoteTargetVel = Vector3.zero;
                m_RemoteTargetYaw = current.Yaw;
                m_RemoteRenderYaw = current.Yaw;
                transform.SetPositionAndRotation(current.Position, Quaternion.Euler(0f, current.Yaw, 0f));
            }
        }

        private void ApplyTeleportLocally(in NetSpawnCommand cmd)
        {
            m_LastAppliedTeleportNonce = cmd.TeleportNonce;
            Quaternion rot = Quaternion.Euler(0f, cmd.Yaw, 0f);
            transform.SetPositionAndRotation(cmd.Position, rot);
            m_LocalCachedPose.Position = cmd.Position;
            m_LocalCachedPose.Yaw = cmd.Yaw;
            m_LocalCachedPose.Pitch = 0f;
            m_LocalCachedPose.Velocity = Vector3.zero;

            if (IsSpawned && IsOwner)
            {
                m_NetPose.Value = NetPlayerPose.FromSnapshot(m_LocalCachedPose);
            }

            OnAuthoritativeTeleport?.Invoke(cmd.Position, rot);
            NetworkMatchManager.Instance?.NotifyLocalPlayerRespawnedClient(cmd.Position, rot);
        }

        private void HandleVitalsChanged(NetVitalsState previous, NetVitalsState current)
        {
            OnHealthArmorChanged?.Invoke(current.Health, current.Armor, current.LastDamageSourcePos);
            if (previous.IsAlive != current.IsAlive)
            {
                OnAliveStateChanged?.Invoke(current.IsAlive);
            }
        }

        private void HandleIdentityChanged(NetPlayerIdentity previous, NetPlayerIdentity current)
        {
            if (previous.TeamRaw != current.TeamRaw)
            {
                OnTeamAssigned?.Invoke(current.Team);
            }
        }
    }
}
