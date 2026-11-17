using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OutfitPicker;

// Loads the optional picture that sits next to an outfit file (arty calm.amogus -> arty calm.png,
// .jpg and .jpeg also work). Textures are loaded lazily - only for tiles that are actually
// drawn - and cached by file path + last-write time, so reopening the window doesn't reload
// anything unless the picture changed.
public static class Thumbs
{
    public class Item
    {
        public DateTime Stamp;
        public Texture2D Tex;
        public GUIContent Content;   // built on demand by PickerUi (draw method 0)
        public GUIStyle BgStyle;     // built on demand by PickerUi (draw method 1)
        public bool Failed;
    }

    private static readonly Dictionary<string, Item> Cache = new();
    private static bool _unavailable;

    private static void Info(string m) => OutfitPickerPlugin.Instance.Log.LogInfo("OutfitPicker: " + m);
    private static void Warn(string m) => OutfitPickerPlugin.Instance.Log.LogWarning("OutfitPicker: " + m);

    // Null if the outfit has no picture, it couldn't be read, or picture loading isn't available.
    public static Item Get(OutfitEntry e)
    {
        if (e == null || e.ImagePath == null || _unavailable) return null;

        if (Cache.TryGetValue(e.ImagePath, out var it))
        {
            if (it.Stamp == e.ImageStamp)
            {
                if (it.Failed) return null;
                if (it.Tex != null) return it; // Unity-null aware: false if the texture was destroyed natively
            }
            Drop(e.ImagePath, it);
        }

        return Load(e);
    }

    private static Item Load(OutfitEntry e)
    {
        var file = System.IO.Path.GetFileName(e.ImagePath);
        var item = new Item { Stamp = e.ImageStamp };
        Cache[e.ImagePath] = item; // cached even on failure, so a bad file isn't retried every frame

        try
        {
            var bytes = File.ReadAllBytes(e.ImagePath);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, bytes, false))
            {
                UnityEngine.Object.Destroy(tex);
                item.Failed = true;
                Warn($"couldn't read '{file}' as a picture (is it really a PNG or JPG?).");
                return null;
            }

            // Without this, Unity may unload the texture the next time it cleans up unused assets.
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            item.Tex = tex;

            Info($"loaded picture '{file}' ({tex.width}x{tex.height}).");
            if (tex.width > 512 || tex.height > 512)
                Warn($"'{file}' is {tex.width}x{tex.height} - a ~256px square crop looks the same here and uses far less memory.");

            return item;
        }
        catch (NotSupportedException ex)
        {
            _unavailable = true;
            Warn($"this game build can't load pictures ({ex.Message}); showing the flat crewmate instead.");
            return null;
        }
        catch (Exception ex)
        {
            item.Failed = true;
            Warn($"couldn't load picture '{file}'. {ex.Message}");
            return null;
        }
    }

    private static void Drop(string key, Item it)
    {
        Cache.Remove(key);
        try { if (it.Tex != null) UnityEngine.Object.Destroy(it.Tex); }
        catch (Exception) { /* already gone */ }
    }

    // Called after every folder reload: free textures whose outfit/picture no longer exists.
    public static void Prune(List<OutfitEntry> entries)
    {
        var keep = new HashSet<string>();
        foreach (var e in entries)
            if (e.ImagePath != null) keep.Add(e.ImagePath);

        foreach (var kv in new List<KeyValuePair<string, Item>>(Cache))
            if (!keep.Contains(kv.Key)) Drop(kv.Key, kv.Value);
    }
}
