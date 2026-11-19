using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VanguardZero.Networking
{
    /// <summary>
    /// Persistent DontDestroyOnLoad root bootstrap that ensures NetworkManager, UnityTransport,
    /// VanguardSessionManager, and LagCompensatedHitValidator are initialized, enforces
    /// Application.runInBackground = true, handles graceful host-disconnect recovery,
    /// and supports direct-in-Editor gameplay scene launching via auto-Solo Skirmish fallback.
    /// </summary>
    [DefaultExecutionOrder(-250)]
    public class VanguardNetworkBootstrap : MonoBehaviour
    {
        public static VanguardNetworkBootstrap Instance { get; private set; }

        [Header("Network Configuration")]
        [SerializeField] private uint m_TickRate = 60;
        [SerializeField] private bool m_AutoStartSoloWhenLaunchingGameplaySceneDirectly = true;

        [Header("Registered Network Prefabs (Optional Auto-Register)")]
        [SerializeField] private GameObject m_PlayerNetworkPrefab;
        [SerializeField] private GameObject m_BotNetworkPrefab;
        [SerializeField] private GameObject m_MatchManagerNetworkPrefab;

        private NetworkManager m_NetworkManager;
        private bool m_CallbacksHooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticInstance()
        {
            Instance = null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Mandatory for multiplayer & MPPM virtual player clones so unfocused processes never stall
            Application.runInBackground = true;

            EnsureCoreComponents();
            ConfigureNetworkManager();
            SceneManager.sceneLoaded += OnUnitySceneLoaded;
        }

        private void Start()
        {
            HookNetworkCallbacks();
            CheckDirectGameplaySceneAutoHost(SceneManager.GetActiveScene());
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnUnitySceneLoaded;
            UnhookNetworkCallbacks();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void EnsureCoreComponents()
        {
            m_NetworkManager = GetComponent<NetworkManager>();
            if (m_NetworkManager == null)
            {
                m_NetworkManager = FindFirstObjectByType<NetworkManager>();
            }
            if (m_NetworkManager == null)
            {
                m_NetworkManager = gameObject.AddComponent<NetworkManager>();
            }

            var transport = m_NetworkManager.GetComponent<UnityTransport>();
            if (transport == null)
            {
                transport = m_NetworkManager.gameObject.AddComponent<UnityTransport>();
            }

            if (m_NetworkManager.NetworkConfig == null)
            {
                m_NetworkManager.NetworkConfig = new NetworkConfig();
            }
            m_NetworkManager.NetworkConfig.NetworkTransport = transport;

            if (GetComponent<VanguardSessionManager>() == null && VanguardSessionManager.Instance == null)
            {
                gameObject.AddComponent<VanguardSessionManager>();
            }

            if (GetComponent<LagCompensatedHitValidator>() == null && LagCompensatedHitValidator.Instance == null)
            {
                gameObject.AddComponent<LagCompensatedHitValidator>();
            }
        }

        private void ConfigureNetworkManager()
        {
            if (m_NetworkManager == null || m_NetworkManager.NetworkConfig == null)
            {
                return;
            }

            m_NetworkManager.NetworkConfig.TickRate = m_TickRate;
            m_NetworkManager.NetworkConfig.EnableSceneManagement = true;
            // NetworkMatchManager spawns team-balanced player avatars
            m_NetworkManager.NetworkConfig.PlayerPrefab = null;

            RegisterPrefabIfMissing(m_PlayerNetworkPrefab);
            RegisterPrefabIfMissing(m_BotNetworkPrefab);
            RegisterPrefabIfMissing(m_MatchManagerNetworkPrefab);
        }

        public void RegisterPrefabIfMissing(GameObject prefab)
        {
            if (prefab == null || m_NetworkManager == null || m_NetworkManager.NetworkConfig == null)
            {
                return;
            }

            if (prefab.GetComponent<NetworkObject>() == null)
            {
                return;
            }

            var prefabs = m_NetworkManager.NetworkConfig.Prefabs;
            if (prefabs != null && !prefabs.Contains(prefab))
            {
                m_NetworkManager.AddNetworkPrefab(prefab);
            }
        }

        private void HookNetworkCallbacks()
        {
            if (m_CallbacksHooked || m_NetworkManager == null)
            {
                return;
            }

            m_NetworkManager.OnClientConnectedCallback += HandleClientConnected;
            m_NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            m_NetworkManager.OnTransportFailure += HandleTransportFailure;
            m_CallbacksHooked = true;
        }

        private void UnhookNetworkCallbacks()
        {
            if (!m_CallbacksHooked || m_NetworkManager == null)
            {
                return;
            }

            m_NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
            m_NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            m_NetworkManager.OnTransportFailure -= HandleTransportFailure;
            m_CallbacksHooked = false;
        }

        private void HandleClientConnected(ulong clientId)
        {
            VanguardSessionManager.Instance?.RebuildAndBroadcastLobbyRoster();
        }

        private async void HandleClientDisconnected(ulong clientId)
        {
            if (m_NetworkManager == null)
            {
                return;
            }

            // On a remote client, return cleanly to MainMenu only if we disconnected or the host shut down
            bool localOrHostDisconnected = clientId == m_NetworkManager.LocalClientId || clientId == NetworkManager.ServerClientId;
            if (!m_NetworkManager.IsServer && localOrHostDisconnected)
            {
                if (VanguardSessionManager.Instance != null)
                {
                    string reason = !string.IsNullOrWhiteSpace(m_NetworkManager.DisconnectReason)
                        ? m_NetworkManager.DisconnectReason
                        : "Disconnected from host — match ended.";
                    await VanguardSessionManager.Instance.LeaveSessionAsync(reason);
                    return;
                }
            }

            VanguardSessionManager.Instance?.RebuildAndBroadcastLobbyRoster();
        }

        private async void HandleTransportFailure()
        {
            Debug.LogWarning("[VanguardNetworkBootstrap] Network transport failure detected. Recovering back to MainMenu.");
            if (VanguardSessionManager.Instance != null)
            {
                await VanguardSessionManager.Instance.LeaveSessionAsync("Network transport connection lost.");
            }
        }

        private void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
        {
            CheckDirectGameplaySceneAutoHost(scene);
        }

        private async void CheckDirectGameplaySceneAutoHost(Scene scene)
        {
            if (!m_AutoStartSoloWhenLaunchingGameplaySceneDirectly)
            {
                return;
            }

            if (!string.Equals(scene.name, VanguardNetServices.GameplaySceneName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Give one frame for any active session or NetworkManager startup
            await System.Threading.Tasks.Task.Yield();

            if (m_NetworkManager != null && !m_NetworkManager.IsListening && VanguardSessionManager.Instance != null)
            {
                await VanguardSessionManager.Instance.StartSoloSkirmishAsync(MatchRulesConfig.CreateDefault(MatchMode.TeamDeathmatch));
            }
        }
    }
}
