using System;
using HarmonyLib;
using UnityEngine;

namespace OutfitPicker;

// Remembers whether the cosmetics menu is currently open (its Update runs every
// frame while it is), and lets us refresh its character preview after a swap.
public static class MenuInfo
{
    public static PlayerCustomizationMenu Active;
    public static int LastSeenFrame = -100;

    public static bool IsOpen => Time.frameCount - LastSeenFrame <= 2;

    public static void RefreshPreview()
    {
        try
        {
            if (IsOpen && Active != null)
                Active.PreviewArea.UpdateFromLocalPlayer(PlayerMaterial.MaskType.None);
        }
        catch (Exception)
        {
            // Preview is cosmetic only - never let it break an outfit swap.
        }
    }
}

[HarmonyPatch(typeof(PlayerCustomizationMenu), nameof(PlayerCustomizationMenu.Update))]
public static class MenuTrackerPatch
{
    public static void Postfix(PlayerCustomizationMenu __instance)
    {
        MenuInfo.Active = __instance;
        MenuInfo.LastSeenFrame = Time.frameCount;
    }
}

// Applied by hand in OutfitPickerPlugin.Load (types looked up by name) so that a
// renamed game class only disables that one nicety instead of breaking the build.
public static class InputBlockPatches
{
    // PassiveButtonManager.Update: skip game button handling while the mouse is over our window.
    public static bool BlockWhileHovering() => !PickerUi.MouseOverWindow;

    // KeyboardJoystick.Update: skip movement while typing in a text box or while the mouse is over our window (mouse-steering).
    public static bool BlockMovement() => !(PickerUi.Typing || PickerUi.MouseOverWindow);
}

// Postfixes on the local player's own outfit calls, so the guard always knows what YOU last chose
// (normal cosmetics menu, StickyColors' reclaim and this mod all go through these).
// Registered by hand in OutfitPickerPlugin.Load; positional __0 args avoid name-mismatch crashes.
public static class IntentPatches
{
    private static bool Mine(PlayerControl p) => p != null && p == PlayerControl.LocalPlayer;

    public static void Color(PlayerControl __instance, byte __0) { if (Mine(__instance)) OutfitGuard.NoteColor(__0); }
    public static void Hat(PlayerControl __instance, string __0) { if (Mine(__instance)) OutfitGuard.NoteHat(__0); }
    public static void Pet(PlayerControl __instance, string __0) { if (Mine(__instance)) OutfitGuard.NotePet(__0); }
    public static void Skin(PlayerControl __instance, string __0) { if (Mine(__instance)) OutfitGuard.NoteSkin(__0); }
    public static void Visor(PlayerControl __instance, string __0) { if (Mine(__instance)) OutfitGuard.NoteVisor(__0); }
    public static void Plate(PlayerControl __instance, string __0) { if (Mine(__instance)) OutfitGuard.NotePlate(__0); }
}
