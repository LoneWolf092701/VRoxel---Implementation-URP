//// Assets/Scripts/WFC/WFCComponentRegistry.cs
//using UnityEngine;
//using WFC.Chunking;
//using WFC.Generation;
//using WFC.MarchingCubes;
//using WFC.Performance;
//using WFC.Processing;

//namespace WFC
//{
//    /// <summary>
//    /// Central registry for WFC system components to manage dependencies and inter-component communication.
//    /// </summary>
//    public class WFCComponentRegistry
//    {
//        // Singleton instance
//        private static WFCComponentRegistry _instance;
//        public static WFCComponentRegistry Instance
//        {
//            get
//            {
//                if (_instance == null)
//                {
//                    _instance = new WFCComponentRegistry();
//                }
//                return _instance;
//            }
//        }

//        // Component references
//        public WFCGenerator WFCGenerator { get; private set; }
//        public ChunkManager ChunkManager { get; private set; }
//        public MeshGenerator MeshGenerator { get; private set; }
//        public ParallelWFCManager ParallelManager { get; private set; }
//        public PerformanceMonitor PerformanceMonitor { get; private set; }

//        // Events
//        public delegate void ComponentRegisteredHandler(string componentName, object component);
//        public event ComponentRegisteredHandler OnComponentRegistered;

//        // Registration methods
//        public void RegisterWFCGenerator(WFCGenerator generator)
//        {
//            WFCGenerator = generator;
//            Debug.Log($"<color=cyan>[WFC Registry]</color> Registered WFCGenerator");
//            OnComponentRegistered?.Invoke("WFCGenerator", generator);
//        }

//        public void RegisterChunkManager(ChunkManager manager)
//        {
//            ChunkManager = manager;
//            Debug.Log($"<color=cyan>[WFC Registry]</color> Registered ChunkManager");
//            OnComponentRegistered?.Invoke("ChunkManager", manager);
//        }

//        public void RegisterMeshGenerator(MeshGenerator generator)
//        {
//            MeshGenerator = generator;
//            Debug.Log($"<color=cyan>[WFC Registry]</color> Registered MeshGenerator");
//            OnComponentRegistered?.Invoke("MeshGenerator", generator);
//        }

//        public void RegisterParallelManager(ParallelWFCManager manager)
//        {
//            ParallelManager = manager;
//            Debug.Log($"<color=cyan>[WFC Registry]</color> Registered ParallelManager");
//            OnComponentRegistered?.Invoke("ParallelManager", manager);
//        }

//        public void RegisterPerformanceMonitor(PerformanceMonitor monitor)
//        {
//            PerformanceMonitor = monitor;
//            Debug.Log($"<color=cyan>[WFC Registry]</color> Registered PerformanceMonitor");
//            OnComponentRegistered?.Invoke("PerformanceMonitor", monitor);
//        }

//        /// <summary>
//        /// Connect components to each other by setting necessary references
//        /// </summary>
//        public void ConnectComponents()
//        {
//            Debug.Log($"<color=cyan>[WFC Registry]</color> Connecting registered components");

//            // Connect WFCGenerator to other components
//            if (WFCGenerator != null)
//            {
//                if (ChunkManager != null)
//                {
//                    // Set WFCGenerator on ChunkManager
//                    var wfcGeneratorField = ChunkManager.GetType().GetField("wfcGenerator",
//                        System.Reflection.BindingFlags.Instance |
//                        System.Reflection.BindingFlags.NonPublic |
//                        System.Reflection.BindingFlags.Public);

//                    if (wfcGeneratorField != null)
//                    {
//                        wfcGeneratorField.SetValue(ChunkManager, WFCGenerator);
//                        Debug.Log($"<color=cyan>[WFC Registry]</color> Connected WFCGenerator to ChunkManager");
//                    }
//                }

//                if (MeshGenerator != null)
//                {
//                    MeshGenerator.wfcGenerator = WFCGenerator;
//                    Debug.Log($"<color=cyan>[WFC Registry]</color> Connected WFCGenerator to MeshGenerator");
//                }

//                if (ParallelManager != null)
//                {
//                    ParallelManager.wfcGenerator = WFCGenerator;
//                    Debug.Log($"<color=cyan>[WFC Registry]</color> Connected WFCGenerator to ParallelManager");
//                }
//            }

//            // Connect ChunkManager to other components
//            if (ChunkManager != null)
//            {
//                if (MeshGenerator != null)
//                {
//                    // Set ChunkManager on MeshGenerator
//                    var chunkManagerField = MeshGenerator.GetType().GetField("chunkManager",
//                        System.Reflection.BindingFlags.Instance |
//                        System.Reflection.BindingFlags.NonPublic |
//                        System.Reflection.BindingFlags.Public);

//                    if (chunkManagerField != null)
//                    {
//                        chunkManagerField.SetValue(MeshGenerator, ChunkManager);
//                        Debug.Log($"<color=cyan>[WFC Registry]</color> Connected ChunkManager to MeshGenerator");
//                    }

//                    // Connect MeshGenerator to ChunkManager events
//                    try
//                    {
//                        var chunkManagerType = ChunkManager.GetType();
//                        var eventInfo = chunkManagerType.GetEvent("OnChunkStateChanged");

//                        if (eventInfo != null)
//                        {
//                            var meshGenType = MeshGenerator.GetType();
//                            var methodInfo = meshGenType.GetMethod("OnChunkStateChanged",
//                                System.Reflection.BindingFlags.Instance |
//                                System.Reflection.BindingFlags.NonPublic |
//                                System.Reflection.BindingFlags.Public);

//                            if (methodInfo != null)
//                            {
//                                var delegateType = eventInfo.EventHandlerType;
//                                var handler = System.Delegate.CreateDelegate(delegateType, MeshGenerator, methodInfo);

//                                eventInfo.AddEventHandler(ChunkManager, handler);
//                                Debug.Log($"<color=cyan>[WFC Registry]</color> Subscribed MeshGenerator to ChunkManager events");
//                            }
//                        }
//                    }
//                    catch (System.Exception e)
//                    {
//                        Debug.LogError($"<color=red>[WFC Registry]</color> Error connecting MeshGenerator to ChunkManager events: {e.Message}");
//                    }
//                }

//                if (ParallelManager != null)
//                {
//                    // Connect ChunkManager to ParallelManager if there's a field for it
//                    var chunkManagerField = ParallelManager.GetType().GetField("chunkManager",
//                        System.Reflection.BindingFlags.Instance |
//                        System.Reflection.BindingFlags.NonPublic |
//                        System.Reflection.BindingFlags.Public);

//                    if (chunkManagerField != null)
//                    {
//                        chunkManagerField.SetValue(ParallelManager, ChunkManager);
//                        Debug.Log($"<color=cyan>[WFC Registry]</color> Connected ChunkManager to ParallelManager");
//                    }
//                }

//                if (PerformanceMonitor != null)
//                {
//                    PerformanceMonitor.chunkManager = ChunkManager;
//                    Debug.Log($"<color=cyan>[WFC Registry]</color> Connected ChunkManager to PerformanceMonitor");
//                }
//            }

//            Debug.Log($"<color=cyan>[WFC Registry]</color> Component connections complete");
//        }
//    }
//}