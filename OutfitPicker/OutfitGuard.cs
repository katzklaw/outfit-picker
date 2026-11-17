using System;
using System.Collections.Generic;
using UnityEngine;

namespace OutfitPicker;

// Fixes "I picked an outfit, then the host loaded in and it changed back".
//
// A non-host client only ASKS the host for its look (CmdCheckColor + the Rpc calls). If the
// host's side isn't fully loaded yet, or reloads its copy of you when it appears, the host's
// older copy of your outfit lands on top of what you just picked.
//
// What this does: for HoldSeconds after (a) you wear something, (b) you enter a lobby, or
// (c) the number of players in the lobby changes (someone - e.g. the host - loading in),
// it compares what you're actually wearing with what YOU last chose, and if something else
// changed it, re-applies your choice (after a short settle time, a few tries at most).
//
// When that window is closed it does nothing except keep up with whatever you're wearing, so
// it never fights changes made by other means (e.g. a /color command) outside the window.
public static class OutfitGuard
{
    private const float TickEvery = 0.25f;
    private const float Settle = 1.2f;         // look must stay "wrong" this long before we act
    private const float RetrySpacing = 2f;
    private const int MaxFixesPerWindow = 6;

    private static Customization _intended;    // colorID -1 / null piece = unknown, not guarded
    private static float _holdUntil;
    private static float _nextTick;
    private static float _driftSince = -1f;
    private static string _driftKey;
    private static float _lastFixAt = -100f;
    private static int _fixes;
    private static bool _capWarned;
    private static int _lastCount = -1;
    private static bool _wasLobby;
    private static float _goneSince = -1f;

    private static float HoldSeconds => OutfitPickerPlugin.HoldSeconds.Value;
    private static bool Armed => Time.realtimeSinceStartup < _holdUntil;

    private static void Info(string m) => OutfitPickerPlugin.Instance.Log.LogInfo("OutfitPicker: " + m);
    private static void Warn(string m) => OutfitPickerPlugin.Instance.Log.LogWarning("OutfitPicker: " + m);

    // ---- called by the rest of the mod

    // The picker is putting this outfit on you.
    public static void Intend(Customization o)
    {
        if (o == null) return;
        _intended = o.Clone();
        Arm("you wore an outfit");
    }

    // Your own Rpc calls (normal menu, StickyColors' reclaim, us) keep the "last chosen" up to date.
    public static void NoteColor(int id) { if (OutfitApplier.CanApply) Ensure().colorID = id; }
    public static void NoteHat(string id) { if (OutfitApplier.CanApply) Ensure().hat = id ?? ""; }
    public static void NotePet(string id) { if (OutfitApplier.CanApply) Ensure().pet = id ?? ""; }
    public static void NoteSkin(string id) { if (OutfitApplier.CanApply) Ensure().skin = id ?? ""; }
    public static void NoteVisor(string id) { if (OutfitApplier.CanApply) Ensure().visor = id ?? ""; }
    public static void NotePlate(string id) { if (OutfitApplier.CanApply) Ensure().namePlate = id ?? ""; }

    private static Customization Ensure() => _intended ??= new Customization { colorID = -1 };

    private static void Arm(string why)
    {
        if (HoldSeconds <= 0f) return;

        var wasArmed = Armed;
        _holdUntil = Time.realtimeSinceStartup + HoldSeconds;
        if (!wasArmed)
        {
            _fixes = 0;
            _capWarned = false;
        }
    }

    // ---- per-frame (called from PickerUi.Update)

    public static void Tick()
    {
        var now = Time.realtimeSinceStartup;

        var lobby = OutfitApplier.CanApply;
        if (!lobby)
        {
            // Ignore brief gaps; only forget once we've really left the lobby.
            if (_goneSince < 0f) _goneSince = now;
            if (now - _goneSince > 10f)
            {
                _intended = null;
                _wasLobby = false;
                _lastCount = -1;
                _driftSince = -1f;
            }
            return;
        }
        _goneSince = -1f;

        if (!_wasLobby)
        {
            _wasLobby = true;
            _intended = null;
            Arm("entered the lobby");
        }

        if (now < _nextTick) return;
        _nextTick = now + TickEvery;

        // Someone loaded in / left (the host loading in is exactly this): hold your outfit for a while.
        var all = PlayerControl.AllPlayerControls;
        var n = all != null ? all.Count : -1;
        if (n >= 0)
        {
            if (_lastCount >= 0 && n != _lastCount)
            {
                Info($"player count {_lastCount} -> {n}; watching your outfit for {HoldSeconds:0}s.");
                Arm("player count changed");
            }
            _lastCount = n;
        }

        var cur = OutfitStore.CaptureCurrent();
        if (cur == null) return;

        // Outside the window: just keep up with reality, never push anything.
        if (!Armed)
        {
            _intended = cur.Clone();
            _driftSince = -1f;
            _driftKey = null;
            return;
        }

        if (_intended == null) return;

        var diffs = Diff(_intended, cur);
        if (diffs.Count == 0)
        {
            _driftSince = -1f;
            _driftKey = null;
            return;
        }

        // Wait until the (wrong) look has stopped changing for a moment.
        var key = $"{cur.colorID}|{cur.HatOrDefault()}|{cur.PetOrDefault()}|{cur.SkinOrDefault()}|{cur.VisorOrDefault()}|{cur.NamePlateOrDefault()}";
        if (key != _driftKey)
        {
            _driftKey = key;
            _driftSince = now;
            Info($"your {string.Join("/", diffs)} changed without you choosing it.");
            return;
        }

        if (now - _driftSince < Settle) return;
        if (now - _lastFixAt < RetrySpacing) return;

        if (_fixes >= MaxFixesPerWindow)
        {
            if (!_capWarned)
            {
                _capWarned = true;
                Warn($"gave up after {MaxFixesPerWindow} re-applies - something keeps changing your {string.Join("/", diffs)} (host-side mod or the host doesn't accept it?).");
                PickerUi.SetStatus("Something keeps changing your outfit - I stopped fighting it.");
            }
            return;
        }

        _fixes++;
        _lastFixAt = now;
        Reapply(diffs);
    }

    private static List<string> Diff(Customization want, Customization cur)
    {
        var d = new List<string>();
        if (want.colorID >= 0 && want.colorID != cur.colorID) d.Add("color");
        if (want.hat != null && Norm(want.hat, "hat_NoHat") != cur.HatOrDefault()) d.Add("hat");
        if (want.pet != null && Norm(want.pet, "pet_EmptyPet") != cur.PetOrDefault()) d.Add("pet");
        if (want.skin != null && Norm(want.skin, "skin_None") != cur.SkinOrDefault()) d.Add("skin");
        if (want.visor != null && Norm(want.visor, "visor_EmptyVisor") != cur.VisorOrDefault()) d.Add("visor");
        if (want.namePlate != null && Norm(want.namePlate, "nameplate_NoPlate") != cur.NamePlateOrDefault()) d.Add("nameplate");
        return d;
    }

    private static string Norm(string s, string def) => string.IsNullOrEmpty(s) ? def : s;

    private static void Reapply(List<string> diffs)
    {
        var p = PlayerControl.LocalPlayer;
        if (p == null || _intended == null) return;

        Info($"re-applying your {string.Join("/", diffs)} ({_fixes}/{MaxFixesPerWindow}).");

        foreach (var piece in diffs)
        {
            switch (piece)
            {
                case "color": p.CmdCheckColor((byte)_intended.colorID); break;
                case "hat": p.RpcSetHat(_intended.HatOrDefault()); break;
                case "pet": p.RpcSetPet(_intended.PetOrDefault()); break;
                case "skin": p.RpcSetSkin(_intended.SkinOrDefault()); break;
                case "visor": p.RpcSetVisor(_intended.VisorOrDefault()); break;
                case "nameplate": p.RpcSetNamePlate(_intended.NamePlateOrDefault()); break;
            }
        }

        PickerUi.SetStatus($"Your outfit was changed by something else - re-applied ({_fixes}/{MaxFixesPerWindow}).");
        MenuInfo.RefreshPreview();
    }
}
