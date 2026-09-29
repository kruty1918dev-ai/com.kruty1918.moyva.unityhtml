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
        public List<UiDocumentInventory> scenes = new();
        public List<UiDocumentInventory> prefabs = new();
        public List<UiScriptReferenceInventory> scriptsWithUiReferences = new();
        public List<UiScriptCreationInventory> scriptsCreatingUi = new();
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
