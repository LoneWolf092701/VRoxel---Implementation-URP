//// First, let's create a ScriptableObject to store our adjacency rules
//using System;
//using System.Collections.Generic;
//using UnityEditor;
//using UnityEngine;
//using WFC.Generation;

//namespace WFC.Rules
//{
//    /// <summary>
//    /// Defines which tiles can be adjacent to each other in the WFC algorithm
//    /// </summary>
//    [CreateAssetMenu(fileName = "WFCRules", menuName = "WFC/Adjacency Rules")]
//    public class WFCAdjacencyRules : ScriptableObject
//    {
//        [Serializable]
//        public class StateInfo
//        {
//            public string name;
//            public int id;
//            public Color color;
//            [TextArea]
//            public string description;
//        }

//        [Serializable]
//        public class AdjacencyRule
//        {
//            public int stateA;
//            public int stateB;

//            [Tooltip("Which directions this rule applies to")]
//            public DirectionFlags directions = DirectionFlags.All;

//            [Tooltip("Whether these states can be adjacent")]
//            public bool canBeAdjacent = true;

//            [Range(0f, 1f)]
//            [Tooltip("Weight/probability for this adjacency")]
//            public float weight = 1.0f;
//        }

//        [Flags]
//        public enum DirectionFlags
//        {
//            None = 0,
//            Up = 1 << 0,       // Y+
//            Right = 1 << 1,    // X+
//            Down = 1 << 2,     // Y-
//            Left = 1 << 3,     // X-
//            Forward = 1 << 4,  // Z+
//            Back = 1 << 5,     // Z-
//            All = Up | Right | Down | Left | Forward | Back
//        }

//        [Header("State Definitions")]
//        [SerializeField] private List<StateInfo> states = new List<StateInfo>();

//        [Header("Adjacency Rules")]
//        [SerializeField] private List<AdjacencyRule> rules = new List<AdjacencyRule>();

//        // For fast lookups during runtime
//        private Dictionary<(int, int, int), bool> ruleCache;

//        /// <summary>
//        /// Initialize the rule cache for fast lookups at runtime
//        /// </summary>
//        public void Initialize()
//        {
//            ruleCache = new Dictionary<(int, int, int), bool>();

//            foreach (var rule in rules)
//            {
//                // Apply rule to each specified direction
//                for (int d = 0; d < 6; d++)
//                {
//                    DirectionFlags dirFlag = (DirectionFlags)(1 << d);
//                    if ((rule.directions & dirFlag) != 0)
//                    {
//                        // Set rule for both directions of the state pair
//                        ruleCache[(rule.stateA, rule.stateB, d)] = rule.canBeAdjacent;
//                        ruleCache[(rule.stateB, rule.stateA, GetOppositeDirection(d))] = rule.canBeAdjacent;
//                    }
//                }
//            }

//            // Set defaults for any undefined rules (false = not adjacent)
//            for (int i = 0; i < states.Count; i++)
//            {
//                for (int j = 0; j < states.Count; j++)
//                {
//                    for (int d = 0; d < 6; d++)
//                    {
//                        if (!ruleCache.ContainsKey((i, j, d)))
//                        {
//                            ruleCache[(i, j, d)] = false;
//                        }
//                    }
//                }
//            }
//        }

//        private int GetOppositeDirection(int direction)
//        {
//            switch (direction)
//            {
//                case 0: return 2; // Up -> Down
//                case 1: return 3; // Right -> Left
//                case 2: return 0; // Down -> Up
//                case 3: return 1; // Left -> Right
//                case 4: return 5; // Forward -> Back
//                case 5: return 4; // Back -> Forward
//                default: return direction;
//            }
//        }

//        /// <summary>
//        /// Check if two states can be adjacent in a given direction
//        /// </summary>
//        public bool AreStatesCompatible(int stateA, int stateB, int direction)
//        {
//            if (ruleCache == null)
//                Initialize();

//            if (ruleCache.TryGetValue((stateA, stateB, direction), out bool result))
//                return result;

//            return false; // Default to not compatible
//        }

//        /// <summary>
//        /// Get all possible states
//        /// </summary>
//        public IEnumerable<int> GetAllStates()
//        {
//            for (int i = 0; i < states.Count; i++)
//                yield return states[i].id;
//        }

//        /// <summary>
//        /// Get maximum state ID
//        /// </summary>
//        public int GetMaxStateID()
//        {
//            int max = 0;
//            foreach (var state in states)
//            {
//                max = Mathf.Max(max, state.id);
//            }
//            return max;
//        }

//        /// <summary>
//        /// Get state information by ID
//        /// </summary>
//        public StateInfo GetStateInfo(int id)
//        {
//            return states.Find(s => s.id == id);
//        }

//        /// <summary>
//        /// Add a rule that makes two states adjacent in all directions
//        /// </summary>
//        public void AddAdjacentRule(int stateA, int stateB, bool canBeAdjacent = true)
//        {
//            rules.Add(new AdjacencyRule
//            {
//                stateA = stateA,
//                stateB = stateB,
//                directions = DirectionFlags.All,
//                canBeAdjacent = canBeAdjacent
//            });
//        }

//        /// <summary>
//        /// Export rules to a JSON file
//        /// </summary>
//        public string ExportRulesToJson()
//        {
//            WFCRulesData data = new WFCRulesData
//            {
//                states = this.states,
//                rules = this.rules
//            };

//            return JsonUtility.ToJson(data, true);
//        }

//        /// <summary>
//        /// Import rules from a JSON file
//        /// </summary>
//        public void ImportRulesFromJson(string json)
//        {
//            WFCRulesData data = JsonUtility.FromJson<WFCRulesData>(json);
//            this.states = data.states;
//            this.rules = data.rules;
//            Initialize();
//        }

//        [Serializable]
//        private class WFCRulesData
//        {
//            public List<StateInfo> states;
//            public List<AdjacencyRule> rules;
//        }
//    }
//}

//// Now let's modify the WFCGenerator to use our data-driven rules
//// Add this to WFCGenerator.cs:

//// In WFCGenerator class:
//[Header("Rule Configuration")]
//[SerializeField] private WFC.Rules.WFCAdjacencyRules rulesAsset;

//private void SetupAdjacencyRules()
//{
//    // Initialize with falses
//    for (int i = 0; i < MaxCellStates; i++)
//    {
//        for (int j = 0; j < MaxCellStates; j++)
//        {
//            for (int d = 0; d < 6; d++)
//            {
//                adjacencyRules[i, j, d] = false;
//            }
//        }
//    }

//    if (rulesAsset != null)
//    {
//        // Initialize rule asset if needed
//        if (rulesAsset.GetMaxStateID() >= MaxCellStates)
//        {
//            Debug.LogWarning($"Rules asset contains states with IDs greater than MaxCellStates ({MaxCellStates}). Some rules will be ignored.");
//        }

//        // Apply all rules from the asset
//        for (int i = 0; i < MaxCellStates; i++)
//        {
//            for (int j = 0; j < MaxCellStates; j++)
//            {
//                for (int d = 0; d < 6; d++)
//                {
//                    adjacencyRules[i, j, d] = rulesAsset.AreStatesCompatible(i, j, d);
//                }
//            }
//        }
//    }
//    else
//    {
//        // Fallback to hardcoded rules if no asset is provided
//        SetupHardcodedRules();
//        Debug.LogWarning("No WFC Rules asset assigned. Using hardcoded rules as fallback.");
//    }
//}

//private void SetupHardcodedRules()
//{
//    // Original hardcoded rules as fallback
//    // Define rules based on the configuration or hardcoded rules
//    // For example: empty(0) can only be next to empty
//    SetAdjacentAll(0, 0, true);

//    // Ground(1) can be next to most things
//    SetAdjacentAll(1, 1, true);
//    SetAdjacentAll(1, 2, true);
//    SetAdjacentAll(1, 4, true);

//    // Grass(2) adjacent rules
//    SetAdjacentAll(2, 2, true);
//    SetAdjacentAll(2, 3, true);
//    SetAdjacentAll(2, 5, true);
//    SetAdjacentAll(2, 6, true);

//    // Water(3) adjacent rules
//    SetAdjacentAll(3, 3, true);
//    SetAdjacentAll(3, 5, true);

//    // Rock(4) adjacent rules
//    SetAdjacentAll(4, 4, true);

//    // Sand(5) adjacent rules
//    SetAdjacentAll(5, 5, true);

//    // Tree(6) adjacent rules
//    SetAdjacentAll(6, 6, true);
//}

//// Finally, let's create an editor script to make it easy to configure rules
//#if UNITY_EDITOR
//using UnityEditor;
//using UnityEngine;
//using System.Collections.Generic;
//using System.IO;

//namespace WFC.Rules.Editor
//{
//    [CustomEditor(typeof(WFCAdjacencyRules))]
//    public class WFCAdjacencyRulesEditor : UnityEditor.Editor
//    {
//        private bool showStates = true;
//        private bool showRules = true;
//        private bool showExportImport = false;
//        private string exportPath = "Assets/WFCRules.json";

//        public override void OnInspectorGUI()
//        {
//            WFCAdjacencyRules rulesAsset = (WFCAdjacencyRules)target;

//            // Basic info
//            EditorGUILayout.HelpBox(
//                "Configure adjacency rules for Wave Function Collapse.\n" +
//                "These rules define which states can be next to each other in different directions.",
//                MessageType.Info);

//            // States section
//            showStates = EditorGUILayout.Foldout(showStates, "States", true);
//            if (showStates)
//            {
//                EditorGUI.indentLevel++;
//                SerializedProperty statesProp = serializedObject.FindProperty("states");
//                EditorGUILayout.PropertyField(statesProp, true);

//                GUILayout.Space(10);
//                if (GUILayout.Button("Add New State"))
//                {
//                    statesProp.arraySize++;
//                    SerializedProperty newState = statesProp.GetArrayElementAtIndex(statesProp.arraySize - 1);
//                    newState.FindPropertyRelative("name").stringValue = "New State";
//                    newState.FindPropertyRelative("id").intValue = statesProp.arraySize - 1;
//                    newState.FindPropertyRelative("color").colorValue = Random.ColorHSV(0f, 1f, 0.5f, 1f, 0.5f, 1f);
//                }
//                EditorGUI.indentLevel--;
//            }

//            // Rules section
//            showRules = EditorGUILayout.Foldout(showRules, "Adjacency Rules", true);
//            if (showRules)
//            {
//                EditorGUI.indentLevel++;
//                SerializedProperty rulesProp = serializedObject.FindProperty("rules");
//                EditorGUILayout.PropertyField(rulesProp, true);

//                GUILayout.Space(10);
//                if (GUILayout.Button("Add New Rule"))
//                {
//                    rulesProp.arraySize++;
//                    SerializedProperty newRule = rulesProp.GetArrayElementAtIndex(rulesProp.arraySize - 1);
//                    newRule.FindPropertyRelative("canBeAdjacent").boolValue = true;
//                    newRule.FindPropertyRelative("weight").floatValue = 1.0f;
//                }

//                GUILayout.Space(10);
//                if (GUILayout.Button("Add Common Rules"))
//                {
//                    ShowAddCommonRulesMenu();
//                }
//                EditorGUI.indentLevel--;
//            }

//            // Export/Import section
//            showExportImport = EditorGUILayout.Foldout(showExportImport, "Export/Import", true);
//            if (showExportImport)
//            {
//                EditorGUI.indentLevel++;

//                exportPath = EditorGUILayout.TextField("File Path", exportPath);

//                GUILayout.BeginHorizontal();
//                if (GUILayout.Button("Export Rules"))
//                {
//                    string json = rulesAsset.ExportRulesToJson();
//                    File.WriteAllText(exportPath, json);
//                    AssetDatabase.Refresh();
//                    Debug.Log($"Rules exported to {exportPath}");
//                }

//                if (GUILayout.Button("Import Rules"))
//                {
//                    if (File.Exists(exportPath))
//                    {
//                        string json = File.ReadAllText(exportPath);
//                        rulesAsset.ImportRulesFromJson(json);
//                        EditorUtility.SetDirty(rulesAsset);
//                        serializedObject.Update();
//                        Debug.Log($"Rules imported from {exportPath}");
//                    }
//                    else
//                    {
//                        EditorUtility.DisplayDialog("File Not Found",
//                            $"Could not find file at {exportPath}", "OK");
//                    }
//                }
//                GUILayout.EndHorizontal();

//                EditorGUI.indentLevel--;
//            }

//            // Rules visualization coming soon
//            EditorGUILayout.HelpBox("Rule visualization coming soon...", MessageType.Info);

//            // Apply changes
//            serializedObject.ApplyModifiedProperties();

//            // Add helper buttons
//            GUILayout.Space(20);
//            if (GUILayout.Button("Generate Documentation"))
//            {
//                GenerateRulesDocumentation(rulesAsset);
//            }

//            // Test rules
//            if (Application.isPlaying && GUILayout.Button("Apply Rules to Current Game"))
//            {
//                rulesAsset.Initialize();
//                FindObjectOfType<WFCGenerator>()?.ResetGeneration();
//            }
//        }

//        private void ShowAddCommonRulesMenu()
//        {
//            GenericMenu menu = new GenericMenu();
//            menu.AddItem(new GUIContent("Terrain Rules"), false, AddTerrainRules);
//            menu.AddItem(new GUIContent("Building Rules"), false, AddBuildingRules);
//            menu.AddItem(new GUIContent("Clear All Rules"), false, ClearAllRules);
//            menu.ShowAsContext();
//        }

//        private void AddTerrainRules()
//        {
//            WFCAdjacencyRules rulesAsset = (WFCAdjacencyRules)target;
//            SerializedProperty rulesProp = serializedObject.FindProperty("rules");

//            // Clear existing rules if requested
//            if (EditorUtility.DisplayDialog("Clear Existing Rules?",
//                "Do you want to clear existing rules before adding terrain rules?", "Yes", "No"))
//            {
//                rulesProp.ClearArray();
//            }

//            // Add basic terrain adjacency rules
//            int startIndex = rulesProp.arraySize;
//            rulesProp.arraySize += 12; // Add 12 new rules

//            // Set up some example terrain rules
//            SetRuleProperties(rulesProp, startIndex++, 0, 0, WFCAdjacencyRules.DirectionFlags.All, true); // Empty next to empty
//            SetRuleProperties(rulesProp, startIndex++, 1, 1, WFCAdjacencyRules.DirectionFlags.All, true); // Ground next to ground
//            SetRuleProperties(rulesProp, startIndex++, 1, 2, WFCAdjacencyRules.DirectionFlags.All, true); // Ground next to grass
//            SetRuleProperties(rulesProp, startIndex++, 1, 4, WFCAdjacencyRules.DirectionFlags.All, true); // Ground next to rock
//            SetRuleProperties(rulesProp, startIndex++, 2, 2, WFCAdjacencyRules.DirectionFlags.All, true); // Grass next to grass
//            SetRuleProperties(rulesProp, startIndex++, 2, 3, WFCAdjacencyRules.DirectionFlags.All, true); // Grass next to water
//            SetRuleProperties(rulesProp, startIndex++, 2, 5, WFCAdjacencyRules.DirectionFlags.All, true); // Grass next to sand
//            SetRuleProperties(rulesProp, startIndex++, 2, 6, WFCAdjacencyRules.DirectionFlags.All, true); // Grass next to tree
//            SetRuleProperties(rulesProp, startIndex++, 3, 3, WFCAdjacencyRules.DirectionFlags.All, true); // Water next to water
//            SetRuleProperties(rulesProp, startIndex++, 3, 5, WFCAdjacencyRules.DirectionFlags.All, true); // Water next to sand
//            SetRuleProperties(rulesProp, startIndex++, 5, 5, WFCAdjacencyRules.DirectionFlags.All, true); // Sand next to sand
//            SetRuleProperties(rulesProp, startIndex++, 6, 6, WFCAdjacencyRules.DirectionFlags.All, true); // Tree next to tree

//            serializedObject.ApplyModifiedProperties();
//            EditorUtility.SetDirty(target);
//        }

//        private void AddBuildingRules()
//        {
//            // Similar to AddTerrainRules but for building-related adjacency rules
//            // This is a placeholder - implement with actual building rules
//            EditorUtility.DisplayDialog("Not Implemented",
//                "Building rules not implemented in this example.", "OK");
//        }

//        private void ClearAllRules()
//        {
//            if (EditorUtility.DisplayDialog("Clear All Rules",
//                "Are you sure you want to clear all rules?", "Yes", "No"))
//            {
//                SerializedProperty rulesProp = serializedObject.FindProperty("rules");
//                rulesProp.ClearArray();
//                serializedObject.ApplyModifiedProperties();
//                EditorUtility.SetDirty(target);
//            }
//        }

//        private void SetRuleProperties(SerializedProperty rulesProp, int index, int stateA, int stateB,
//                                  WFCAdjacencyRules.DirectionFlags directions, bool canBeAdjacent, float weight = 1.0f)
//        {
//            SerializedProperty rule = rulesProp.GetArrayElementAtIndex(index);
//            rule.FindPropertyRelative("stateA").intValue = stateA;
//            rule.FindPropertyRelative("stateB").intValue = stateB;
//            rule.FindPropertyRelative("directions").enumValueIndex = (int)directions;
//            rule.FindPropertyRelative("canBeAdjacent").boolValue = canBeAdjacent;
//            rule.FindPropertyRelative("weight").floatValue = weight;
//        }

//        private void GenerateRulesDocumentation(WFCAdjacencyRules rulesAsset)
//        {
//            rulesAsset.Initialize(); // Make sure the rule cache is initialized

//            // Get state info
//            SerializedProperty statesProp = serializedObject.FindProperty("states");
//            Dictionary<int, string> stateNames = new Dictionary<int, string>();

//            for (int i = 0; i < statesProp.arraySize; i++)
//            {
//                SerializedProperty state = statesProp.GetArrayElementAtIndex(i);
//                int id = state.FindPropertyRelative("id").intValue;
//                string name = state.FindPropertyRelative("name").stringValue;
//                stateNames[id] = name;
//            }

//            // Create a string report
//            System.Text.StringBuilder sb = new System.Text.StringBuilder();
//            sb.AppendLine("# WFC Rules Documentation");
//            sb.AppendLine();

//            sb.AppendLine("## States");
//            foreach (var pair in stateNames)
//            {
//                sb.AppendLine($"* State {pair.Key}: {pair.Value}");
//            }
//            sb.AppendLine();

//            sb.AppendLine("## Adjacency Rules");
//            SerializedProperty rulesProp = serializedObject.FindProperty("rules");
//            for (int i = 0; i < rulesProp.arraySize; i++)
//            {
//                SerializedProperty rule = rulesProp.GetArrayElementAtIndex(i);
//                int stateA = rule.FindPropertyRelative("stateA").intValue;
//                int stateB = rule.FindPropertyRelative("stateB").intValue;
//                bool canBeAdjacent = rule.FindPropertyRelative("canBeAdjacent").boolValue;
//                WFCAdjacencyRules.DirectionFlags directions =
//                    (WFCAdjacencyRules.DirectionFlags)rule.FindPropertyRelative("directions").enumValueIndex;

//                string stateAName = stateNames.ContainsKey(stateA) ? stateNames[stateA] : $"State {stateA}";
//                string stateBName = stateNames.ContainsKey(stateB) ? stateNames[stateB] : $"State {stateB}";

//                sb.AppendLine($"* {stateAName} {(canBeAdjacent ? "CAN" : "CANNOT")} be adjacent to {stateBName} in directions: {directions}");
//            }

//            // Show in a text area
//            EditorGUIUtility.systemCopyBuffer = sb.ToString();
//            EditorUtility.DisplayDialog("Rules Documentation",
//                "Documentation copied to clipboard!", "OK");
//        }
//    }
//}
//#endif