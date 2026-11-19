using System.Collections.Generic;
using UnityEngine;
using VanguardZero.Networking;

namespace VanguardZero.Gameplay
{
    /// <summary>
    /// Scene-level spawn & tactical waypoint provider for BLACKSITE-09.
    /// Implements IVanguardSpawnProvider and registers with VanguardNetServices.
    /// </summary>
    public class BlacksiteSpawnProvider : MonoBehaviour, IVanguardSpawnProvider
    {
        [Header("Physics Mask")]
        [SerializeField] private LayerMask worldGeometryMask = ~0;

        [Header("Team & Mode Spawn Points")]
        [SerializeField] private List<Transform> alphaSpawnPoints = new List<Transform>();
        [SerializeField] private List<Transform> bravoSpawnPoints = new List<Transform>();
        [SerializeField] private List<Transform> ffaSpawnPoints = new List<Transform>();

        [Header("AI Navigation Waypoints")]
        [SerializeField] private List<Transform> tacticalWaypoints = new List<Transform>();
        [SerializeField] private List<Transform> coverNodes = new List<Transform>();

        private int _roundRobinIndex;

        public LayerMask WorldGeometryMask => worldGeometryMask;
        public IReadOnlyList<Transform> TacticalWaypoints => tacticalWaypoints;
        public IReadOnlyList<Transform> CoverNodes => coverNodes;

        private void Awake()
        {
            // Default worldGeometryMask to Default (0) + WorldGeometry (6) if unconfigured
            int wgLayer = LayerMask.NameToLayer("WorldGeometry");
            if (wgLayer >= 0)
                worldGeometryMask = (1 << 0) | (1 << wgLayer);
            else
                worldGeometryMask = (1 << 0);

            AutoCollectChildNodesIfEmpty();
            VanguardNetServices.RegisterSpawnProvider(this);
        }

        private void OnDestroy()
        {
            VanguardNetServices.UnregisterSpawnProvider(this);
        }

        public void ConfigureNodes(
            LayerMask geometryMask,
            List<Transform> alpha,
            List<Transform> bravo,
            List<Transform> ffa,
            List<Transform> waypoints,
            List<Transform> cover)
        {
            worldGeometryMask = geometryMask;
            alphaSpawnPoints = alpha ?? new List<Transform>();
            bravoSpawnPoints = bravo ?? new List<Transform>();
            ffaSpawnPoints = ffa ?? new List<Transform>();
            tacticalWaypoints = waypoints ?? new List<Transform>();
            coverNodes = cover ?? new List<Transform>();
        }

        private void AutoCollectChildNodesIfEmpty()
        {
            if (alphaSpawnPoints.Count > 0 && bravoSpawnPoints.Count > 0) return;

            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child == transform) continue;
                string n = child.name;
                if (n.StartsWith("Spawn_Alpha")) alphaSpawnPoints.Add(child);
                else if (n.StartsWith("Spawn_Bravo")) bravoSpawnPoints.Add(child);
                else if (n.StartsWith("Spawn_FFA")) ffaSpawnPoints.Add(child);
                else if (n.StartsWith("Waypoint_")) tacticalWaypoints.Add(child);
                else if (n.StartsWith("Cover_")) coverNodes.Add(child);
            }
        }

        public IReadOnlyList<Transform> GetAllSpawnPoints(TeamId team)
        {
            return team switch
            {
                TeamId.Alpha when alphaSpawnPoints.Count > 0 => alphaSpawnPoints,
                TeamId.Bravo when bravoSpawnPoints.Count > 0 => bravoSpawnPoints,
                _ => ffaSpawnPoints.Count > 0 ? ffaSpawnPoints : alphaSpawnPoints
            };
        }

        public Transform GetBestSpawnPoint(TeamId team, IReadOnlyList<Vector3> activeEnemyPositions)
        {
            var candidates = GetAllSpawnPoints(team);
            if (candidates == null || candidates.Count == 0)
                return transform;

            if (activeEnemyPositions == null || activeEnemyPositions.Count == 0)
            {
                var pick = candidates[_roundRobinIndex % candidates.Count];
                _roundRobinIndex++;
                return pick != null ? pick : transform;
            }

            Transform best = candidates[0];
            float bestScore = float.MinValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate == null) continue;

                Vector3 pos = candidate.position;
                Vector3 eye = pos + Vector3.up * 1.55f;
                float minDist = float.MaxValue;
                int exposedSightlines = 0;

                for (int e = 0; e < activeEnemyPositions.Count; e++)
                {
                    Vector3 enemyPos = activeEnemyPositions[e];
                    float dist = Vector3.Distance(pos, enemyPos);
                    if (dist < minDist) minDist = dist;

                    Vector3 enemyEye = enemyPos + Vector3.up * 1.55f;
                    Vector3 toEnemy = enemyEye - eye;
                    float len = toEnemy.magnitude;
                    if (len > 0.5f && len < 55f)
                    {
                        if (!Physics.Raycast(eye, toEnemy / len, len, worldGeometryMask, QueryTriggerInteraction.Ignore))
                            exposedSightlines++;
                    }
                }

                // Prefer ~18-42m from nearest enemy, penalize <10m or direct exposed sightline
                float distScore = Mathf.Clamp(minDist, 0f, 45f);
                if (minDist < 9f) distScore -= (9f - minDist) * 8f;
                float sightPenalty = exposedSightlines * 14f;
                float jitter = UnityEngine.Random.Range(0f, 2.5f);

                float totalScore = distScore - sightPenalty + jitter;
                if (totalScore > bestScore)
                {
                    bestScore = totalScore;
                    best = candidate;
                }
            }

            return best != null ? best : transform;
        }
    }
}
