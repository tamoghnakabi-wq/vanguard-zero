using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace VanguardZero.Networking
{
    /// <summary>
    /// Server/Host-authoritative position-history lag compensation & hitscan validator.
    /// Maintains a 60 Hz fixed-size ring buffer (64 snapshots = ~1.06s) per combatant (players + bots)
    /// and rewinds target hitboxes up to MaxRewindSeconds (250ms) based on the shooter's measured RTT.
    /// </summary>
    public class LagCompensatedHitValidator : MonoBehaviour
    {
        public const int HistoryCapacity = 64;
        public const float MaxRewindSeconds = 0.25f;
        public const float InterpolationBufferSeconds = 0.045f;

        // TacticalOperator proportions (1.80m standing, HeadEyePoint ~1.66m)
        public const float HeadRadius = 0.21f;
        public const float StandingHeadCenterY = 1.62f;
        public const float CrouchingHeadCenterY = 1.12f;

        public const float BodyCapsuleRadius = 0.36f;
        public const float StandingBodyBottomY = 0.36f;
        public const float StandingBodyTopY = 1.42f;
        public const float CrouchingBodyBottomY = 0.36f;
        public const float CrouchingBodyTopY = 0.94f;

        public static LagCompensatedHitValidator Instance { get; private set; }

        private struct PoseHistorySample
        {
            public double ServerTime;
            public Vector3 RootPosition;
            public float Yaw;
            public bool IsCrouching;
            public bool IsAlive;
        }

        private sealed class TargetHistoryBuffer
        {
            public ILagCompensatedTarget Target;
            public readonly PoseHistorySample[] Samples = new PoseHistorySample[HistoryCapacity];
            public int WriteIndex;
            public int Count;
            public double LastFireServerTime = -10.0;

            public void Record(double serverTime)
            {
                if (Target == null)
                {
                    return;
                }

                Samples[WriteIndex] = new PoseHistorySample
                {
                    ServerTime = serverTime,
                    RootPosition = Target.CurrentRootPosition,
                    Yaw = Target.CurrentYaw,
                    IsCrouching = Target.IsCrouching,
                    IsAlive = Target.IsAlive
                };

                WriteIndex = (WriteIndex + 1) % HistoryCapacity;
                if (Count < HistoryCapacity)
                {
                    Count++;
                }
            }

            public bool TrySampleAtTime(double targetTime, out Vector3 rootPos, out bool isCrouching)
            {
                if (Target == null || !Target.IsAlive)
                {
                    rootPos = Vector3.zero;
                    isCrouching = false;
                    return false;
                }

                if (Count == 0)
                {
                    rootPos = Target.CurrentRootPosition;
                    isCrouching = Target.IsCrouching;
                    return true;
                }

                int newestIdx = (WriteIndex - 1 + HistoryCapacity) % HistoryCapacity;
                int oldestIdx = (WriteIndex - Count + HistoryCapacity) % HistoryCapacity;

                PoseHistorySample newest = Samples[newestIdx];
                if (targetTime >= newest.ServerTime || Count == 1)
                {
                    rootPos = newest.RootPosition;
                    isCrouching = newest.IsCrouching;
                    return newest.IsAlive;
                }

                PoseHistorySample oldest = Samples[oldestIdx];
                if (targetTime <= oldest.ServerTime)
                {
                    rootPos = oldest.RootPosition;
                    isCrouching = oldest.IsCrouching;
                    return oldest.IsAlive;
                }

                // Walk backward from newest to oldest to bracket targetTime
                PoseHistorySample after = newest;
                for (int i = 1; i < Count; i++)
                {
                    int idx = (newestIdx - i + HistoryCapacity) % HistoryCapacity;
                    PoseHistorySample before = Samples[idx];
                    if (before.ServerTime <= targetTime)
                    {
                        double span = after.ServerTime - before.ServerTime;
                        float t = span > 0.0001 ? Mathf.Clamp01((float)((targetTime - before.ServerTime) / span)) : 1f;
                        rootPos = Vector3.Lerp(before.RootPosition, after.RootPosition, t);
                        isCrouching = t < 0.5f ? before.IsCrouching : after.IsCrouching;
                        return before.IsAlive && after.IsAlive;
                    }
                    after = before;
                }

                rootPos = newest.RootPosition;
                isCrouching = newest.IsCrouching;
                return newest.IsAlive;
            }
        }

        public struct ShotResolution
        {
            public bool Validated;
            public bool DidHitWorldOrTarget;
            public bool DidHitCombatant;
            public bool WasHeadshot;
            public bool WasKill;
            public int TotalDamageDealt;
            public Vector3 ImpactPoint;
            public Vector3 ImpactNormal;
            public HitSurfaceType Surface;
            public string VictimCallsign;
            public ulong VictimNetObjectId;
            public float RewindSecondsApplied;
        }

        private readonly Dictionary<ulong, TargetHistoryBuffer> m_Targets = new Dictionary<ulong, TargetHistoryBuffer>(16);
        private readonly List<TargetHistoryBuffer> m_TargetList = new List<TargetHistoryBuffer>(16);
        private readonly RaycastHit[] m_WorldRaycastBuffer = new RaycastHit[16];

        public IReadOnlyList<ILagCompensatedTarget> GetActiveTargetsSnapshot(List<ILagCompensatedTarget> buffer)
        {
            buffer.Clear();
            for (int i = 0; i < m_TargetList.Count; i++)
            {
                var t = m_TargetList[i].Target;
                if (t != null)
                {
                    buffer.Add(t);
                }
            }
            return buffer;
        }

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
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void FixedUpdate()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening && !nm.IsServer)
            {
                return;
            }

            double serverTime = GetAuthoritativeTime();
            for (int i = 0; i < m_TargetList.Count; i++)
            {
                m_TargetList[i].Record(serverTime);
            }
        }

        public static void RegisterTarget(ILagCompensatedTarget target)
        {
            if (target == null)
            {
                return;
            }
            EnsureInstance();
            Instance.RegisterTargetInternal(target);
        }

        public static void UnregisterTarget(ILagCompensatedTarget target)
        {
            if (target == null || Instance == null)
            {
                return;
            }
            Instance.UnregisterTargetInternal(target.TargetNetworkObjectId);
        }

        private static void EnsureInstance()
        {
            if (Instance != null)
            {
                return;
            }
            var existing = FindFirstObjectByType<LagCompensatedHitValidator>();
            if (existing != null)
            {
                Instance = existing;
                return;
            }
            var go = new GameObject("LagCompensatedHitValidator");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<LagCompensatedHitValidator>();
        }

        private void RegisterTargetInternal(ILagCompensatedTarget target)
        {
            ulong id = target.TargetNetworkObjectId;
            if (m_Targets.TryGetValue(id, out var existing))
            {
                existing.Target = target;
                return;
            }

            var buffer = new TargetHistoryBuffer { Target = target };
            buffer.Record(GetAuthoritativeTime());
            m_Targets[id] = buffer;
            m_TargetList.Add(buffer);
        }

        private void UnregisterTargetInternal(ulong id)
        {
            if (m_Targets.TryGetValue(id, out var buffer))
            {
                m_Targets.Remove(id);
                m_TargetList.Remove(buffer);
            }
        }

        public double GetAuthoritativeTime()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
            {
                return nm.ServerTime.Time;
            }
            return Time.timeAsDouble;
        }

        public float GetClientRttSeconds(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening || clientId == nm.LocalClientId)
            {
                return 0f;
            }

            var transport = nm.NetworkConfig?.NetworkTransport;
            if (transport == null)
            {
                return 0f;
            }

            ulong rttMs = transport.GetCurrentRtt(clientId);
            return Mathf.Clamp(rttMs * 0.001f, 0f, 1.0f);
        }

        /// <summary>
        /// Server/Host-authoritative hit validation with RTT-bounded position-history rewind.
        /// </summary>
        public ShotResolution ValidateAndResolveShot(
            ILagCompensatedTarget shooter,
            in NetFireShotRequest request,
            MatchMode matchMode)
        {
            var result = new ShotResolution();
            if (shooter == null || !shooter.IsAlive)
            {
                return result;
            }

            var match = VanguardNetServices.Match;
            if (match != null && match.CurrentState.Phase != MatchPhase.InProgress)
            {
                return result;
            }

            WeaponBallisticsSpec spec = WeaponBallisticsRegistry.Get(request.Weapon);
            double now = GetAuthoritativeTime();

            if (m_Targets.TryGetValue(shooter.TargetNetworkObjectId, out var shooterHistory))
            {
                if (now - shooterHistory.LastFireServerTime < spec.FireIntervalSeconds * 0.80f)
                {
                    return result;
                }
                shooterHistory.LastFireServerTime = now;
            }

            // Origin sanity check: clamp shot origin near shooter's current eye position
            float eyeOffsetY = shooter.IsCrouching ? CrouchingHeadCenterY : StandingHeadCenterY;
            Vector3 shooterEye = shooter.CurrentRootPosition + new Vector3(0f, eyeOffsetY, 0f);
            Vector3 origin = request.AimOrigin;
            if (Vector3.Distance(origin, shooterEye) > 2.25f)
            {
                origin = shooterEye;
            }

            Vector3 baseDir = request.AimDirection.sqrMagnitude > 0.0001f
                ? request.AimDirection.normalized
                : Quaternion.Euler(0f, shooter.CurrentYaw, 0f) * Vector3.forward;

            // Compute rewind timestamp for remote clients
            float rewindSeconds = 0f;
            if (!shooter.IsBotTarget)
            {
                float rtt = GetClientRttSeconds(shooter.OwnerClientIdOrBot);
                if (rtt > 0.001f)
                {
                    rewindSeconds = Mathf.Clamp(rtt * 0.5f + InterpolationBufferSeconds, 0f, MaxRewindSeconds);
                }
            }
            double evalTime = now - rewindSeconds;

            LayerMask worldMask = GetWorldOcclusionMask();
            int pelletCount = Mathf.Max(1, spec.PelletsPerShot);

            result.Validated = true;
            result.RewindSecondsApplied = rewindSeconds;
            result.ImpactPoint = origin + baseDir * spec.MaxRange;
            result.ImpactNormal = -baseDir;
            result.Surface = HitSurfaceType.Concrete;

            ILagCompensatedTarget primaryVictim = null;
            int accumulatedDamage = 0;
            bool anyHeadshot = false;
            Vector3 bestCombatHitPoint = result.ImpactPoint;
            Vector3 bestCombatHitNormal = result.ImpactNormal;

            for (int pellet = 0; pellet < pelletCount; pellet++)
            {
                Vector3 rayDir = pelletCount > 1
                    ? ApplyDeterministicSpread(baseDir, spec.SpreadDegrees, request.ShotSequence, shooter.TargetNetworkObjectId, pellet)
                    : baseDir;

                float maxDist = spec.MaxRange;
                bool hitWorld = false;
                Vector3 worldHitPt = origin + rayDir * maxDist;
                Vector3 worldHitNormal = -rayDir;
                HitSurfaceType worldSurface = HitSurfaceType.Concrete;

                int hitCount = Physics.RaycastNonAlloc(origin, rayDir, m_WorldRaycastBuffer, maxDist, worldMask, QueryTriggerInteraction.Ignore);
                float closestWorldDist = maxDist;
                for (int h = 0; h < hitCount; h++)
                {
                    RaycastHit worldHit = m_WorldRaycastBuffer[h];
                    if (worldHit.collider == null || worldHit.distance >= closestWorldDist)
                    {
                        continue;
                    }

                    // Ignore self and combatant colliders (validated via rewound hitboxes below)
                    if (worldHit.collider.GetComponentInParent<ILagCompensatedTarget>() != null)
                    {
                        continue;
                    }

                    hitWorld = true;
                    closestWorldDist = worldHit.distance;
                    worldHitPt = worldHit.point;
                    worldHitNormal = worldHit.normal;
                    worldSurface = ClassifyWorldSurface(worldHit.collider);
                }
                maxDist = closestWorldDist;

                // Evaluate rewound hitboxes of all active enemy targets closer than maxDist
                ILagCompensatedTarget hitVictim = null;
                bool hitHead = false;
                float closestTargetDist = maxDist;
                Vector3 targetHitPoint = worldHitPt;
                Vector3 targetHitNormal = worldHitNormal;

                for (int i = 0; i < m_TargetList.Count; i++)
                {
                    var candidateBuf = m_TargetList[i];
                    var candidate = candidateBuf.Target;
                    if (candidate == null || !candidate.IsAlive || candidate.TargetNetworkObjectId == shooter.TargetNetworkObjectId)
                    {
                        continue;
                    }

                    // Friendly fire filter in TeamDeathmatch
                    if (matchMode == MatchMode.TeamDeathmatch
                        && shooter.Team != TeamId.None
                        && candidate.Team == shooter.Team)
                    {
                        continue;
                    }

                    if (!candidateBuf.TrySampleAtTime(evalTime, out Vector3 rewoundRoot, out bool rewoundCrouch))
                    {
                        continue;
                    }

                    float headY = rewoundCrouch ? CrouchingHeadCenterY : StandingHeadCenterY;
                    Vector3 headCenter = rewoundRoot + new Vector3(0f, headY, 0f);

                    // 1. Test Head Sphere (highest priority)
                    if (RayIntersectsSphere(origin, rayDir, headCenter, HeadRadius, closestTargetDist, out float headDist, out Vector3 headPt, out Vector3 headNrm))
                    {
                        closestTargetDist = headDist;
                        hitVictim = candidate;
                        hitHead = true;
                        targetHitPoint = headPt;
                        targetHitNormal = headNrm;
                        continue;
                    }

                    // 2. Test Body Capsule
                    Vector3 capBottom = rewoundRoot + new Vector3(0f, rewoundCrouch ? CrouchingBodyBottomY : StandingBodyBottomY, 0f);
                    Vector3 capTop = rewoundRoot + new Vector3(0f, rewoundCrouch ? CrouchingBodyTopY : StandingBodyTopY, 0f);
                    if (RayIntersectsCapsule(origin, rayDir, capBottom, capTop, BodyCapsuleRadius, closestTargetDist, out float bodyDist, out Vector3 bodyPt, out Vector3 bodyNrm))
                    {
                        closestTargetDist = bodyDist;
                        hitVictim = candidate;
                        hitHead = false;
                        targetHitPoint = bodyPt;
                        targetHitNormal = bodyNrm;
                    }
                }

                if (hitVictim != null)
                {
                    int pelletDamage = ComputeDamageWithFalloff(spec, closestTargetDist, hitHead);
                    if (primaryVictim == null || hitVictim == primaryVictim)
                    {
                        primaryVictim = hitVictim;
                        accumulatedDamage += pelletDamage;
                        if (hitHead)
                        {
                            anyHeadshot = true;
                        }
                        bestCombatHitPoint = targetHitPoint;
                        bestCombatHitNormal = targetHitNormal;
                    }
                    else
                    {
                        // Secondary target caught in shotgun spread
                        hitVictim.ApplyAuthoritativeDamage(
                            pelletDamage,
                            shooter.OwnerClientIdOrBot,
                            shooter.TargetNetworkObjectId,
                            shooter.Callsign,
                            shooter.Team,
                            request.Weapon,
                            hitHead,
                            origin,
                            out _);
                    }
                }
                else if (pellet == 0 && hitWorld)
                {
                    result.DidHitWorldOrTarget = true;
                    result.ImpactPoint = worldHitPt;
                    result.ImpactNormal = worldHitNormal;
                    result.Surface = worldSurface;
                }
            }

            if (primaryVictim != null && accumulatedDamage > 0)
            {
                bool hadArmor = false;
                if (primaryVictim is NetworkPlayerBridge npb && npb.Armor > 0)
                {
                    hadArmor = true;
                }
                else if (primaryVictim is NetworkBotSync nbs && nbs.Armor > 0)
                {
                    hadArmor = true;
                }

                bool killed = primaryVictim.ApplyAuthoritativeDamage(
                    accumulatedDamage,
                    shooter.OwnerClientIdOrBot,
                    shooter.TargetNetworkObjectId,
                    shooter.Callsign,
                    shooter.Team,
                    request.Weapon,
                    anyHeadshot,
                    origin,
                    out int actualDealt);

                result.DidHitWorldOrTarget = true;
                result.DidHitCombatant = true;
                result.WasHeadshot = anyHeadshot;
                result.WasKill = killed;
                result.TotalDamageDealt = actualDealt;
                result.ImpactPoint = bestCombatHitPoint;
                result.ImpactNormal = bestCombatHitNormal;
                result.Surface = (!anyHeadshot && hadArmor) ? HitSurfaceType.Armor : HitSurfaceType.Flesh;
                result.VictimCallsign = primaryVictim.Callsign;
                result.VictimNetObjectId = primaryVictim.TargetNetworkObjectId;
            }

            return result;
        }

        private static int ComputeDamageWithFalloff(in WeaponBallisticsSpec spec, float distance, bool isHeadshot)
        {
            float mult = 1f;
            if (distance > spec.FalloffStartRange && spec.MaxRange > spec.FalloffStartRange)
            {
                float t = Mathf.Clamp01((distance - spec.FalloffStartRange) / (spec.MaxRange - spec.FalloffStartRange));
                mult = Mathf.Lerp(1f, spec.MinFalloffMultiplier, t);
            }

            if (isHeadshot)
            {
                mult *= spec.HeadshotMultiplier;
            }

            return Mathf.Max(1, Mathf.RoundToInt(spec.BaseDamage * mult));
        }

        private static LayerMask GetWorldOcclusionMask()
        {
            var sp = VanguardNetServices.SpawnProvider;
            if (sp != null && sp.WorldGeometryMask.value != 0)
            {
                return sp.WorldGeometryMask;
            }
            return Physics.DefaultRaycastLayers;
        }

        private static HitSurfaceType ClassifyWorldSurface(Collider col)
        {
            if (col == null)
            {
                return HitSurfaceType.Concrete;
            }
            string n = col.gameObject.name;
            if (n.IndexOf("Metal", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Container", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Catwalk", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Radar", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return HitSurfaceType.Metal;
            }
            return HitSurfaceType.Concrete;
        }

        private static Vector3 ApplyDeterministicSpread(Vector3 forward, float spreadDegrees, uint shotSeq, ulong shooterId, int pelletIndex)
        {
            uint seed = (uint)(shotSeq * 747796405u + (uint)shooterId * 2891336453u + (uint)(pelletIndex + 1) * 1597334677u);
            float u1 = ((seed & 0xFFFFu) / 65535f);
            float u2 = (((seed >> 16) & 0xFFFFu) / 65535f);

            float angleRad = u1 * Mathf.PI * 2f;
            float radiusTan = Mathf.Tan(spreadDegrees * Mathf.Deg2Rad) * Mathf.Sqrt(u2);

            Vector3 up = Mathf.Abs(forward.y) < 0.98f ? Vector3.up : Vector3.right;
            Vector3 right = Vector3.Cross(up, forward).normalized;
            Vector3 orthoUp = Vector3.Cross(forward, right).normalized;

            Vector3 spreadOffset = (right * Mathf.Cos(angleRad) + orthoUp * Mathf.Sin(angleRad)) * radiusTan;
            return (forward + spreadOffset).normalized;
        }

        public static bool RayIntersectsSphere(
            Vector3 rayOrigin,
            Vector3 rayDir,
            Vector3 sphereCenter,
            float radius,
            float maxDist,
            out float hitDist,
            out Vector3 hitPoint,
            out Vector3 hitNormal)
        {
            hitDist = 0f;
            hitPoint = Vector3.zero;
            hitNormal = Vector3.up;

            Vector3 m = rayOrigin - sphereCenter;
            float b = Vector3.Dot(m, rayDir);
            float c = Vector3.Dot(m, m) - radius * radius;

            if (c > 0f && b > 0f)
            {
                return false;
            }

            float discr = b * b - c;
            if (discr < 0f)
            {
                return false;
            }

            float t = -b - Mathf.Sqrt(discr);
            if (t < 0f)
            {
                t = 0f;
            }

            if (t > maxDist)
            {
                return false;
            }

            hitDist = t;
            hitPoint = rayOrigin + rayDir * t;
            hitNormal = (hitPoint - sphereCenter).normalized;
            return true;
        }

        public static bool RayIntersectsCapsule(
            Vector3 rayOrigin,
            Vector3 rayDir,
            Vector3 capA,
            Vector3 capB,
            float radius,
            float maxDist,
            out float hitDist,
            out Vector3 hitPoint,
            out Vector3 hitNormal)
        {
            hitDist = maxDist;
            hitPoint = Vector3.zero;
            hitNormal = Vector3.up;
            bool found = false;

            // 1. Exact ray vs finite cylinder along [capA, capB]
            Vector3 ab = capB - capA;
            float abLenSq = Vector3.Dot(ab, ab);
            if (abLenSq > 0.00001f)
            {
                Vector3 ao = rayOrigin - capA;
                float dDotAb = Vector3.Dot(rayDir, ab);
                float aoDotAb = Vector3.Dot(ao, ab);

                Vector3 dPerp = rayDir - ab * (dDotAb / abLenSq);
                Vector3 aoPerp = ao - ab * (aoDotAb / abLenSq);

                float a = Vector3.Dot(dPerp, dPerp);
                float b = Vector3.Dot(dPerp, aoPerp);
                float c = Vector3.Dot(aoPerp, aoPerp) - radius * radius;

                if (a > 0.000001f)
                {
                    float discr = b * b - a * c;
                    if (discr >= 0f)
                    {
                        float t = (-b - Mathf.Sqrt(discr)) / a;
                        if (t < 0f && c <= 0f)
                        {
                            t = 0f;
                        }

                        if (t >= 0f && t <= hitDist)
                        {
                            float s = (aoDotAb + t * dDotAb) / abLenSq;
                            if (s >= 0f && s <= 1f)
                            {
                                found = true;
                                hitDist = t;
                                hitPoint = rayOrigin + rayDir * t;
                                Vector3 axisPt = capA + ab * s;
                                Vector3 nrm = hitPoint - axisPt;
                                hitNormal = nrm.sqrMagnitude > 0.000001f ? nrm.normalized : -rayDir;
                            }
                        }
                    }
                }
            }

            // 2. Exact ray vs hemispherical endcaps at capA and capB
            if (RayIntersectsSphere(rayOrigin, rayDir, capA, radius, hitDist, out float dA, out Vector3 pA, out Vector3 nA))
            {
                found = true;
                hitDist = dA;
                hitPoint = pA;
                hitNormal = nA;
            }

            if (RayIntersectsSphere(rayOrigin, rayDir, capB, radius, hitDist, out float dB, out Vector3 pB, out Vector3 nB))
            {
                found = true;
                hitDist = dB;
                hitPoint = pB;
                hitNormal = nB;
            }

            return found;
        }
    }
}
