using UnityEngine;

namespace OutfitPicker;

public static class OutfitApplier
{
    public static bool CanApply => PlayerControl.LocalPlayer != null && LobbyBehaviour.Instance != null;

    // "Wear this outfit" (window button / arrow keys).
    // Also remembers what you were wearing BEFORE you started switching, for Unequip.
    // A new "switching session" starts when what you have on is neither something this mod
    // put on you nor your remembered default - e.g. you changed your look in the normal menu.
    public static void Wear(Customization o)
    {
        if (!CanApply || o == null) return;

        var st = OutfitState.Current;
        var cur = OutfitStore.CaptureCurrent();
        var newSession = st.baseline == null
                         || (cur != null && !cur.SameLookAs(st.lastApplied) && !cur.SameLookAs(st.baseline));
        if (newSession && cur != null) st.baseline = cur;

        st.lastApplied = o.Clone();
        OutfitState.Save();
        ApplyRaw(o);
    }

    // Back to the look you had before you started switching.
    public static bool Unequip(out string message)
    {
        var st = OutfitState.Current;
        if (!CanApply) { message = "Only works in the lobby."; return false; }
        if (st.baseline == null)
        {
            message = "No default saved yet - it's remembered the first time you wear an outfit from here.";
            return false;
        }

        ApplyRaw(st.baseline);
        st.lastApplied = null;
        OutfitState.Save();
        message = "Back to your default look.";
        return true;
    }

    // Same as OutfitChanger's SetOutfit, minus the name change (the game kicks for that now):
    // color goes through CmdCheckColor, everything else through the game's own Rpc methods.
    // That is exactly what StickyColors listens to, so a swap made here is remembered across
    // games with no extra wiring.
    private static void ApplyRaw(Customization o)
    {
        var p = PlayerControl.LocalPlayer;
        if (p == null || o == null || LobbyBehaviour.Instance == null) return;

        OutfitGuard.Intend(o);
        StickyBridge.Push(o);

        p.CmdCheckColor((byte)o.colorID);
        p.RpcSetHat(o.HatOrDefault());
        p.RpcSetPet(o.PetOrDefault());
        p.RpcSetSkin(o.SkinOrDefault());
        p.RpcSetVisor(o.VisorOrDefault());
        p.RpcSetNamePlate(o.NamePlateOrDefault());

        MenuInfo.RefreshPreview();
    }
}
