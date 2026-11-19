using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace VanguardZero.Networking
{
    public struct NetPlayerPose : INetworkSerializable, IEquatable<NetPlayerPose>
    {
        public Vector3 Position;
        public float Yaw;
        public float Pitch;
        public Vector3 Velocity;
        public byte StanceRaw;
        public byte WeaponRaw;

        public PlayerStanceFlags Stance
        {
            get => (PlayerStanceFlags)StanceRaw;
            set => StanceRaw = (byte)value;
        }

        public WeaponId Weapon
        {
            get => (WeaponId)WeaponRaw;
            set => WeaponRaw = (byte)value;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Yaw);
            serializer.SerializeValue(ref Pitch);
            serializer.SerializeValue(ref Velocity);
            serializer.SerializeValue(ref StanceRaw);
            serializer.SerializeValue(ref WeaponRaw);
        }

        public PlayerPoseSnapshot ToSnapshot()
        {
            return new PlayerPoseSnapshot
            {
                Position = Position,
                Yaw = Yaw,
                Pitch = Pitch,
                Velocity = Velocity,
                Stance = (PlayerStanceFlags)StanceRaw,
                ActiveWeapon = (WeaponId)WeaponRaw
            };
        }

        public static NetPlayerPose FromSnapshot(in PlayerPoseSnapshot snapshot)
        {
            return new NetPlayerPose
            {
                Position = snapshot.Position,
                Yaw = snapshot.Yaw,
                Pitch = snapshot.Pitch,
                Velocity = snapshot.Velocity,
                StanceRaw = (byte)snapshot.Stance,
                WeaponRaw = (byte)snapshot.ActiveWeapon
            };
        }

        public bool Equals(NetPlayerPose other)
        {
            return Position.Equals(other.Position)
                && Mathf.Approximately(Yaw, other.Yaw)
                && Mathf.Approximately(Pitch, other.Pitch)
                && Velocity.Equals(other.Velocity)
                && StanceRaw == other.StanceRaw
                && WeaponRaw == other.WeaponRaw;
        }
    }

    public struct NetSpawnCommand : INetworkSerializable, IEquatable<NetSpawnCommand>
    {
        public uint TeleportNonce;
        public Vector3 Position;
        public float Yaw;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref TeleportNonce);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Yaw);
        }

        public bool Equals(NetSpawnCommand other)
        {
            return TeleportNonce == other.TeleportNonce
                && Position.Equals(other.Position)
                && Mathf.Approximately(Yaw, other.Yaw);
        }
    }

    public struct NetVitalsState : INetworkSerializable, IEquatable<NetVitalsState>
    {
        public short Health;
        public short Armor;
        public bool IsAlive;
        public uint DamageNonce;
        public double RespawnEndServerTime;
        public Vector3 LastDamageSourcePos;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Health);
            serializer.SerializeValue(ref Armor);
            serializer.SerializeValue(ref IsAlive);
            serializer.SerializeValue(ref DamageNonce);
            serializer.SerializeValue(ref RespawnEndServerTime);
            serializer.SerializeValue(ref LastDamageSourcePos);
        }

        public bool Equals(NetVitalsState other)
        {
            return Health == other.Health
                && Armor == other.Armor
                && IsAlive == other.IsAlive
                && DamageNonce == other.DamageNonce
                && Math.Abs(RespawnEndServerTime - other.RespawnEndServerTime) < 0.001;
        }
    }

    public struct NetPlayerIdentity : INetworkSerializable, IEquatable<NetPlayerIdentity>
    {
        public byte SlotIndex;
        public byte TeamRaw;
        public bool IsReady;
        public FixedString32Bytes Callsign;
        public FixedString64Bytes PlayerId;

        public TeamId Team
        {
            get => (TeamId)TeamRaw;
            set => TeamRaw = (byte)value;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref SlotIndex);
            serializer.SerializeValue(ref TeamRaw);
            serializer.SerializeValue(ref IsReady);
            serializer.SerializeValue(ref Callsign);
            serializer.SerializeValue(ref PlayerId);
        }

        public bool Equals(NetPlayerIdentity other)
        {
            return SlotIndex == other.SlotIndex
                && TeamRaw == other.TeamRaw
                && IsReady == other.IsReady
                && Callsign.Equals(other.Callsign)
                && PlayerId.Equals(other.PlayerId);
        }
    }

    public struct NetMatchState : INetworkSerializable, IEquatable<NetMatchState>
    {
        public byte PhaseRaw;
        public byte ModeRaw;
        public byte WinningTeamRaw;
        public byte ConnectionModeRaw;
        public short AlphaScore;
        public short BravoScore;
        public short ScoreLimit;
        public double PhaseEndServerTime;
        public FixedString32Bytes MapName;
        public FixedString32Bytes WinnerCallsign;
        public FixedString32Bytes JoinCode;

        public MatchPhase Phase
        {
            get => (MatchPhase)PhaseRaw;
            set => PhaseRaw = (byte)value;
        }

        public MatchMode Mode
        {
            get => (MatchMode)ModeRaw;
            set => ModeRaw = (byte)value;
        }

        public TeamId WinningTeam
        {
            get => (TeamId)WinningTeamRaw;
            set => WinningTeamRaw = (byte)value;
        }

        public ConnectionMode ConnectionMode
        {
            get => (ConnectionMode)ConnectionModeRaw;
            set => ConnectionModeRaw = (byte)value;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PhaseRaw);
            serializer.SerializeValue(ref ModeRaw);
            serializer.SerializeValue(ref WinningTeamRaw);
            serializer.SerializeValue(ref ConnectionModeRaw);
            serializer.SerializeValue(ref AlphaScore);
            serializer.SerializeValue(ref BravoScore);
            serializer.SerializeValue(ref ScoreLimit);
            serializer.SerializeValue(ref PhaseEndServerTime);
            serializer.SerializeValue(ref MapName);
            serializer.SerializeValue(ref WinnerCallsign);
            serializer.SerializeValue(ref JoinCode);
        }

        public MatchStateSnapshot ToSnapshot(double currentServerTime)
        {
            float remaining = 0f;
            if (PhaseEndServerTime > currentServerTime)
            {
                remaining = (float)(PhaseEndServerTime - currentServerTime);
            }

            return new MatchStateSnapshot
            {
                Phase = Phase,
                Mode = Mode,
                MapName = MapName.ToString(),
                AlphaScore = AlphaScore,
                BravoScore = BravoScore,
                ScoreLimit = ScoreLimit,
                RemainingSeconds = remaining,
                WinningTeam = WinningTeam,
                WinnerCallsign = WinnerCallsign.ToString(),
                JoinCode = JoinCode.ToString(),
                ConnectionMode = ConnectionMode
            };
        }

        public bool Equals(NetMatchState other)
        {
            return PhaseRaw == other.PhaseRaw
                && ModeRaw == other.ModeRaw
                && WinningTeamRaw == other.WinningTeamRaw
                && ConnectionModeRaw == other.ConnectionModeRaw
                && AlphaScore == other.AlphaScore
                && BravoScore == other.BravoScore
                && ScoreLimit == other.ScoreLimit
                && Math.Abs(PhaseEndServerTime - other.PhaseEndServerTime) < 0.01
                && MapName.Equals(other.MapName)
                && WinnerCallsign.Equals(other.WinnerCallsign)
                && JoinCode.Equals(other.JoinCode);
        }
    }

    public struct NetScoreboardEntry : INetworkSerializable, IEquatable<NetScoreboardEntry>
    {
        public ulong ClientId;
        public ulong NetworkObjectId;
        public FixedString64Bytes PlayerId;
        public FixedString32Bytes Callsign;
        public byte TeamRaw;
        public short Kills;
        public short Deaths;
        public short Assists;
        public int Score;
        public short PingMs;
        public bool IsBot;
        public bool IsAlive;
        public bool IsReady;
        public bool IsHost;
        public byte SlotIndex;

        public TeamId Team
        {
            get => (TeamId)TeamRaw;
            set => TeamRaw = (byte)value;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref NetworkObjectId);
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref Callsign);
            serializer.SerializeValue(ref TeamRaw);
            serializer.SerializeValue(ref Kills);
            serializer.SerializeValue(ref Deaths);
            serializer.SerializeValue(ref Assists);
            serializer.SerializeValue(ref Score);
            serializer.SerializeValue(ref PingMs);
            serializer.SerializeValue(ref IsBot);
            serializer.SerializeValue(ref IsAlive);
            serializer.SerializeValue(ref IsReady);
            serializer.SerializeValue(ref IsHost);
            serializer.SerializeValue(ref SlotIndex);
        }

        public PlayerCombatStats ToStats()
        {
            return new PlayerCombatStats
            {
                ClientId = ClientId,
                NetworkObjectId = NetworkObjectId,
                PlayerId = PlayerId.ToString(),
                Callsign = Callsign.ToString(),
                Team = Team,
                Kills = Kills,
                Deaths = Deaths,
                Assists = Assists,
                Score = Score,
                PingMs = PingMs,
                IsBot = IsBot,
                IsAlive = IsAlive,
                IsReady = IsReady,
                IsHost = IsHost,
                SlotIndex = SlotIndex
            };
        }

        public bool Equals(NetScoreboardEntry other)
        {
            return ClientId == other.ClientId
                && NetworkObjectId == other.NetworkObjectId
                && TeamRaw == other.TeamRaw
                && Kills == other.Kills
                && Deaths == other.Deaths
                && Assists == other.Assists
                && Score == other.Score
                && PingMs == other.PingMs
                && IsBot == other.IsBot
                && IsAlive == other.IsAlive
                && IsReady == other.IsReady
                && IsHost == other.IsHost
                && SlotIndex == other.SlotIndex
                && Callsign.Equals(other.Callsign)
                && PlayerId.Equals(other.PlayerId);
        }
    }

    public struct NetKillFeedPayload : INetworkSerializable
    {
        public uint SequenceId;
        public FixedString32Bytes KillerName;
        public byte KillerTeamRaw;
        public FixedString32Bytes VictimName;
        public byte VictimTeamRaw;
        public byte WeaponRaw;
        public bool IsHeadshot;
        public double ServerTime;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref SequenceId);
            serializer.SerializeValue(ref KillerName);
            serializer.SerializeValue(ref KillerTeamRaw);
            serializer.SerializeValue(ref VictimName);
            serializer.SerializeValue(ref VictimTeamRaw);
            serializer.SerializeValue(ref WeaponRaw);
            serializer.SerializeValue(ref IsHeadshot);
            serializer.SerializeValue(ref ServerTime);
        }

        public KillFeedEntry ToEntry()
        {
            return new KillFeedEntry
            {
                SequenceId = SequenceId,
                KillerName = KillerName.ToString(),
                KillerTeam = (TeamId)KillerTeamRaw,
                VictimName = VictimName.ToString(),
                VictimTeam = (TeamId)VictimTeamRaw,
                Weapon = (WeaponId)WeaponRaw,
                IsHeadshot = IsHeadshot,
                ServerTime = ServerTime
            };
        }
    }

    public struct NetFireShotRequest : INetworkSerializable
    {
        public byte WeaponRaw;
        public Vector3 MuzzlePosition;
        public Vector3 AimOrigin;
        public Vector3 AimDirection;
        public uint ShotSequence;
        public float ClientTimeSeconds;

        public WeaponId Weapon => (WeaponId)WeaponRaw;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref WeaponRaw);
            serializer.SerializeValue(ref MuzzlePosition);
            serializer.SerializeValue(ref AimOrigin);
            serializer.SerializeValue(ref AimDirection);
            serializer.SerializeValue(ref ShotSequence);
            serializer.SerializeValue(ref ClientTimeSeconds);
        }
    }

    public struct NetShotVisualPayload : INetworkSerializable
    {
        public ulong ShooterClientId;
        public ulong ShooterNetObjectId;
        public byte WeaponRaw;
        public Vector3 MuzzlePosition;
        public Vector3 HitPoint;
        public Vector3 HitNormal;
        public byte SurfaceRaw;
        public bool DidHit;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ShooterClientId);
            serializer.SerializeValue(ref ShooterNetObjectId);
            serializer.SerializeValue(ref WeaponRaw);
            serializer.SerializeValue(ref MuzzlePosition);
            serializer.SerializeValue(ref HitPoint);
            serializer.SerializeValue(ref HitNormal);
            serializer.SerializeValue(ref SurfaceRaw);
            serializer.SerializeValue(ref DidHit);
        }
    }

    public static class NetWireHelpers
    {
        public static FixedString32Bytes ToFixed32(string value)
        {
            FixedString32Bytes result = default;
            if (!string.IsNullOrEmpty(value))
            {
                result.CopyFromTruncated(value);
            }
            return result;
        }

        public static FixedString64Bytes ToFixed64(string value)
        {
            FixedString64Bytes result = default;
            if (!string.IsNullOrEmpty(value))
            {
                result.CopyFromTruncated(value);
            }
            return result;
        }
    }

    public struct NetHitConfirmPayload : INetworkSerializable
    {
        public bool IsHeadshot;
        public bool IsKill;
        public short DamageDealt;
        public Vector3 HitPoint;
        public FixedString32Bytes VictimCallsign;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref IsHeadshot);
            serializer.SerializeValue(ref IsKill);
            serializer.SerializeValue(ref DamageDealt);
            serializer.SerializeValue(ref HitPoint);
            serializer.SerializeValue(ref VictimCallsign);
        }
    }
}
