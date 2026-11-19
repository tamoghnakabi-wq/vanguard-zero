using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace VanguardZero.Networking
{
    /// <summary>
    /// Networked Tactical AI Bot bridge attached to Bot_Networked.prefab.
    /// Runs NavMeshAgent AI strictly on the Host/Server (ShouldRunBotAI == true), replicates
    /// 20 Hz pose + authoritative vitals/team to all clients, and routes bot gunfire through
    /// LagCompensatedHitValidator for identical combat rules.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkBotSync : NetworkBehaviour, IBotNetworkBridge, ILagCompensatedTarget
    {
        public const int DefaultMaxHealth = 100;
        public const int DefaultMaxArmor = 25;
        public const float PoseSendInterval = 1f / 20f; // 20 Hz server pose sync
        public const float RemoteInterpSpeed = 14f;
        public const float RemoteSnapDistance = 4.5f;

        private readonly NetworkVariable<NetPlayerPose> m_NetPose = new NetworkVariable<NetPlayerPose>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<NetVitalsState> m_NetVitals = new NetworkVariable<NetVitalsState>(
            new NetVitalsState { Health = DefaultMaxHealth, Armor = DefaultMaxArmor, IsAlive = true },
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<NetPlayerIdentity> m_NetIdentity = new NetworkVariable<NetPlayerIdentity>(
            new NetPlayerIdentity { Callsign = "BOT-01", TeamRaw = (byte)TeamId.Bravo, IsReady = true },
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        // IBotNetworkBridge properties
        public bool ShouldRunBotAI => !IsSpawned || IsServer;
        public bool IsAlive => IsSpawned ? m_NetVitals.Value.IsAlive : m_OfflineAlive;
        public int Health => IsSpawned ? m_NetVitals.Value.Health : m_OfflineHealth;
        public int Armor => IsSpawned ? m_NetVitals.Value.Armor : m_OfflineArmor;
        public TeamId Team => IsSpawned ? m_NetIdentity.Value.Team : TeamId.Bravo;
        public string Callsign => IsSpawned ? m_NetIdentity.Value.Callsign.ToString() : "BOT-01";
        public WeaponId EquippedWeapon => IsSpawned ? m_NetPose.Value.Weapon : m_ServerCachedPose.ActiveWeapon;

        // ILagCompensatedTarget properties
        public ulong TargetNetworkObjectId => IsSpawned ? NetworkObjectId : (ulong)GetInstanceID();
        public ulong OwnerClientIdOrBot => ulong.MaxValue - (IsSpawned ? NetworkObjectId : (ulong)Mathf.Abs(GetInstanceID()));
        public bool IsBotTarget => true;
        public Vector3 CurrentRootPosition => ShouldRunBotAI ? transform.position : m_RemoteTargetPos;
        public float CurrentYaw => ShouldRunBotAI ? transform.eulerAngles.y : m_RemoteRenderYaw;
        public bool IsCrouching => ((ShouldRunBotAI ? m_ServerCachedPose.Stance : m_NetPose.Value.Stance) & (PlayerStanceFlags.Crouching | PlayerStanceFlags.Sliding)) != 0;

        public double RespawnEndServerTime => m_NetVitals.Value.RespawnEndServerTime;

        public event Action<PlayerPoseSnapshot> OnBotPoseUpdated;
        public event Action<bool> OnBotAliveStateChanged;
        public event Action<TeamId> OnBotTeamAssigned;
        public event Action<WeaponShotVisualEvent> OnBotShotVisual;

        private PlayerPoseSnapshot m_ServerCachedPose;
        private float m_LastPoseSendTime;
        private PlayerStanceFlags m_LastSentStance;
        private WeaponId m_LastSentWeapon;

        private Vector3 m_RemoteTargetPos;
        private Vector3 m_RemoteTargetVel;
        private float m_RemoteTargetYaw;
        private float m_RemoteTargetPitch;
        private float m_RemoteRenderYaw;
        private float m_RemoteRenderPitch;
        private bool m_HasRemotePose;

        private int m_OfflineHealth = DefaultMaxHealth;
        private int m_OfflineArmor = DefaultMaxArmor;
        private bool m_OfflineAlive = true;

        private void Awake()
        {
            m_RemoteTargetPos = transform.position;
            m_RemoteTargetYaw = transform.eulerAngles.y;
            m_RemoteRenderYaw = m_RemoteTargetYaw;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            m_NetPose.OnValueChanged += HandleNetPoseChanged;
            m_NetVitals.OnValueChanged += HandleVitalsChanged;
            m_NetIdentity.OnValueChanged += HandleIdentityChanged;

            LagCompensatedHitValidator.RegisterTarget(this);

            if (!ShouldRunBotAI)
            {
                NetPlayerPose p = m_NetPose.Value;
                m_RemoteTargetPos = p.Position != Vector3.zero ? p.Position : transform.position;
                m_RemoteTargetVel = p.Velocity;
                m_RemoteTargetYaw = p.Yaw;
                m_RemoteTargetPitch = p.Pitch;
                m_RemoteRenderYaw = p.Yaw;
                m_RemoteRenderPitch = p.Pitch;
                m_HasRemotePose = true;
                transform.SetPositionAndRotation(m_RemoteTargetPos, Quaternion.Euler(0f, m_RemoteRenderYaw, 0f));
            }

            OnBotTeamAssigned?.Invoke(m_NetIdentity.Value.Team);
            OnBotAliveStateChanged?.Invoke(m_NetVitals.Value.IsAlive);
        }

        public override void OnNetworkDespawn()
        {
            m_NetPose.OnValueChanged -= HandleNetPoseChanged;
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
            if (ShouldRunBotAI || !m_HasRemotePose)
            {
                return;
            }

            float dt = Time.deltaTime;
            if (Vector3.Distance(transform.position, m_RemoteTargetPos) > RemoteSnapDistance)
            {
                transform.position = m_RemoteTargetPos;
            }
            else
            {
                float t = 1f - Mathf.Exp(-RemoteInterpSpeed * dt);
                transform.position = Vector3.Lerp(transform.position, m_RemoteTargetPos, t);
            }

            float rotT = 1f - Mathf.Exp(-RemoteInterpSpeed * dt);
            m_RemoteRenderYaw = Mathf.LerpAngle(m_RemoteRenderYaw, m_RemoteTargetYaw, rotT);
            m_RemoteRenderPitch = Mathf.Lerp(m_RemoteRenderPitch, m_RemoteTargetPitch, rotT);
            transform.rotation = Quaternion.Euler(0f, m_RemoteRenderYaw, 0f);

            NetPlayerPose raw = m_NetPose.Value;
            OnBotPoseUpdated?.Invoke(new PlayerPoseSnapshot
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
        // IBotNetworkBridge (Called by unity-core Tactical Bot AI on Server)
        // =====================================================================

        public void PushBotPose(in PlayerPoseSnapshot pose)
        {
            if (!ShouldRunBotAI)
            {
                return;
            }

            m_ServerCachedPose = pose;
            if (!IsSpawned)
            {
                OnBotPoseUpdated?.Invoke(pose);
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

        public void FireBotWeapon(in WeaponFireInput fireInput)
        {
            if (!ShouldRunBotAI || !IsAlive)
            {
                return;
            }

            var validator = LagCompensatedHitValidator.Instance;
            if (validator == null)
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

            MatchMode mode = NetworkMatchManager.Instance != null
                ? NetworkMatchManager.Instance.CurrentState.Mode
                : MatchMode.TeamDeathmatch;

            var res = validator.ValidateAndResolveShot(this, req, mode);
            if (!res.Validated)
            {
                return;
            }

            var visual = new NetShotVisualPayload
            {
                ShooterClientId = ulong.MaxValue,
                ShooterNetObjectId = TargetNetworkObjectId,
                WeaponRaw = req.WeaponRaw,
                MuzzlePosition = req.MuzzlePosition,
                HitPoint = res.ImpactPoint,
                HitNormal = res.ImpactNormal,
                SurfaceRaw = (byte)res.Surface,
                DidHit = res.DidHitWorldOrTarget
            };

            if (IsSpawned)
            {
                BroadcastBotShotVisualRpc(visual);
            }
            else
            {
                OnBotShotVisual?.Invoke(new WeaponShotVisualEvent
                {
                    ShooterNetworkObjectId = visual.ShooterNetObjectId,
                    Weapon = (WeaponId)visual.WeaponRaw,
                    MuzzlePosition = visual.MuzzlePosition,
                    HitPoint = visual.HitPoint,
                    HitNormal = visual.HitNormal,
                    Surface = (HitSurfaceType)visual.SurfaceRaw,
                    DidHit = visual.DidHit,
                    IsLocalShooter = false
                });
            }
        }

        // =====================================================================
        // Server Authoritative Lifecycle (called by NetworkMatchManager)
        // =====================================================================

        public void ServerInitializeBot(byte slotIndex, TeamId team, string callsign, WeaponId weapon, Vector3 spawnPos, float spawnYaw)
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
                Callsign = NetWireHelpers.ToFixed32(callsign),
                PlayerId = NetWireHelpers.ToFixed64($"BOT-{slotIndex}")
            };

            m_ServerCachedPose.ActiveWeapon = weapon;
            ServerRespawnAt(spawnPos, spawnYaw);
        }

        public void ServerRespawnAt(Vector3 spawnPos, float spawnYaw)
        {
            if (!IsServer)
            {
                return;
            }

            transform.SetPositionAndRotation(spawnPos, Quaternion.Euler(0f, spawnYaw, 0f));
            m_ServerCachedPose.Position = spawnPos;
            m_ServerCachedPose.Yaw = spawnYaw;
            m_ServerCachedPose.Pitch = 0f;
            m_ServerCachedPose.Velocity = Vector3.zero;
            m_ServerCachedPose.Stance = PlayerStanceFlags.Grounded;

            m_NetPose.Value = NetPlayerPose.FromSnapshot(m_ServerCachedPose);
            m_NetVitals.Value = new NetVitalsState
            {
                Health = DefaultMaxHealth,
                Armor = DefaultMaxArmor,
                IsAlive = true,
                RespawnEndServerTime = 0,
                LastDamageSourcePos = Vector3.zero
            };
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
                        OwnerClientIdOrBot,
                        NetworkObjectId,
                        Callsign,
                        Team,
                        weapon,
                        isHeadshot,
                        victimIsBot: true);
                }
            }
            else
            {
                m_OfflineHealth = currentHealth;
                m_OfflineArmor = currentArmor;
                m_OfflineAlive = !eliminated;
                if (eliminated)
                {
                    OnBotAliveStateChanged?.Invoke(false);
                }
            }

            return eliminated;
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void BroadcastBotShotVisualRpc(NetShotVisualPayload payload)
        {
            OnBotShotVisual?.Invoke(new WeaponShotVisualEvent
            {
                ShooterNetworkObjectId = payload.ShooterNetObjectId,
                Weapon = (WeaponId)payload.WeaponRaw,
                MuzzlePosition = payload.MuzzlePosition,
                HitPoint = payload.HitPoint,
                HitNormal = payload.HitNormal,
                Surface = (HitSurfaceType)payload.SurfaceRaw,
                DidHit = payload.DidHit,
                IsLocalShooter = false
            });
        }

        private void HandleNetPoseChanged(NetPlayerPose previous, NetPlayerPose current)
        {
            if (ShouldRunBotAI)
            {
                return;
            }
            m_RemoteTargetPos = current.Position;
            m_RemoteTargetVel = current.Velocity;
            m_RemoteTargetYaw = current.Yaw;
            m_RemoteTargetPitch = current.Pitch;
            m_HasRemotePose = true;
        }

        private void HandleVitalsChanged(NetVitalsState previous, NetVitalsState current)
        {
            if (previous.IsAlive != current.IsAlive)
            {
                OnBotAliveStateChanged?.Invoke(current.IsAlive);
            }
        }

        private void HandleIdentityChanged(NetPlayerIdentity previous, NetPlayerIdentity current)
        {
            if (previous.TeamRaw != current.TeamRaw)
            {
                OnBotTeamAssigned?.Invoke(current.Team);
            }
        }
    }
}
