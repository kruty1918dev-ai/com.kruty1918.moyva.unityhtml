using System;
using System.Collections.Generic;

namespace UnityHTML.Editor.Migration
{
    [Serializable]
    internal sealed class ProjectUiInventory
    {
        public string generatedAtUtc;
        public string unityVersion;
        public List<string> buildScenes = new();
        public List<string> discoveredScenes = new();
        public List<string> diagnostics = new();
        public List<UiDocumentInventory> scenes = new();
        public List<UiDocumentInventory> prefabs = new();
        public List<UiScriptReferenceInventory> scriptsWithUiReferences = new();
        public List<UiScriptCreationInventory> scriptsCreatingUi = new();
    }

    /// <summary>
    /// Scan roots loaded from ProjectSettings/UnityHTMLAnalyzer.json —
    /// <c>{"sceneRoots":["Assets/UI"],"prefabRoots":[...],"scriptRoots":[...]}</c>.
    /// Missing file scans all of Assets.
    /// </summary>
    [Serializable]
    internal sealed class UnityHtmlAnalyzerSettings
    {
        public string[] sceneRoots = { "Assets" };
        public string[] prefabRoots = { "Assets" };
        public string[] scriptRoots = { "Assets" };

        internal static UnityHtmlAnalyzerSettings Load()
        {
            const string path = "ProjectSettings/UnityHTMLAnalyzer.json";
            try
            {
                if (System.IO.File.Exists(path))
                {
                    var settings = UnityEngine.JsonUtility.FromJson<UnityHtmlAnalyzerSettings>(
                        System.IO.File.ReadAllText(path));
                    if (settings != null)
                    {
                        settings.sceneRoots = Valid(settings.sceneRoots);
                        settings.prefabRoots = Valid(settings.prefabRoots);
                        settings.scriptRoots = Valid(settings.scriptRoots);
                        return settings;
                    }
                }
            }
            catch { /* fall through to defaults */ }
            return new UnityHtmlAnalyzerSettings();
        }

        private static string[] Valid(string[] roots)
            => roots is { Length: > 0 } ? roots : new[] { "Assets" };
    }

    [Serializable]
    internal sealed class UiDocumentInventory
    {
        public string assetPath;
        public string kind;
        public List<UiCanvasInventory> canvases = new();
        public List<string> diagnostics = new();
    }

    [Serializable]
    internal sealed class UiCanvasInventory
    {
        public string hierarchyPath;
        public string renderMode;
        public string classification;
        public int descendants;
        public List<UiTypeCount> components = new();
        public List<UiEventInventory> persistentEvents = new();
        public List<UiReferenceInventory> externalReferences = new();
        public List<UiReferenceInventory> inboundReferences = new();
        public List<string> customComponents = new();
    }

    [Serializable]
    internal sealed class UiTypeCount
    {
        public string type;
        public int count;
    }

    [Serializable]
    internal sealed class UiEventInventory
    {
        public string objectPath;
        public string component;
        public string eventPath;
        public string target;
        public string method;
    }

    [Serializable]
    internal sealed class UiReferenceInventory
    {
        public string objectPath;
        public string component;
        public string field;
        public string target;
    }

    [Serializable]
    internal sealed class UiScriptReferenceInventory
    {
        public string scriptPath;
        public string type;
        public List<string> fields = new();
    }

    [Serializable]
    internal sealed class UiScriptCreationInventory
    {
        public string scriptPath;
        public List<string> markers = new();
    }
}
