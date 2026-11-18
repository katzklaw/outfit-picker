using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace OutfitPicker;

// Replaces OutfitChanger (do NOT run both - they'd fight over the arrow keys).
// Reads the same Outfits folder / *.amogus files, so your saved outfits carry over.
[BepInPlugin(Id, Name, Version)]
[BepInProcess("Among Us.exe")]
public class OutfitPickerPlugin : BasePlugin
{
    public const string Id = "dev.vicki.outfitpicker";
    public const string Name = "OutfitPicker";
    public const string Version = "1.2.2";

    public static OutfitPickerPlugin Instance { get; private set; } = null!;

    public static ConfigEntry<string> OutfitFolder { get; private set; } = null!;
    public static ConfigEntry<KeyCode> OpenKey { get; private set; } = null!;
    public static ConfigEntry<bool> ArrowKeysCycle { get; private set; } = null!;
    public static ConfigEntry<float> UiScale { get; private set; } = null!;
    public static ConfigEntry<float> HoldSeconds { get; private set; } = null!;
    public static ConfigEntry<bool> ShowPictures { get; private set; } = null!;
    public static ConfigEntry<int> PictureMethod { get; private set; } = null!;

    public Harmony Harmony { get; } = new Harmony(Id);

    public override void Load()
    {
        Instance = this;

        OutfitFolder = Config.Bind("General", "OutfitFolder",
            Path.GetFullPath("Outfits", Application.persistentDataPath),
            "Folder holding your *.amogus outfit files (same default as OutfitChanger). The file name is the outfit's name.");

        OpenKey = Config.Bind("Hotkeys", "OpenWindow", KeyCode.F6,
            "Key that opens/closes the outfit window (lobby only). Any UnityEngine.KeyCode name, e.g. F6, Tab, BackQuote, O.");

        ArrowKeysCycle = Config.Bind("Hotkeys", "ArrowKeysCycle", true,
            "Keep OutfitChanger's behavior: Left/Right arrows step through outfits while the cosmetics menu is open.");

        UiScale = Config.Bind("General", "UiScale", 1.0f,
            "Scales the window. Raise to 1.5 or 2 on high-resolution screens if the text is tiny.");

        ShowPictures = Config.Bind("General", "ShowPictures", true,
            "Show a picture on each outfit's tile if a .png/.jpg with the same name sits next to its .amogus file " +
            "(e.g. 'arty calm.amogus' + 'arty calm.png'). Turn off if pictures ever cause trouble.");

        PictureMethod = Config.Bind("General", "PictureMethod", 0,
            "How pictures are drawn. 0 = automatic (tries the methods below in order and moves on if one throws an error). " +
            "If pictures show up as blank boxes instead of the image, set this to 1, then 2. " +
            "1 = style background, 2 = Graphics.DrawTexture.");

        HoldSeconds = Config.Bind("General", "HoldSeconds", 20f,
            "After you wear an outfit, enter a lobby, or the player count changes (e.g. the host loading in), your outfit is " +
            "re-applied if something else changes it, for this many seconds. 0 = off. Outside this window nothing is ever re-applied " +
            "(so e.g. a /color command still works normally).");

        try { Directory.CreateDirectory(OutfitFolder.Value); }
        catch (Exception e) { Log.LogWarning($"OutfitPicker: couldn't create outfit folder. {e}"); }

        OutfitState.Load();

        // Patch each behavior independently (same approach as StickyColors) so one
        // renamed game method can't take the whole mod down.
        try
        {
            Harmony.CreateClassProcessor(typeof(MenuTrackerPatch)).Patch();
            Log.LogInfo("OutfitPicker: patched PlayerCustomizationMenu.Update tracker.");
        }
        catch (Exception e) { Log.LogError($"OutfitPicker: FAILED to patch menu tracker (preview refresh + arrow keys disabled). {e}"); }

        TryPatchIntent("CmdCheckColor", typeof(byte), nameof(IntentPatches.Color));
        TryPatchIntent("RpcSetHat", typeof(string), nameof(IntentPatches.Hat));
        TryPatchIntent("RpcSetPet", typeof(string), nameof(IntentPatches.Pet));
        TryPatchIntent("RpcSetSkin", typeof(string), nameof(IntentPatches.Skin));
        TryPatchIntent("RpcSetVisor", typeof(string), nameof(IntentPatches.Visor));
        TryPatchIntent("RpcSetNamePlate", typeof(string), nameof(IntentPatches.Plate));

        try
        {
            var modManager = AccessTools.TypeByName("ModManager");
            var lateUpdate = modManager == null ? null : AccessTools.Method(modManager, "LateUpdate");
            if (lateUpdate == null || AccessTools.Method(modManager, "ShowModStamp") == null)
            {
                Log.LogWarning("OutfitPicker: ModManager.LateUpdate/ShowModStamp not found - the mod stamp won't be shown.");
            }
            else
            {
                Harmony.Patch(lateUpdate, postfix: new HarmonyMethod(typeof(ModStampPatch), nameof(ModStampPatch.Postfix)));
                Log.LogInfo("OutfitPicker: patched ModManager.LateUpdate (mod stamp).");
            }
        }
        catch (Exception e) { Log.LogWarning($"OutfitPicker: couldn't patch the mod stamp. {e}"); }

        TryPatchPrefix("PassiveButtonManager", "Update", nameof(InputBlockPatches.BlockWhileHovering),
            "clicks on the window may also hit game buttons underneath");
        TryPatchPrefix("KeyboardJoystick", "Update", nameof(InputBlockPatches.BlockMovement),
            "typing in a name box or clicking the window may also move your character");

        try { AddComponent<PickerWindow>(); Log.LogInfo("OutfitPicker: window component created."); }
        catch (Exception e) { Log.LogError($"OutfitPicker: FAILED to create the window component - no window will appear. {e}"); }

        Log.LogInfo("OutfitPicker loaded.");
    }

    private void TryPatchIntent(string methodName, Type argType, string postfixName)
    {
        try
        {
            var method = AccessTools.Method(typeof(PlayerControl), methodName, new[] { argType });
            if (method == null)
            {
                Log.LogWarning($"OutfitPicker: PlayerControl.{methodName}({argType.Name}) not found - outfit hold may miss changes made in the normal menu.");
                return;
            }

            Harmony.Patch(method, postfix: new HarmonyMethod(typeof(IntentPatches), postfixName));
            Log.LogInfo($"OutfitPicker: watching PlayerControl.{methodName}.");
        }
        catch (Exception e)
        {
            Log.LogWarning($"OutfitPicker: couldn't watch PlayerControl.{methodName}. {e}");
        }
    }

    private void TryPatchPrefix(string typeName, string methodName, string prefixName, string consequence)
    {
        try
        {
            var type = AccessTools.TypeByName(typeName);
            var method = type == null ? null : AccessTools.Method(type, methodName);
            if (method == null)
            {
                Log.LogWarning($"OutfitPicker: {typeName}.{methodName} not found - {consequence}.");
                return;
            }

            Harmony.Patch(method, prefix: new HarmonyMethod(typeof(InputBlockPatches), prefixName));
            Log.LogInfo($"OutfitPicker: patched {typeName}.{methodName}.");
        }
        catch (Exception e)
        {
            Log.LogWarning($"OutfitPicker: couldn't patch {typeName}.{methodName} - {consequence}. {e}");
        }
    }
}
