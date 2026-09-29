using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace UnityHTML.Editor.Migration
{
    /// <summary>
    /// Будує відтворюваний звіт про UI через Unity API, не читаючи YAML як доменну модель.
    /// Сцени відкриваються тимчасово; початковий Editor setup відновлюється у finally.
    /// </summary>
    internal static class ProjectUiAnalyzer
    {
        internal const string ReportDirectory = "Temp/UnityHTML";
        internal const string JsonReportPath = ReportDirectory + "/project-ui-inventory.json";
        internal const string MarkdownReportPath = ReportDirectory + "/project-ui-inventory.md";

        private static readonly Type[] UiReferenceTypes =
        {
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(RectTransform),
            typeof(Graphic), typeof(Selectable), typeof(ScrollRect), typeof(Scrollbar),
            typeof(LayoutGroup), typeof(ContentSizeFitter), typeof(AspectRatioFitter),
            typeof(CanvasGroup), typeof(TMP_Text), typeof(TMP_InputField), typeof(TMP_Dropdown),
            typeof(EventSystem), typeof(Animator), typeof(Animation)
        };

        private static readonly string[] RuntimeCreationMarkers =
        {
            "new GameObject(", "Instantiate(", "Object.Instantiate(", "AddComponent<Canvas",
            "AddComponent<Button", "AddComponent<Image", "AddComponent<RawImage",
            "AddComponent<TextMeshProUGUI", "AddComponent<TMP_", "AddComponent<RectTransform"
        };

        [MenuItem("Tools/UnityHTML/Analyze Project UI")]
        internal static void AnalyzeFromMenu()
        {
            var inventory = AnalyzeProject();
            WriteReports(inventory);
            Debug.Log($"[UnityHTML] UI inventory: {MarkdownReportPath}");
        }

        internal static string AnalyzeForAutomation()
        {
            var inventory = AnalyzeProject();
            WriteReports(inventory);
            return $"UNITYHTML_UI_INVENTORY scenes={inventory.scenes.Count} prefabs={inventory.prefabs.Count} " +
                   $"scriptsWithRefs={inventory.scriptsWithUiReferences.Count} scriptsCreatingUi={inventory.scriptsCreatingUi.Count} " +
                   $"report={MarkdownReportPath}";
        }

        private static ProjectUiInventory AnalyzeProject()
        {
            var inventory = new ProjectUiInventory
            {
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion
            };

            foreach (var scene in EditorBuildSettings.scenes)
                if (scene.enabled)
                    inventory.buildScenes.Add(scene.path);

            var scenePaths = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Moyva" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            inventory.discoveredScenes.AddRange(scenePaths);

            var originalSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (var scenePath in scenePaths)
                    inventory.scenes.Add(AnalyzeScene(scenePath));
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
            }

            AnalyzePrefabs(inventory);
            AnalyzeScripts(inventory);
            return inventory;
        }

        private static UiDocumentInventory AnalyzeScene(string scenePath)
        {
            var document = new UiDocumentInventory { assetPath = scenePath, kind = "Scene" };
            try
            {
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                foreach (var canvas in FindSceneComponents<Canvas>(scene))
                    document.canvases.Add(AnalyzeCanvas(canvas, scenePath));

                var eventSystems = FindSceneComponents<EventSystem>(scene).Count;
                if (eventSystems > 0)
                    document.diagnostics.Add($"EventSystem: {eventSystems}");
            }
            catch (Exception exception)
            {
                document.diagnostics.Add(exception.GetBaseException().Message);
            }

            return document;
        }

        private static void AnalyzePrefabs(ProjectUiInventory inventory)
        {
            var prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Moyva" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal);

            foreach (var prefabPath in prefabPaths)
            {
                GameObject root = null;
                try
                {
                    root = PrefabUtility.LoadPrefabContents(prefabPath);
                    var canvases = root.GetComponentsInChildren<Canvas>(true);
                    var hasUi = canvases.Length > 0 || root.GetComponentInChildren<Graphic>(true) ||
                                root.GetComponentInChildren<Selectable>(true) || root.GetComponentInChildren<TMP_Text>(true);
                    if (!hasUi)
                        continue;

                    var document = new UiDocumentInventory { assetPath = prefabPath, kind = "Prefab" };
                    if (canvases.Length == 0)
                        document.canvases.Add(AnalyzeUiRoot(root, prefabPath, "Inherited"));
                    else
                        foreach (var canvas in canvases)
                            document.canvases.Add(AnalyzeCanvas(canvas, prefabPath));
                    inventory.prefabs.Add(document);
                }
                catch (Exception exception)
                {
                    inventory.prefabs.Add(new UiDocumentInventory
                    {
                        assetPath = prefabPath,
                        kind = "Prefab",
                        diagnostics = { exception.GetBaseException().Message }
                    });
                }
                finally
                {
                    if (root)
                        PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        private static UiCanvasInventory AnalyzeCanvas(Canvas canvas, string assetPath)
        {
            return AnalyzeUiRoot(canvas.gameObject, assetPath, canvas.renderMode.ToString());
        }

        private static UiCanvasInventory AnalyzeUiRoot(GameObject root, string assetPath, string renderMode)
        {
            var result = new UiCanvasInventory
            {
                hierarchyPath = GetHierarchyPath(root.transform),
                renderMode = renderMode,
                descendants = root.GetComponentsInChildren<Transform>(true).Length - 1
            };

            var components = root.GetComponentsInChildren<Component>(true).Where(component => component).ToArray();
            foreach (var group in components.GroupBy(component => component.GetType().FullName).OrderBy(group => group.Key, StringComparer.Ordinal))
                result.components.Add(new UiTypeCount { type = group.Key, count = group.Count() });

            foreach (var component in components)
            {
                CollectPersistentEvents(component, result);
                CollectExternalReferences(component, root.transform, assetPath, result);

                if (component is MonoBehaviour && !IsKnownPresentationComponent(component.GetType()))
                    result.customComponents.Add(component.GetType().FullName);
            }

            result.customComponents = result.customComponents.Distinct().OrderBy(value => value, StringComparer.Ordinal).ToList();
            result.classification = Classify(root, components, result);
            return result;
        }

        private static string Classify(GameObject root, Component[] components, UiCanvasInventory inventory)
        {
            var classes = new List<string> { "A static layout" };
            if (components.Any(component => component is Selectable)) classes.Add("B interactive controls");
            if (components.Any(component => component is LayoutGroup || component is ContentSizeFitter) ||
                inventory.customComponents.Any(name => ContainsAny(name, "List", "Inventory", "Factory", "Builder")))
                classes.Add("C runtime-generated UI candidate");
            if (components.OfType<Canvas>().Any(canvas => canvas.renderMode == RenderMode.WorldSpace)) classes.Add("D world-space UI");
            if (inventory.externalReferences.Count > 0) classes.Add("E external serialized references");
            if (PrefabUtility.IsPartOfPrefabAsset(root) || PrefabUtility.IsPartOfPrefabInstance(root)) classes.Add("F UI prefab");
            if (inventory.customComponents.Count > 0) classes.Add("H gameplay/presentation script coupling");
            return string.Join("; ", classes);
        }

        private static void AnalyzeScripts(ProjectUiInventory inventory)
        {
            var scriptPaths = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets/Moyva/Scripts" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal);

            foreach (var scriptPath in scriptPaths)
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
                var type = script ? script.GetClass() : null;
                if (type != null && typeof(MonoBehaviour).IsAssignableFrom(type))
                {
                    var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Where(field => IsUiReferenceType(field.FieldType))
                        .Select(field => $"{field.Name}: {FormatType(field.FieldType)}")
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToList();
                    if (fields.Count > 0)
                        inventory.scriptsWithUiReferences.Add(new UiScriptReferenceInventory
                        {
                            scriptPath = scriptPath,
                            type = type.FullName,
                            fields = fields
                        });
                }

                if (!File.Exists(scriptPath))
                    continue;
                var source = File.ReadAllText(scriptPath);
                var markers = RuntimeCreationMarkers.Where(source.Contains).ToList();
                if (markers.Count > 0)
                    inventory.scriptsCreatingUi.Add(new UiScriptCreationInventory
                    {
                        scriptPath = scriptPath,
                        markers = markers
                    });
            }
        }

        private static void CollectPersistentEvents(Component component, UiCanvasInventory result)
        {
            SerializedObject serializedObject;
            try
            {
                serializedObject = new SerializedObject(component);
            }
            catch
            {
                return;
            }

            var iterator = serializedObject.GetIterator();
            var enterChildren = true;
            while (iterator.Next(enterChildren))
            {
                enterChildren = true;
                if (!iterator.isArray || !iterator.propertyPath.EndsWith("m_PersistentCalls.m_Calls", StringComparison.Ordinal))
                    continue;

                for (var index = 0; index < iterator.arraySize; index++)
                {
                    var call = iterator.GetArrayElementAtIndex(index);
                    var target = call.FindPropertyRelative("m_Target")?.objectReferenceValue;
                    var method = call.FindPropertyRelative("m_MethodName")?.stringValue;
                    result.persistentEvents.Add(new UiEventInventory
                    {
                        objectPath = GetHierarchyPath(component.transform),
                        component = component.GetType().FullName,
                        eventPath = iterator.propertyPath,
                        target = FormatObject(target),
                        method = method
                    });
                }
                enterChildren = false;
            }
        }

        private static void CollectExternalReferences(Component component, Transform root, string assetPath, UiCanvasInventory result)
        {
            SerializedObject serializedObject;
            try
            {
                serializedObject = new SerializedObject(component);
            }
            catch
            {
                return;
            }

            var iterator = serializedObject.GetIterator();
            var enterChildren = true;
            while (iterator.Next(enterChildren))
            {
                enterChildren = true;
                if (iterator.propertyType != SerializedPropertyType.ObjectReference || iterator.objectReferenceValue == null ||
                    iterator.propertyPath == "m_Script")
                    continue;

                var target = iterator.objectReferenceValue;
                var targetTransform = GetTransform(target);
                var targetAssetPath = AssetDatabase.GetAssetPath(target);
                var isInsideUiRoot = targetTransform && (targetTransform == root || targetTransform.IsChildOf(root));
                var isSameAsset = !string.IsNullOrEmpty(targetAssetPath) && string.Equals(targetAssetPath, assetPath, StringComparison.Ordinal);
                if (isInsideUiRoot || isSameAsset)
                    continue;

                result.externalReferences.Add(new UiReferenceInventory
                {
                    objectPath = GetHierarchyPath(component.transform),
                    component = component.GetType().FullName,
                    field = iterator.propertyPath,
                    target = FormatObject(target)
                });
            }
        }

        private static void WriteReports(ProjectUiInventory inventory)
        {
            Directory.CreateDirectory(ReportDirectory);
            File.WriteAllText(JsonReportPath, JsonUtility.ToJson(inventory, true));
            File.WriteAllText(MarkdownReportPath, BuildMarkdown(inventory));
        }

        private static string BuildMarkdown(ProjectUiInventory inventory)
        {
            var text = new StringBuilder(32768);
            text.AppendLine("# UnityHTML UI migration inventory");
            text.AppendLine();
            text.AppendLine($"Generated: {inventory.generatedAtUtc}");
            text.AppendLine($"Unity: {inventory.unityVersion}");
            text.AppendLine($"Build scenes: {inventory.buildScenes.Count}");
            text.AppendLine($"Discovered Moyva scenes: {inventory.discoveredScenes.Count}");
            text.AppendLine($"UI prefabs: {inventory.prefabs.Count}");
            text.AppendLine($"Scripts with serialized UI fields: {inventory.scriptsWithUiReferences.Count}");
            text.AppendLine($"Scripts creating UI candidates: {inventory.scriptsCreatingUi.Count}");
            text.AppendLine();

            AppendDocuments(text, "Scenes", inventory.scenes);
            AppendDocuments(text, "UI prefabs", inventory.prefabs);

            text.AppendLine("## Scripts with UI references");
            text.AppendLine();
            foreach (var script in inventory.scriptsWithUiReferences)
                text.AppendLine($"- `{script.type}` (`{script.scriptPath}`): {string.Join(", ", script.fields)}");
            text.AppendLine();

            text.AppendLine("## Scripts creating UI candidates");
            text.AppendLine();
            foreach (var script in inventory.scriptsCreatingUi)
                text.AppendLine($"- `{script.scriptPath}`: {string.Join(", ", script.markers)}");
            return text.ToString();
        }

        private static void AppendDocuments(StringBuilder text, string title, List<UiDocumentInventory> documents)
        {
            text.AppendLine($"## {title}");
            text.AppendLine();
            foreach (var document in documents)
            {
                text.AppendLine($"### {document.assetPath}");
                text.AppendLine();
                text.AppendLine($"Canvases/UI roots: {document.canvases.Count}");
                foreach (var diagnostic in document.diagnostics)
                    text.AppendLine($"- Diagnostic: {diagnostic}");
                foreach (var canvas in document.canvases)
                {
                    text.AppendLine($"- `{canvas.hierarchyPath}` | {canvas.renderMode} | {canvas.classification}");
                    text.AppendLine($"  Components: {string.Join(", ", canvas.components.Select(item => $"{ShortType(item.type)}={item.count}"))}");
                    text.AppendLine($"  Persistent events: {canvas.persistentEvents.Count}; external refs: {canvas.externalReferences.Count}; custom components: {canvas.customComponents.Count}");
                }
                text.AppendLine();
            }
        }

        private static List<T> FindSceneComponents<T>(Scene scene) where T : Component
        {
            var result = new List<T>();
            foreach (var root in scene.GetRootGameObjects())
                result.AddRange(root.GetComponentsInChildren<T>(true));
            return result;
        }

        private static bool IsKnownPresentationComponent(Type type)
        {
            return type.Namespace != null &&
                   (type.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal) ||
                    type.Namespace.StartsWith("TMPro", StringComparison.Ordinal) ||
                    type.Namespace.StartsWith("Zenject", StringComparison.Ordinal));
        }

        private static bool IsUiReferenceType(Type type)
        {
            var candidate = type.IsArray ? type.GetElementType() : type;
            if (candidate != null && candidate.IsGenericType)
                candidate = candidate.GetGenericArguments().FirstOrDefault();
            return candidate != null && UiReferenceTypes.Any(uiType => uiType.IsAssignableFrom(candidate));
        }

        private static string FormatType(Type type) => type.FullName ?? type.Name;

        private static bool ContainsAny(string value, params string[] fragments) =>
            fragments.Any(fragment => value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);

        private static Transform GetTransform(UnityEngine.Object target)
        {
            if (target is GameObject gameObject) return gameObject.transform;
            if (target is Component component) return component.transform;
            return null;
        }

        private static string FormatObject(UnityEngine.Object target)
        {
            if (!target) return "null";
            var assetPath = AssetDatabase.GetAssetPath(target);
            if (!string.IsNullOrEmpty(assetPath)) return $"{assetPath}::{target.name}";
            var transform = GetTransform(target);
            return transform ? GetHierarchyPath(transform) : $"{target.GetType().FullName}::{target.name}";
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (!transform) return "<missing>";
            var names = new Stack<string>();
            for (var current = transform; current; current = current.parent)
                names.Push(current.name);
            return string.Join("/", names);
        }

        private static string ShortType(string type)
        {
            var separator = type.LastIndexOf('.');
            return separator >= 0 ? type.Substring(separator + 1) : type;
        }
    }
}
