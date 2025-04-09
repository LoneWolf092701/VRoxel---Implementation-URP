using UnityEngine;

namespace WFC.Configuration
{
    /// <summary>
    /// Manager class providing global access to WFC configuration.
    /// Acts as a singleton for accessing the configuration throughout the system.
    /// </summary>
    public class WFCConfigManager : MonoBehaviour
    {
        [Tooltip("Reference to the WFC configuration asset")]
        [SerializeField] private WFCConfiguration configAsset;

        // Singleton instance
        private static WFCConfigManager _instance;

        /// <summary>
        /// Gets the active configuration
        /// </summary>
        public static WFCConfiguration Config
        {
            get
            {
                if (_instance == null)
                {
                    Debug.LogWarning("WFCConfigManager: No instance found. Searching for instance in scene...");
                    _instance = FindAnyObjectByType<WFCConfigManager>();

                    if (_instance == null)
                    {
                        Debug.LogError("WFCConfigManager: No instance found in scene. Creating temporary instance...");

                        // Create a new GameObject with the manager
                        GameObject go = new GameObject("WFCConfigManager");
                        _instance = go.AddComponent<WFCConfigManager>();

                        // Try to find a configuration asset
                        _instance.configAsset = Resources.Load<WFCConfiguration>("DefaultWFCConfig");

                        if (_instance.configAsset == null)
                        {
                            Debug.LogError("WFCConfigManager: No configuration asset found. Create a WFCConfiguration asset in Resources/DefaultWFCConfig or assign manually.");

                            // Create a default configuration
                            _instance.configAsset = ScriptableObject.CreateInstance<WFCConfiguration>();
                        }
                    }
                }

                return _instance.configAsset;
            }
        }

        private void Awake()
        {
            // Singleton pattern
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("WFCConfigManager: Multiple instances detected. Destroying duplicate.");
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            // Validate configuration on startup
            ValidateConfiguration();
        }

        /// <summary>
        /// Validates the configuration and logs any issues
        /// </summary>
        public void ValidateConfiguration()
        {
            if (configAsset == null)
            {
                Debug.LogError("WFCConfigManager: No configuration asset assigned.");
                return;
            }

            if (!configAsset.Validate())
            {
                Debug.LogWarning("WFCConfigManager: Configuration validation failed. Check the console for details.");
            }
        }

        /// <summary>
        /// Sets a new configuration asset at runtime
        /// </summary>
        /// <param name="newConfig">The new configuration to use</param>
        public static void SetConfiguration(WFCConfiguration newConfig)
        {
            if (_instance == null)
            {
                Debug.LogError("WFCConfigManager: Cannot set configuration. No instance exists.");
                return;
            }

            _instance.configAsset = newConfig;
            _instance.ValidateConfiguration();
        }

        /// <summary>
        /// Reloads the configuration asset from disk
        /// </summary>
        public static void ReloadConfiguration()
        {
            if (_instance != null && _instance.configAsset != null)
            {
                string path = UnityEditor.AssetDatabase.GetAssetPath(_instance.configAsset);
                if (!string.IsNullOrEmpty(path))
                {
                    #if UNITY_EDITOR
                    WFCConfiguration reloadedConfig = UnityEditor.AssetDatabase.LoadAssetAtPath<WFCConfiguration>(path);        //have to check
                    #else
                        Debug.LogWarning("Cannot reload configuration at runtime in builds");
                    #endif
                    if (reloadedConfig != null)
                    {
                        _instance.configAsset = reloadedConfig;
                        _instance.ValidateConfiguration();
                    }
                }
            }
        }
    }
}