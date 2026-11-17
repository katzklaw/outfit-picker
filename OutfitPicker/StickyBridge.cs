using System;
using System.Reflection;
using BepInEx.Configuration;

namespace OutfitPicker;

// StickyColors restores its own "last saved" outfit at several moments (opening the customize
// panel, entering a lobby, and - the one that bites - when a round starts). It only updates that
// saved outfit while its internal "in lobby" switch is on, and a log showed that switch never
// turning on in a joined lobby, so the saved outfit stayed stale and got put back on you at
// load-in. So whenever this mod puts an outfit on you, it also writes it straight into
// StickyColors' saved values. Found by reflection, so there's no dependency on that DLL:
// if StickyColors isn't installed this quietly does nothing.
public static class StickyBridge
{
    private static bool _resolved;
    private static Type _sticky;
    private static bool _warned;

    private static void Info(string m) => OutfitPickerPlugin.Instance.Log.LogInfo("OutfitPicker: " + m);
    private static void Warn(string m) => OutfitPickerPlugin.Instance.Log.LogWarning("OutfitPicker: " + m);

    public static void Push(Customization o)
    {
        if (o == null) return;

        try
        {
            if (!_resolved)
            {
                _resolved = true;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name != "StickyColors") continue;
                    _sticky = a.GetType("StickyColors.StickyColorsPlugin");
                    break;
                }

                Info(_sticky == null
                    ? "StickyColors not found - not syncing outfits to it."
                    : "StickyColors found - outfits you wear here are saved into it too.");
            }

            if (_sticky == null) return;

            var ok = SetInt("SavedColorId", o.colorID)
                     & SetStr("SavedHat", o.HatOrDefault())
                     & SetStr("SavedPet", o.PetOrDefault())
                     & SetStr("SavedSkin", o.SkinOrDefault())
                     & SetStr("SavedVisor", o.VisorOrDefault())
                     & SetStr("SavedNamePlate", o.NamePlateOrDefault());

            if (ok) Info($"told StickyColors to remember this outfit (color {o.colorID}, hat {o.HatOrDefault()}).");
        }
        catch (Exception e)
        {
            if (_warned) return;
            _warned = true;
            Warn($"couldn't sync the outfit into StickyColors. {e.Message}");
        }
    }

    private static object Entry(string name) =>
        _sticky.GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

    private static bool SetStr(string name, string value)
    {
        if (Entry(name) is not ConfigEntry<string> e) { WarnOnce(name); return false; }
        e.Value = value;
        return true;
    }

    private static bool SetInt(string name, int value)
    {
        if (Entry(name) is not ConfigEntry<int> e) { WarnOnce(name); return false; }
        e.Value = value;
        return true;
    }

    private static void WarnOnce(string name)
    {
        if (_warned) return;
        _warned = true;
        Warn($"StickyColors has no '{name}' setting I can write to - its outfit won't be synced.");
    }
}
