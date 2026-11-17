using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace OutfitPicker;

// Same JSON shape as OutfitChanger's *.amogus files (property names matter).
public class Customization
{
    public string name { get; set; }
    public int colorID { get; set; }
    public string pet { get; set; }
    public string hat { get; set; }
    public string skin { get; set; }
    public string visor { get; set; }
    public string namePlate { get; set; }

    // Methods, not properties, so they never end up in the JSON.
    public string HatOrDefault() => Or(hat, "hat_NoHat");
    public string PetOrDefault() => Or(pet, "pet_EmptyPet");
    public string SkinOrDefault() => Or(skin, "skin_None");
    public string VisorOrDefault() => Or(visor, "visor_EmptyVisor");
    public string NamePlateOrDefault() => Or(namePlate, "nameplate_NoPlate");

    private static string Or(string s, string def) => string.IsNullOrEmpty(s) ? def : s;

    public Customization Clone() => new Customization
    {
        name = name, colorID = colorID, hat = hat, pet = pet, skin = skin, visor = visor, namePlate = namePlate,
    };

    public bool SameLookAs(Customization o) =>
        o != null
        && colorID == o.colorID
        && HatOrDefault() == o.HatOrDefault()
        && PetOrDefault() == o.PetOrDefault()
        && SkinOrDefault() == o.SkinOrDefault()
        && VisorOrDefault() == o.VisorOrDefault()
        && NamePlateOrDefault() == o.NamePlateOrDefault();
}

public class Root
{
    public Customization customization { get; set; }
}

public class OutfitEntry
{
    public string Path;
    public Customization Data;
    public string ImagePath;      // picture next to the outfit file, or null
    public DateTime ImageStamp;   // its last-write time (cache key)
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
}

public static class OutfitStore
{
    public static List<OutfitEntry> Entries = new();

    internal static string Folder => OutfitPickerPlugin.OutfitFolder.Value;

    public static void Reload()
    {
        var list = new List<OutfitEntry>();
        try
        {
            if (Directory.Exists(Folder))
            {
                foreach (var file in Directory.GetFiles(Folder, "*.amogus")
                             .OrderBy(f => System.IO.Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        var root = JsonSerializer.Deserialize<Root>(File.ReadAllText(file));
                        if (root?.customization == null) continue;
                        var img = FindImage(file);
                        list.Add(new OutfitEntry
                        {
                            Path = file,
                            Data = root.customization,
                            ImagePath = img,
                            ImageStamp = img != null ? File.GetLastWriteTimeUtc(img) : default,
                        });
                    }
                    catch (Exception e)
                    {
                        OutfitPickerPlugin.Instance.Log.LogWarning($"OutfitPicker: skipping unreadable outfit '{file}'. {e.Message}");
                    }
                }
            }
        }
        catch (Exception e)
        {
            OutfitPickerPlugin.Instance.Log.LogWarning($"OutfitPicker: couldn't read outfit folder. {e.Message}");
        }

        Entries = list;
        Thumbs.Prune(list);
    }

    private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg" };

    // arty calm.amogus -> arty calm.png (or .jpg / .jpeg) in the same folder.
    internal static string FindImage(string amogusPath)
    {
        foreach (var ext in ImageExts)
        {
            var p = System.IO.Path.ChangeExtension(amogusPath, ext);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    // What the local player is wearing right now (read from the game, not from a file).
    public static Customization CaptureCurrent()
    {
        var player = PlayerControl.LocalPlayer;
        var outfit = player != null && player.Data != null ? player.Data.DefaultOutfit : null;
        if (outfit == null) return null;

        return new Customization
        {
            name = outfit.PlayerName,
            colorID = outfit.ColorId,
            hat = outfit.HatId,
            pet = outfit.PetId,
            skin = outfit.SkinId,
            visor = outfit.VisorId,
            namePlate = outfit.NamePlateId,
        };
    }

    public static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var bad = System.IO.Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var ch in name.Trim())
            if (Array.IndexOf(bad, ch) < 0) sb.Append(ch);
        return sb.ToString().Trim().TrimEnd('.');
    }

    private static string PathFor(string stem) => System.IO.Path.Combine(Folder, stem + ".amogus");

    // Returns the saved path, or null with an error message.
    public static string SaveNew(string requestedName, Customization data, out string error)
    {
        error = null;
        if (data == null) { error = "Couldn't read your current outfit."; return null; }

        try
        {
            Directory.CreateDirectory(Folder);

            var stem = Sanitize(requestedName);
            if (stem.Length == 0)
            {
                var i = 1;
                while (File.Exists(PathFor($"outfit{i}"))) i++;
                stem = $"outfit{i}";
            }
            else if (File.Exists(PathFor(stem)))
            {
                var i = 2;
                while (File.Exists(PathFor($"{stem} ({i})"))) i++;
                stem = $"{stem} ({i})";
            }

            var path = PathFor(stem);
            File.WriteAllText(path, JsonSerializer.Serialize(new Root { customization = data },
                new JsonSerializerOptions { WriteIndented = true }));
            return path;
        }
        catch (Exception e)
        {
            error = e.Message;
            return null;
        }
    }

    public static bool Rename(string oldPath, string requestedName, out string error)
    {
        error = null;
        var stem = Sanitize(requestedName);
        if (stem.Length == 0) { error = "Name can't be empty."; return false; }

        var newPath = PathFor(stem);
        if (newPath == oldPath) return true;

        var onlyCaseChange = string.Equals(newPath, oldPath, StringComparison.OrdinalIgnoreCase);
        if (!onlyCaseChange && File.Exists(newPath)) { error = $"'{stem}' already exists."; return false; }

        try
        {
            var oldImage = FindImage(oldPath);
            File.Move(oldPath, newPath);

            // Best effort: the picture follows the outfit's new name.
            try
            {
                if (oldImage != null)
                {
                    var newImage = System.IO.Path.ChangeExtension(newPath, System.IO.Path.GetExtension(oldImage));
                    if (!File.Exists(newImage)) File.Move(oldImage, newImage);
                }
            }
            catch (Exception) { /* picture stays under the old name; the outfit itself renamed fine */ }

            return true;
        }
        catch (Exception e) { error = e.Message; return false; }
    }

    // Moves to Outfits/_deleted instead of deleting, so a misclick is recoverable.
    public static bool Remove(string path, out string error)
    {
        error = null;
        try
        {
            var trash = System.IO.Path.Combine(Folder, "_deleted");
            Directory.CreateDirectory(trash);
            var target = System.IO.Path.Combine(trash, System.IO.Path.GetFileName(path));
            if (File.Exists(target)) File.Delete(target);
            File.Move(path, target);

            // Best effort: the picture goes to _deleted with it.
            try
            {
                var image = FindImage(path);
                if (image != null)
                {
                    var imageTarget = System.IO.Path.Combine(trash, System.IO.Path.GetFileName(image));
                    if (File.Exists(imageTarget)) File.Delete(imageTarget);
                    File.Move(image, imageTarget);
                }
            }
            catch (Exception) { /* leave the picture where it is */ }

            return true;
        }
        catch (Exception e) { error = e.Message; return false; }
    }
}

// Remembered between lobbies and game restarts (StickyColors keeps whatever you last wore,
// so without this the "look from before I started switching" would be lost on every relobby).
public class PickerState
{
    public Customization baseline { get; set; }      // your look before you started switching
    public Customization lastApplied { get; set; }   // the last outfit this mod put on you
}

public static class OutfitState
{
    public static PickerState Current = new();

    private static string StatePath => System.IO.Path.Combine(OutfitStore.Folder, "_picker_state.json");

    public static void Load()
    {
        try
        {
            if (File.Exists(StatePath))
                Current = JsonSerializer.Deserialize<PickerState>(File.ReadAllText(StatePath)) ?? new PickerState();
        }
        catch (Exception e)
        {
            Current = new PickerState();
            OutfitPickerPlugin.Instance.Log.LogWarning($"OutfitPicker: couldn't read saved state, starting fresh. {e.Message}");
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(OutfitStore.Folder);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            OutfitPickerPlugin.Instance.Log.LogWarning($"OutfitPicker: couldn't save state. {e.Message}");
        }
    }
}
