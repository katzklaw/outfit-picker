using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace OutfitPicker;

// Everything the window does.
//
// This game build has parts of Unity's IMGUI compiled out ("Method unstripping failed"),
// and the error doesn't say which part. So this deliberately leans on only the most basic
// primitives - GUI.Label, a filled rectangle, and raw Event data - and builds its own
// buttons, text boxes and paging on top of them instead of using GUI.Button / TextField /
// ScrollView / GUILayout / Event.Use.
public static class PickerUi
{
    public static bool Open;
    public static bool Typing => _focusId != null;

    private const float TileH = 64f;
    private const float TileGap = 4f;

    private static Rect _rect = new(60f, 60f, 520f, 560f);
    private static bool _dragging;
    private static bool _menuWasOpen;
    private static string _saveName = "";
    private static string _status = "";
    private static string _renamePath;
    private static string _renameText = "";
    private static string _deletePath;
    private static string _focusId;          // which of our text boxes has focus ("save"/"rename"/null)
    private static int _cycleIndex = -1;
    private static int _first;               // index of the first visible tile
    private static int _reveal = -1;         // scroll so this index is visible
    private static int _fillMode;            // 0 = DrawTexture, 1 = Box, 2 = give up
    private static bool _stylesTried;
    private static GUIStyle _bold;
    private static GUIStyle _small;
    private static bool _errLogged;
    private static bool _updateLogged;
    private static bool _guiLogged;
    private static bool _drawLogged;
    private static bool _probed;
    private static int _imgMode;              // picture draw method 0/1/2 (see DrawPicture); 3 = none worked
    private static bool _imgStyleTried;
    private static GUIStyle _img;
    private static Thumbs.Item _zoomItem;     // picture under the mouse this frame (shown enlarged)
    private static string _zoomName;

    private static float Scale => Mathf.Clamp(OutfitPickerPlugin.UiScale.Value, 0.5f, 3f);

    // Screen-space hit test used by the input-blocking patches (runs outside OnGUI).
    public static bool MouseOverWindow
    {
        get
        {
            if (!Open) return false;
            var s = Scale;
            var m = Input.mousePosition;
            var screenRect = new Rect(_rect.x * s, _rect.y * s, _rect.width * s, _rect.height * s);
            return screenRect.Contains(new Vector2(m.x, Screen.height - m.y));
        }
    }

    // ---------------------------------------------------------------- logging / safety

    private static void Info(string msg) => OutfitPickerPlugin.Instance.Log.LogInfo("OutfitPicker: " + msg);
    private static void Warn(string msg) => OutfitPickerPlugin.Instance.Log.LogWarning("OutfitPicker: " + msg);

    private static void LogError(Exception e)
    {
        if (_errLogged) return;
        _errLogged = true;
        OutfitPickerPlugin.Instance.Log.LogError($"OutfitPicker UI error (logged once): {e}");
    }

    public static void SafeUpdate()
    {
        if (!_updateLogged) { _updateLogged = true; Info("Update() is running."); }
        try { Update(); }
        catch (Exception e) { LogError(e); }
    }

    public static void SafeDraw()
    {
        if (!_guiLogged) { _guiLogged = true; Info("OnGUI() is running."); }
        try { Draw(); }
        catch (Exception e) { LogError(e); }
    }

    // Logs which basic IMGUI pieces work in this game build (once, the first time the window draws).
    private static void Probe()
    {
        var ok = new List<string>();
        var bad = new List<string>();

        void T(string name, Action a)
        {
            try { a(); ok.Add(name); }
            catch (Exception ex) { bad.Add($"{name} [{ex.GetType().Name}: {ex.Message}]"); }
        }

        var off = new Rect(-1000f, -1000f, 10f, 10f);
        T("Event.type", () => { var v = Event.current.type; });
        T("Event.mousePosition", () => { var v = Event.current.mousePosition; });
        T("Event.delta", () => { var v = Event.current.delta; });
        T("Event.button", () => { var v = Event.current.button; });
        T("Event.keyCode", () => { var v = Event.current.keyCode; });
        T("Event.character", () => { var v = Event.current.character; });
        T("Rect.Contains", () => { var v = off.Contains(new Vector2(0f, 0f)); });
        T("GUI.color", () => { var c = GUI.color; GUI.color = c; });
        T("GUI.matrix", () => { var m = GUI.matrix; GUI.matrix = m; });
        T("Texture2D.whiteTexture", () => { var v = Texture2D.whiteTexture; });
        T("GUI.DrawTexture", () => GUI.DrawTexture(off, Texture2D.whiteTexture));
        T("GUI.Box", () => GUI.Box(off, ""));
        T("GUI.Label", () => GUI.Label(off, "x"));
        T("GUI.skin.label", () => { var v = GUI.skin.label; });
        T("GUIStyle copy+props", () => { var v = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, fontStyle = FontStyle.Bold }; });
        T("GUI.Label(style)", () => GUI.Label(off, "x", GUI.skin.label));
        T("GUI.Button", () => GUI.Button(off, "x"));
        T("GUI.TextField", () => GUI.TextField(off, "x", 10));
        T("picture method 0: GUIContent.image + GUI.Label", () => GUI.Label(off, MakeContent(Texture2D.whiteTexture), ImgStyle()));
        T("picture method 1: style background + GUI.Box", () => GUI.Box(off, "", MakeBgStyle(Texture2D.whiteTexture)));
        T("picture method 2: Graphics.DrawTexture exists", () =>
        {
            if (typeof(Graphics).GetMethod("DrawTexture", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Rect), typeof(Texture) }, null) == null)
                throw new NotSupportedException("not found");
        });
        T("ImageConversion.LoadImage", () =>
        {
            // 1x1 PNG
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var ok2 = ImageConversion.LoadImage(t, png, false);
            UnityEngine.Object.Destroy(t);
            if (!ok2) throw new Exception("LoadImage returned false");
        });

        Info("IMGUI probe OK: " + string.Join(", ", ok));
        if (bad.Count > 0) Warn("IMGUI probe FAILED: " + string.Join(" | ", bad));
    }

    // ---------------------------------------------------------------- per-frame logic

    public static void SetStatus(string text) => _status = text;

    public static void Update()
    {
        OutfitGuard.Tick();

        var pressed = !Typing && Input.GetKeyDown(OutfitPickerPlugin.OpenKey.Value);
        if (pressed)
        {
            Info($"{OutfitPickerPlugin.OpenKey.Value} pressed. CanApply={OutfitApplier.CanApply} " +
                 $"(localPlayer={PlayerControl.LocalPlayer != null}, lobby={LobbyBehaviour.Instance != null}).");
        }

        if (!OutfitApplier.CanApply)
        {
            Open = false;
            _focusId = null;
            _menuWasOpen = false;
            return;
        }

        if (pressed)
        {
            Open = !Open;
            Info(Open ? "Window opened." : "Window closed.");
            if (Open)
            {
                OutfitStore.Reload();
                _status = "";
            }
            else
            {
                _focusId = null;
            }
        }

        var menuOpen = MenuInfo.IsOpen;
        if (menuOpen && !_menuWasOpen) OutfitStore.Reload();
        _menuWasOpen = menuOpen;

        if (OutfitPickerPlugin.ArrowKeysCycle.Value && menuOpen && !Typing)
        {
            if (Input.GetKeyDown(KeyCode.RightArrow)) Cycle(+1);
            else if (Input.GetKeyDown(KeyCode.LeftArrow)) Cycle(-1);
        }
    }

    private static void Cycle(int dir)
    {
        var list = OutfitStore.Entries;
        if (list.Count == 0) return;

        if (_cycleIndex < 0 || _cycleIndex >= list.Count)
        {
            var current = OutfitStore.CaptureCurrent();
            var worn = list.FindIndex(e => e.Data.SameLookAs(current));
            _cycleIndex = worn >= 0 ? worn : (dir > 0 ? -1 : 0);
        }

        _cycleIndex = (_cycleIndex + dir + list.Count) % list.Count;
        Wear(list[_cycleIndex]);
    }

    private static void Wear(OutfitEntry entry)
    {
        Info($"Wearing '{entry.Name}'.");
        OutfitApplier.Wear(entry.Data);
        _cycleIndex = OutfitStore.Entries.IndexOf(entry);
        _reveal = _cycleIndex;
        _status = $"Wearing '{entry.Name}'.";
    }

    // ---------------------------------------------------------------- drawing

    public static void Draw()
    {
        if (!Open || !OutfitApplier.CanApply)
        {
            _focusId = null;
            return;
        }

        if (!_drawLogged) { _drawLogged = true; Info("Drawing the window for the first time."); }
        if (!_probed) { _probed = true; _imgMode = Mathf.Clamp(OutfitPickerPlugin.PictureMethod.Value, 0, 2); Probe(); }

        EnsureStyles();

        var s = Scale;
        if (Math.Abs(s - 1f) < 0.001f)
        {
            DrawWindow(s);
            return;
        }

        var oldMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        try { DrawWindow(s); }
        finally { GUI.matrix = oldMatrix; }
    }

    private static void EnsureStyles()
    {
        if (_stylesTried) return;
        _stylesTried = true;
        try
        {
            _bold = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = false };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        }
        catch (Exception ex)
        {
            _bold = null;
            _small = null;
            Warn($"Custom label styles unavailable, using the default style. {ex.Message}");
        }
    }

    private static void DrawWindow(float s)
    {
        var e = Event.current;

        _zoomItem = null;
        _zoomName = null;

        _rect.height = Mathf.Min(560f, Screen.height / s - 20f);
        _rect.x = Mathf.Clamp(_rect.x, 0f, Mathf.Max(0f, Screen.width / s - _rect.width));
        _rect.y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, Screen.height / s - _rect.height));

        // Any click drops text focus; a text box that was clicked claims it back below.
        if (e.type == EventType.MouseDown) _focusId = null;

        var x = _rect.x + 8f;
        var w = _rect.width - 16f;
        var y = _rect.y + 32f;
        var listTop = y + 74f;
        var listBottom = _rect.yMax - 36f;
        var rowH = TileH + TileGap;
        var visible = Mathf.Max(1, (int)((listBottom - listTop) / rowH));
        var entries = OutfitStore.Entries;
        var maxFirst = Mathf.Max(0, entries.Count - visible);

        if (_reveal >= 0 && _reveal < entries.Count)
        {
            if (_reveal < _first) _first = _reveal;
            else if (_reveal >= _first + visible) _first = _reveal - visible + 1;
        }
        _reveal = -1;

        if (e.type == EventType.ScrollWheel && new Rect(x, listTop, w, listBottom - listTop).Contains(e.mousePosition))
            _first += e.delta.y > 0f ? 1 : -1;
        _first = Mathf.Clamp(_first, 0, maxFirst);

        Fill(_rect, new Color(0.09f, 0.09f, 0.11f, 0.98f));

        // Title bar: drag to move.
        var title = new Rect(_rect.x, _rect.y, _rect.width, 26f);
        if (e.type == EventType.MouseDown && title.Contains(e.mousePosition) && e.mousePosition.x < _rect.xMax - 32f)
            _dragging = true;
        else if (e.type == EventType.MouseDrag && _dragging)
        {
            _rect.x += e.delta.x;
            _rect.y += e.delta.y;
        }
        else if (e.type == EventType.MouseUp)
            _dragging = false;

        var range = entries.Count == 0
            ? "none saved"
            : $"{_first + 1}-{Math.Min(entries.Count, _first + visible)} of {entries.Count}";
        Lbl(title, $"  Outfit Picker   ({range})", _bold);
        if (Btn(new Rect(_rect.xMax - 28f, _rect.y + 3f, 24f, 20f), "X"))
        {
            Open = false;
            _focusId = null;
            return;
        }

        // Save-current row.
        Lbl(new Rect(x, y + 3f, 98f, 22f), "Save current as:", null);
        var enterSave = TextInput(new Rect(x + 100f, y, w - 100f - 128f, 24f), "save", ref _saveName, 40);
        if (Btn(new Rect(x + w - 124f, y, 58f, 24f), "Save") || enterSave)
        {
            SaveCurrent();
            _focusId = null;
        }

        if (Btn(new Rect(x + w - 62f, y, 62f, 24f), "Reload"))
        {
            OutfitStore.Reload();
            _deletePath = null;
            _status = $"Reloaded - {OutfitStore.Entries.Count} outfits.";
        }

        // Unequip row: back to the look you had before you started switching.
        var baseline = OutfitState.Current.baseline;
        var y2 = y + 32f;
        if (Btn(new Rect(x, y2, 150f, 30f), "Unequip (default)")) Unequip();
        if (Btn(new Rect(x + w - 62f, y2, 62f, 30f), "Folder")) OpenFolder();
        Lbl(new Rect(x + 158f, y2 - 1f, w - 158f - 68f, 34f),
            baseline == null
                ? "No default saved yet - your current look is remembered the first time you wear an outfit from here."
                : "Back to: " + Brief(baseline),
            _small);

        // Outfit tiles (whole tiles only, paged - no scroll view, so no clipping needed).
        if (entries.Count == 0)
        {
            Lbl(new Rect(x + 4f, listTop + 4f, w - 8f, 80f),
                "No outfits yet. Put on a look you like and press Save, or drop *.amogus files into your Outfits folder and press Reload.",
                _small);
        }
        else
        {
            var current = OutfitStore.CaptureCurrent();
            var last = Mathf.Min(entries.Count, _first + visible);
            for (var i = _first; i < last; i++)
            {
                var tile = new Rect(x, listTop + (i - _first) * rowH, w, TileH);
                if (DrawTile(tile, entries[i], current))
                    break; // list changed (rename/delete) - redraw next frame
            }
        }

        // Bottom bar: status + paging (mouse wheel over the list works too).
        var barY = _rect.yMax - 30f;
        Lbl(new Rect(x, barY, w - 156f, 24f), _status, _small);
        if (Btn(new Rect(x + w - 150f, barY, 72f, 24f), "< Prev")) _first = Mathf.Max(0, _first - visible);
        if (Btn(new Rect(x + w - 74f, barY, 72f, 24f), "Next >")) _first = Mathf.Min(maxFirst, _first + visible);

        DrawZoom(s);
    }

    // Returns true if the outfit list changed.
    private static bool DrawTile(Rect r, OutfitEntry entry, Customization current)
    {
        var worn = current != null && entry.Data.SameLookAs(current);
        Fill(r, worn ? new Color(0.25f, 0.60f, 0.35f, 0.45f) : new Color(1f, 1f, 1f, 0.07f));

        // Picture (arty calm.png next to arty calm.amogus) or, without one, the flat crewmate.
        var slot = new Rect(r.x + 4f, r.y + 2f, 60f, 60f);
        var item = OutfitPickerPlugin.ShowPictures.Value ? Thumbs.Get(entry) : null;
        if (item != null && DrawPicture(slot, item))
        {
            if (slot.Contains(Event.current.mousePosition))
            {
                _zoomItem = item;
                _zoomName = entry.Name;
            }
        }
        else
        {
            DrawMini(new Rect(r.x + 8f, r.y + 4f, 44f, 56f), BodyColor(entry.Data.colorID));
        }

        const float btnW = 130f;
        var textX = r.x + 68f;
        var textW = r.width - 68f - btnW - 8f;
        var bx = r.xMax - btnW - 4f;
        var renaming = _renamePath == entry.Path;
        var enterRename = false;

        if (renaming)
            enterRename = TextInput(new Rect(textX, r.y + 4f, textW, 22f), "rename", ref _renameText, 40);
        else
            Lbl(new Rect(textX, r.y + 2f, textW, 22f), entry.Name + (worn ? "  (wearing)" : ""), _bold);

        Lbl(new Rect(textX, r.y + 24f, textW, 38f), Details(entry.Data), _small);

        if (renaming)
        {
            if (Btn(new Rect(bx, r.y + 4f, btnW, 26f), "OK") || enterRename)
            {
                if (OutfitStore.Rename(entry.Path, _renameText, out var err))
                {
                    _status = $"Renamed to '{OutfitStore.Sanitize(_renameText)}'.";
                    _renamePath = null;
                    _focusId = null;
                    OutfitStore.Reload();
                    return true;
                }
                _status = "Couldn't rename: " + err;
            }

            if (Btn(new Rect(bx, r.y + 34f, btnW, 24f), "Cancel"))
            {
                _renamePath = null;
                _focusId = null;
            }
            return false;
        }

        if (Btn(new Rect(bx, r.y + 4f, btnW, 26f), "Wear"))
        {
            _deletePath = null;
            _focusId = null;
            Wear(entry);
        }

        if (Btn(new Rect(bx, r.y + 34f, 62f, 24f), "Rename"))
        {
            _deletePath = null;
            _renamePath = entry.Path;
            _renameText = entry.Name;
            _focusId = "rename";
        }

        if (Btn(new Rect(bx + 66f, r.y + 34f, 64f, 24f), _deletePath == entry.Path ? "Sure?" : "Delete"))
        {
            if (_deletePath == entry.Path)
            {
                _deletePath = null;
                if (OutfitStore.Remove(entry.Path, out var err))
                {
                    _status = $"Removed '{entry.Name}' (moved to Outfits/_deleted).";
                    OutfitStore.Reload();
                    _cycleIndex = -1;
                    return true;
                }
                _status = "Couldn't remove: " + err;
            }
            else
            {
                _deletePath = entry.Path;
                _status = "Click Delete again to confirm (the file is moved to Outfits/_deleted, not erased).";
            }
        }

        return false;
    }

    private static void Unequip()
    {
        _deletePath = null;
        _focusId = null;
        Info("Unequip pressed.");
        OutfitApplier.Unequip(out var msg);
        _cycleIndex = -1;
        _status = msg;
    }

    private static string Brief(Customization c) =>
        $"color {c.colorID} / hat {Pretty(c.HatOrDefault())} / skin {Pretty(c.SkinOrDefault())} / visor {Pretty(c.VisorOrDefault())}";

    private static void SaveCurrent()
    {
        var path = OutfitStore.SaveNew(_saveName, OutfitStore.CaptureCurrent(), out var err);
        if (path == null)
        {
            _status = "Couldn't save: " + err;
            return;
        }

        var savedAs = Path.GetFileNameWithoutExtension(path);
        _status = $"Saved as '{savedAs}'. For a picture, put '{savedAs}.png' in the Outfits folder (Folder button).";
        _saveName = "";
        OutfitStore.Reload();
        _reveal = OutfitStore.Entries.FindIndex(en => en.Path == path);
    }

    // ---------------------------------------------------------------- pictures

    // Pictures are drawn without GUI.DrawTexture (stripped in this game build) and without the
    // GUIContent(Texture) constructor (not present at all). Instead there are three methods, tried in
    // order; each is built from calls that are looked up by name at runtime, so a missing one
    // just logs and moves on to the next instead of breaking the build:
    //   0 = a label whose GUIContent has its .image set
    //   1 = a box whose style has the picture as its background
    //   2 = Graphics.DrawTexture
    private static GUIStyle ImgStyle()
    {
        if (_img == null && !_imgStyleTried)
        {
            _imgStyleTried = true;
            try { _img = new GUIStyle(GUI.skin.label) { imagePosition = ImagePosition.ImageOnly, alignment = TextAnchor.MiddleCenter }; }
            catch (Exception ex) { _img = null; Warn($"couldn't make the picture style, using the default. {ex.Message}"); }
        }
        return _img ?? GUI.skin.label;
    }

    private static GUIContent MakeContent(Texture2D tex)
    {
        var c = new GUIContent(""); // the string constructor is the one this game build is known to have
        var p = typeof(GUIContent).GetProperty("image", BindingFlags.Public | BindingFlags.Instance);
        if (p == null || !p.CanWrite) throw new NotSupportedException("GUIContent.image isn't available");
        p.SetValue(c, tex);
        return c;
    }

    private static GUIStyle MakeBgStyle(Texture2D tex)
    {
        var style = new GUIStyle(GUI.skin.label);
        var normalProp = typeof(GUIStyle).GetProperty("normal", BindingFlags.Public | BindingFlags.Instance);
        var state = normalProp?.GetValue(style);
        var bgProp = state?.GetType().GetProperty("background", BindingFlags.Public | BindingFlags.Instance);
        if (bgProp == null || !bgProp.CanWrite) throw new NotSupportedException("GUIStyleState.background isn't available");
        bgProp.SetValue(state, tex);
        return style;
    }

    private static MethodInfo _drawTexture;

    private static void DrawViaGraphics(Rect r, Texture2D tex)
    {
        _drawTexture ??= typeof(Graphics).GetMethod("DrawTexture", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Rect), typeof(Texture) }, null);
        if (_drawTexture == null) throw new NotSupportedException("Graphics.DrawTexture isn't available");

        // Graphics.DrawTexture works in raw screen pixels and ignores GUI.matrix, so apply the UI scale by hand.
        var k = Scale;
        var screen = new Rect(r.x * k, r.y * k, r.width * k, r.height * k);
        try { _drawTexture.Invoke(null, new object[] { screen, tex }); }
        catch (TargetInvocationException tie) { throw tie.InnerException ?? tie; }
    }

    // Draws a picture fitted (aspect preserved) and centered inside the slot.
    private static bool DrawPicture(Rect slot, Thumbs.Item item)
    {
        if (item == null || item.Tex == null) return false;

        float tw = item.Tex.width, th = item.Tex.height;
        if (tw < 1f || th < 1f) return false;

        var k = Mathf.Min(slot.width / tw, slot.height / th);
        var w = tw * k;
        var h = th * k;
        var r = new Rect(slot.x + (slot.width - w) / 2f, slot.y + (slot.height - h) / 2f, w, h);

        while (_imgMode < 3)
        {
            try
            {
                switch (_imgMode)
                {
                    case 0:
                        item.Content ??= MakeContent(item.Tex);
                        GUI.Label(r, item.Content, ImgStyle());
                        return true;
                    case 1:
                        item.BgStyle ??= MakeBgStyle(item.Tex);
                        GUI.Box(r, "", item.BgStyle);
                        return true;
                    default:
                        if (Event.current.type == EventType.Repaint) DrawViaGraphics(r, item.Tex);
                        return true;
                }
            }
            catch (Exception ex)
            {
                Warn($"picture method {_imgMode} failed ({ex.InnerException?.Message ?? ex.Message}); trying the next one.");
                _imgMode++;
            }
        }

        return false;
    }

    // Big version of the picture under the mouse, shown beside the window.
    private static void DrawZoom(float s)
    {
        if (_zoomItem == null) return;

        const float box = 260f;
        var sw = Screen.width / s;
        var sh = Screen.height / s;

        var x = _rect.xMax + 8f;
        if (x + box + 12f > sw) x = _rect.x - box - 20f;           // no room on the right -> left side
        if (x < 0f) x = Mathf.Max(0f, _rect.xMax - box - 20f);     // no room at all -> overlay the window's edge
        var y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, sh - box - 40f));

        Fill(new Rect(x, y, box + 12f, box + 38f), new Color(0.09f, 0.09f, 0.11f, 0.98f));
        DrawPicture(new Rect(x + 6f, y + 6f, box, box), _zoomItem);
        Lbl(new Rect(x + 6f, y + box + 10f, box, 24f), _zoomName, _bold);
    }

    private static void OpenFolder()
    {
        var folder = OutfitStore.Folder;
        try
        {
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            _status = "Opened the Outfits folder. Press Reload after adding pictures.";
        }
        catch (Exception ex)
        {
            try
            {
                Application.OpenURL("file:///" + folder.Replace('\\', '/'));
                _status = "Opened the Outfits folder. Press Reload after adding pictures.";
            }
            catch (Exception)
            {
                _status = "Couldn't open the folder: " + ex.Message;
            }
        }
    }

    // ---------------------------------------------------------------- hand-made widgets

    // Button: drawn as a rectangle + label; click = left mouse DOWN over it.
    private static bool Btn(Rect r, string text)
    {
        var e = Event.current;
        var hover = r.Contains(e.mousePosition);
        Fill(r, hover ? new Color(0.36f, 0.38f, 0.50f, 1f) : new Color(0.24f, 0.25f, 0.33f, 1f));

        // Roughly center the text (wide rect so the default label never wraps).
        var tx = r.x + Mathf.Max(2f, (r.width - text.Length * 7f) / 2f);
        Lbl(new Rect(tx, r.y + (r.height - 20f) / 2f, r.width * 2f, 22f), text, null);

        return e.type == EventType.MouseDown && e.button == 0 && hover;
    }

    // Single-line text box built from raw key events. Returns true when Enter was pressed in it.
    private static bool TextInput(Rect r, string id, ref string text, int max)
    {
        var e = Event.current;
        var entered = false;

        if (e.type == EventType.MouseDown && r.Contains(e.mousePosition)) _focusId = id;
        var focused = _focusId == id;

        if (focused && e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Backspace)
            {
                if (text.Length > 0) text = text.Substring(0, text.Length - 1);
            }
            else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                entered = true;
            }
            else if (e.keyCode == KeyCode.Escape)
            {
                _focusId = null;
            }
            else
            {
                var ch = e.character;
                if (ch != '\0' && !char.IsControl(ch) && text.Length < max) text += ch;
            }
        }

        Fill(r, focused ? new Color(0.22f, 0.22f, 0.30f, 1f) : new Color(0.16f, 0.16f, 0.19f, 1f));
        var caret = focused && ((int)(Time.realtimeSinceStartup * 2f) % 2 == 0) ? "|" : "";
        Lbl(new Rect(r.x + 4f, r.y + 2f, r.width - 8f, r.height - 4f), text + caret, null);

        return entered;
    }

    // ---------------------------------------------------------------- drawing helpers

    private static void Lbl(Rect r, string text, GUIStyle style)
    {
        if (style != null) GUI.Label(r, text, style);
        else GUI.Label(r, text);
    }

    private static void Fill(Rect r, Color c)
    {
        if (_fillMode >= 2) return;

        var old = GUI.color;
        try
        {
            GUI.color = c;
            if (_fillMode == 0) GUI.DrawTexture(r, Texture2D.whiteTexture);
            else GUI.Box(r, "");
        }
        catch (Exception ex)
        {
            Warn($"Fill mode {_fillMode} failed ({ex.GetType().Name}: {ex.Message}); falling back.");
            _fillMode++;
        }
        finally
        {
            try { GUI.color = old; } catch (Exception) { /* nothing more to do */ }
        }
    }

    private static Color BodyColor(int id)
    {
        var colors = Palette.PlayerColors;
        if (colors == null || id < 0 || id >= colors.Length) return Color.gray;
        Color c = colors[id];
        return c;
    }

    // A tiny flat-colored crewmate: backpack, body, legs, visor.
    private static void DrawMini(Rect r, Color body)
    {
        var dark = new Color(body.r * 0.7f, body.g * 0.7f, body.b * 0.7f, 1f);
        Fill(new Rect(r.x + 2f, r.y + 16f, 8f, 26f), dark);                     // backpack
        Fill(new Rect(r.x + 8f, r.y + 2f, 30f, 44f), body);                     // body
        Fill(new Rect(r.x + 8f, r.y + 44f, 12f, 10f), body);                    // left leg
        Fill(new Rect(r.x + 26f, r.y + 44f, 12f, 10f), body);                   // right leg
        Fill(new Rect(r.x + 16f, r.y + 10f, 20f, 13f), new Color(0.62f, 0.86f, 0.93f, 1f)); // visor
    }

    private static string Details(Customization c) =>
        $"hat: {Pretty(c.HatOrDefault())}   pet: {Pretty(c.PetOrDefault())}\n" +
        $"skin: {Pretty(c.SkinOrDefault())}   visor: {Pretty(c.VisorOrDefault())}   plate: {Pretty(c.NamePlateOrDefault())}";

    // "hat_pk05_Cheese" -> "Cheese"; the various "nothing" ids -> "-".
    private static string Pretty(string id)
    {
        if (string.IsNullOrEmpty(id)) return "-";
        var i = id.IndexOf('_');
        var s = i >= 0 ? id.Substring(i + 1) : id;

        if (s is "NoHat" or "EmptyPet" or "None" or "EmptyVisor" or "NoPlate") return "-";

        var j = s.IndexOf('_');
        if (j > 0 && j <= 6 && s.Substring(0, j).Any(char.IsDigit)) s = s.Substring(j + 1);

        return s.Length == 0 ? "-" : s;
    }
}
