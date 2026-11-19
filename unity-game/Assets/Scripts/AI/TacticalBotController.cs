using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using VanguardZero.Core;
using VanguardZero.Gameplay;
using VanguardZero.Networking;
using VanguardZero.VFX;

namespace VanguardZero.AI
{
    /// <summary>
    /// Smart Tactical AI Bot driven by Unity AI Navigation (NavMeshAgent) on the authoritative Host/Server.
    /// Patrols Blacksite-09 tactical lanes, detects enemies via sightline raycasts, strafes & seeks cover,
    /// fires controlled bursts through IBotNetworkBridge, and syncs pose/shots cleanly to all clients.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class TacticalBotController : MonoBehaviour
    {
        private enum BotState { Patrol, Engage, SeekCover }

        [Header("Tactical AI Tuning")]
        [SerializeField] private float detectionRange = 46f;
        [SerializeField] private float preferredEngageRange = 19f;
        [SerializeField] private float patrolSpeed = 3.9f;
        [SerializeField] private float combatSpeed = 4.9f;
        [SerializeField] private float aimSpreadDegrees = 3.2f;
        [SerializeField] private float reactionDelay = 0.28f;

        [SerializeField] private TacticalOperatorRig operatorRig;

        private NavMeshAgent _agent;
        private IBotNetworkBridge _bridge;
        private BotState _state = BotState.Patrol;

        private Transform _currentTarget;
        private float _targetScanTimer;
        private float _strafeSwitchTimer;
        private int _strafeSign = 1;
        private float _burstTimer;
        private int _burstShotsRemaining;
        private float _nextFireTime;
        private float _acquiredTime;
        private uint _shotSeq;
        private float _currentYaw;
        private float _currentPitch;
        private bool _isCrouching;
        private int _waypointIndex;
        private bool _configured;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _bridge = GetComponent<IBotNetworkBridge>();
            if (operatorRig == null) operatorRig = GetComponentInChildren<TacticalOperatorRig>(true);
        }

        private void Start()
        {
            BindBridge();
        }

        private void OnDestroy()
        {
            if (_bridge != null)
            {
                _bridge.OnBotPoseUpdated -= HandleRemoteBotPose;
                _bridge.OnBotAliveStateChanged -= HandleAliveStateChanged;
                _bridge.OnBotTeamAssigned -= HandleTeamAssigned;
                _bridge.OnBotShotVisual -= HandleBotShotVisual;
            }
        }

        private void BindBridge()
        {
            if (_bridge == null) _bridge = GetComponent<IBotNetworkBridge>();
            if (_bridge == null) return;

            _bridge.OnBotPoseUpdated += HandleRemoteBotPose;
            _bridge.OnBotAliveStateChanged += HandleAliveStateChanged;
            _bridge.OnBotTeamAssigned += HandleTeamAssigned;
            _bridge.OnBotShotVisual += HandleBotShotVisual;

            operatorRig?.ApplyTeamVisuals(_bridge.Team);
            ConfigureAgentAuthority();
        }

        private void ConfigureAgentAuthority()
        {
            _configured = true;
            bool runAi = _bridge != null && _bridge.ShouldRunBotAI;
            if (_agent != null)
            {
                _agent.enabled = runAi;
                if (runAi)
                {
                    _agent.speed = patrolSpeed;
                    _agent.angularSpeed = 360f;
                    _agent.acceleration = 14f;
                    _agent.stoppingDistance = 1.1f;
                    _agent.updateRotation = false;
                    PickNextPatrolWaypoint();
                }
            }
        }

        private void Update()
        {
            if (_bridge == null)
            {
                BindBridge();
                return;
            }

            if (!_configured) ConfigureAgentAuthority();
            if (!_bridge.ShouldRunBotAI) return;

            if (!_bridge.IsAlive)
            {
                if (_agent.enabled && _agent.isOnNavMesh) _agent.isStopped = true;
                return;
            }

            if (_agent.enabled && !_agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit navHit, 6f, NavMesh.AllAreas))
                {
                    _agent.Warp(navHit.position);
                }
                return;
            }

            float dt = Time.deltaTime;
            _targetScanTimer -= dt;
            if (_targetScanTimer <= 0f)
            {
                _targetScanTimer = 0.25f;
                ScanForBestEnemyTarget();
            }

            switch (_state)
            {
                case BotState.Patrol:
                    TickPatrol(dt);
                    break;
                case BotState.Engage:
                    TickEngage(dt);
                    break;
                case BotState.SeekCover:
                    TickSeekCover(dt);
                    break;
            }

            PushPoseToNetwork();
        }

        private void ScanForBestEnemyTarget()
        {
            LayerMask worldMask = VanguardNetServices.SpawnProvider != null
                ? VanguardNetServices.SpawnProvider.WorldGeometryMask
                : (LayerMask)(1 << 0);

            Vector3 myEye = transform.position + Vector3.up * 1.55f;
            Transform best = null;
            float bestDist = detectionRange;

            // Check human players
            var players = FindObjectsByType<TacticalFpsController>(FindObjectsSortMode.None);
            foreach (var p in players)
            {
                if (p == null || p.Authority == null || !p.Authority.IsAlive) continue;
                if (!IsHostile(_bridge.Team, p.Authority.Team)) continue;
                CheckCandidate(p.transform, myEye, worldMask, ref best, ref bestDist);
            }

            // Check other bots
            var bots = FindObjectsByType<TacticalBotController>(FindObjectsSortMode.None);
            foreach (var b in bots)
            {
                if (b == null || b == this || b._bridge == null || !b._bridge.IsAlive) continue;
                if (!IsHostile(_bridge.Team, b._bridge.Team)) continue;
                CheckCandidate(b.transform, myEye, worldMask, ref best, ref bestDist);
            }

            if (best != null)
            {
                if (_currentTarget != best) _acquiredTime = Time.time;
                _currentTarget = best;
                _state = _bridge.Health < 40 ? BotState.SeekCover : BotState.Engage;
            }
            else if (_state == BotState.Engage)
            {
                _currentTarget = null;
                _state = BotState.Patrol;
                PickNextPatrolWaypoint();
            }
        }

        private void CheckCandidate(Transform candidate, Vector3 myEye, LayerMask worldMask, ref Transform best, ref float bestDist)
        {
            Vector3 targetEye = candidate.position + Vector3.up * 1.42f;
            Vector3 delta = targetEye - myEye;
            float dist = delta.magnitude;
            if (dist >= bestDist || dist < 0.5f) return;

            if (!Physics.Raycast(myEye, delta / dist, dist - 0.35f, worldMask, QueryTriggerInteraction.Ignore))
            {
                bestDist = dist;
                best = candidate;
            }
        }

        private static bool IsHostile(TeamId myTeam, TeamId otherTeam)
        {
            if (myTeam == TeamId.None || otherTeam == TeamId.None) return true;
            return myTeam != otherTeam;
        }

        private void TickPatrol(float dt)
        {
            _isCrouching = false;
            _agent.isStopped = false;
            _agent.speed = patrolSpeed;

            if (!_agent.pathPending && _agent.remainingDistance <= 1.6f)
            {
                PickNextPatrolWaypoint();
            }

            Vector3 vel = _agent.velocity;
            if (vel.sqrMagnitude > 0.1f)
            {
                float desiredYaw = Mathf.Atan2(vel.x, vel.z) * Mathf.Rad2Deg;
                _currentYaw = Mathf.MoveTowardsAngle(_currentYaw, desiredYaw, 320f * dt);
                _currentPitch = Mathf.MoveTowards(_currentPitch, 0f, 90f * dt);
            }
            transform.rotation = Quaternion.Euler(0f, _currentYaw, 0f);
        }

        private void TickEngage(float dt)
        {
            if (_currentTarget == null)
            {
                _state = BotState.Patrol;
                return;
            }

            Vector3 myEye = transform.position + Vector3.up * 1.52f;
            Vector3 targetCenter = _currentTarget.position + Vector3.up * 1.32f;
            Vector3 toTarget = targetCenter - myEye;
            float dist = toTarget.magnitude;

            // Smoothly aim toward enemy
            float desiredYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
            float desiredPitch = -Mathf.Asin(Mathf.Clamp(toTarget.y / Mathf.Max(0.1f, dist), -0.95f, 0.95f)) * Mathf.Rad2Deg;
            _currentYaw = Mathf.MoveTowardsAngle(_currentYaw, desiredYaw, 440f * dt);
            _currentPitch = Mathf.MoveTowards(_currentPitch, desiredPitch, 220f * dt);
            transform.rotation = Quaternion.Euler(0f, _currentYaw, 0f);

            // Tactical strafe + range maintenance
            _strafeSwitchTimer -= dt;
            if (_strafeSwitchTimer <= 0f)
            {
                _strafeSwitchTimer = UnityEngine.Random.Range(1.1f, 2.4f);
                _strafeSign = UnityEngine.Random.value > 0.5f ? 1 : -1;
                _isCrouching = dist > 14f && UnityEngine.Random.value > 0.65f;
            }

            _agent.isStopped = false;
            _agent.speed = _isCrouching ? 2.6f : combatSpeed;

            Vector3 right = Vector3.Cross(Vector3.up, toTarget.normalized);
            Vector3 moveGoal = transform.position + right * (_strafeSign * 4.5f);
            if (dist > preferredEngageRange + 5f) moveGoal += toTarget.normalized * 4.5f;
            else if (dist < 7f) moveGoal -= toTarget.normalized * 3.5f;

            if (NavMesh.SamplePosition(moveGoal, out NavMeshHit hit, 3.5f, NavMesh.AllAreas))
            {
                _agent.SetDestination(hit.position);
            }

            // Controlled burst fire
            if (Time.time - _acquiredTime < reactionDelay) return;
            if (Mathf.Abs(Mathf.DeltaAngle(_currentYaw, desiredYaw)) > 16f) return;

            if (_burstShotsRemaining <= 0)
            {
                _burstTimer -= dt;
                if (_burstTimer <= 0f)
                {
                    _burstShotsRemaining = UnityEngine.Random.Range(3, 6);
                    _burstTimer = UnityEngine.Random.Range(0.45f, 0.95f);
                }
            }
            else if (Time.time >= _nextFireTime)
            {
                var spec = VanguardNetServices.GetWeaponSpec(_bridge.EquippedWeapon);
                _nextFireTime = Time.time + Mathf.Max(spec.FireIntervalSeconds * 1.45f, 0.14f);
                _burstShotsRemaining--;
                _shotSeq++;

                Vector3 aimDir = ApplyBotSpread(toTarget.normalized, aimSpreadDegrees);
                Vector3 muzzle = operatorRig != null && operatorRig.MuzzleTransform != null
                    ? operatorRig.MuzzleTransform.position
                    : (myEye + transform.forward * 0.55f);

                var fireInput = new WeaponFireInput
                {
                    Weapon = _bridge.EquippedWeapon,
                    MuzzlePosition = muzzle,
                    AimOrigin = myEye,
                    AimDirection = aimDir,
                    ShotSequence = _shotSeq,
                    ClientTimeSeconds = Time.time
                };
                _bridge.FireBotWeapon(in fireInput);
            }
        }

        private void TickSeekCover(float dt)
        {
            _isCrouching = true;
            _agent.speed = combatSpeed;
            if (!_agent.pathPending && _agent.remainingDistance <= 1.4f)
            {
                _state = _currentTarget != null ? BotState.Engage : BotState.Patrol;
                return;
            }

            var spawnProvider = VanguardNetServices.SpawnProvider as BlacksiteSpawnProvider;
            if (spawnProvider != null && spawnProvider.CoverNodes.Count > 0 && !_agent.hasPath)
            {
                Transform cover = spawnProvider.CoverNodes[UnityEngine.Random.Range(0, spawnProvider.CoverNodes.Count)];
                if (cover != null) _agent.SetDestination(cover.position);
            }

            Vector3 vel = _agent.velocity;
            if (vel.sqrMagnitude > 0.1f)
            {
                _currentYaw = Mathf.MoveTowardsAngle(_currentYaw, Mathf.Atan2(vel.x, vel.z) * Mathf.Rad2Deg, 360f * dt);
                transform.rotation = Quaternion.Euler(0f, _currentYaw, 0f);
            }
        }

        private void PickNextPatrolWaypoint()
        {
            var spawnProvider = VanguardNetServices.SpawnProvider as BlacksiteSpawnProvider;
            if (spawnProvider != null && spawnProvider.TacticalWaypoints.Count > 0)
            {
                _waypointIndex = (_waypointIndex + UnityEngine.Random.Range(1, spawnProvider.TacticalWaypoints.Count)) % spawnProvider.TacticalWaypoints.Count;
                var wp = spawnProvider.TacticalWaypoints[_waypointIndex];
                if (wp != null && _agent.isOnNavMesh)
                {
                    _agent.SetDestination(wp.position);
                    return;
                }
            }

            // Fallback random NavMesh point
            Vector3 randomDir = transform.position + new Vector3(UnityEngine.Random.Range(-18f, 18f), 0f, UnityEngine.Random.Range(-18f, 18f));
            if (NavMesh.SamplePosition(randomDir, out NavMeshHit hit, 12f, NavMesh.AllAreas) && _agent.isOnNavMesh)
            {
                _agent.SetDestination(hit.position);
            }
        }

        private void PushPoseToNetwork()
        {
            PlayerStanceFlags flags = PlayerStanceFlags.Grounded;
            if (_isCrouching) flags |= PlayerStanceFlags.Crouching;
            if (_state == BotState.Engage) flags |= PlayerStanceFlags.AimingDownSights;

            var pose = new PlayerPoseSnapshot
            {
                Position = transform.position,
                Yaw = _currentYaw,
                Pitch = _currentPitch,
                Velocity = _agent != null ? _agent.velocity : Vector3.zero,
                Stance = flags,
                ActiveWeapon = _bridge.EquippedWeapon
            };

            _bridge.PushBotPose(in pose);
            operatorRig?.ApplyPoseSnapshot(in pose);
        }

        private static Vector3 ApplyBotSpread(Vector3 forward, float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            Vector2 disk = UnityEngine.Random.insideUnitCircle * Mathf.Tan(rad);
            Quaternion basis = Quaternion.LookRotation(forward);
            return (forward + basis * new Vector3(disk.x, disk.y, 0f)).normalized;
        }

        private void HandleRemoteBotPose(PlayerPoseSnapshot pose)
        {
            if (_bridge != null && _bridge.ShouldRunBotAI) return;
            operatorRig?.ApplyPoseSnapshot(in pose);
        }

        private void HandleAliveStateChanged(bool alive)
        {
            operatorRig?.SetAliveState(alive);
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.isStopped = !alive;
                if (alive) PickNextPatrolWaypoint();
            }
        }

        private void HandleTeamAssigned(TeamId team)
        {
            operatorRig?.ApplyTeamVisuals(team);
        }

        private void HandleBotShotVisual(WeaponShotVisualEvent ev)
        {
            string prefix = ev.Weapon switch
            {
                WeaponId.M890_TacticalShotgun => "m890_fire",
                WeaponId.M19_TacticalPistol => "m19_fire",
                _ => "vx47_fire"
            };
            VanguardAudioManager.Instance?.PlayRandom3D(prefix, 2, ev.MuzzlePosition, AudioBus.Weapons, 0.78f, 0.05f, 80f);
            VanguardVfxManager.Instance?.SpawnMuzzleBurst(ev.MuzzlePosition, (ev.HitPoint - ev.MuzzlePosition).normalized);
            VanguardVfxManager.Instance?.SpawnTracer(ev.MuzzlePosition, ev.HitPoint, ev.Weapon == WeaponId.M890_TacticalShotgun);
            if (ev.DidHit)
            {
                VanguardVfxManager.Instance?.SpawnImpact(ev.HitPoint, ev.HitNormal, (VanguardSurfaceTag.SurfaceKind)ev.Surface);
            }
        }
    }
}
