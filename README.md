# OutfitPicker

An in-game window for picking saved Among Us outfits, built on the same `*.amogus`
files as whichtwix's OutfitChanger. **Replaces OutfitChanger - don't run both.**

https://github.com/user-attachments/assets/0b9b198d-89ee-4d9b-82f8-e13b066d1772

## Use
- **F6** (configurable) in the lobby opens/closes the window. Drag the title bar to move it.
- **Wear** applies an outfit. **Rename** renames the file (the file name is the outfit name).
  **Delete** moves the file to `Outfits/_deleted` (recoverable).
- **Save current as:** type a name (or leave blank for `outfitN`) and press Save/Enter to store
  what you're wearing right now. No more hand-making files.
- Tiles show a flat-colored crewmate in the outfit's color, plus hat / pet / skin / visor / plate.
  The outfit you're currently wearing is highlighted.
- **Unequip (default)** puts you back in the look you had before you started switching. That look is
  remembered the first time you wear an outfit from the window (and survives relobbies / restarts, in
  `Outfits/_picker_state.json`). If you later change your look in the normal menu and then wear an
  outfit again, that new look becomes the default.
- **Pictures (optional):** put a screenshot next to an outfit file with the same name -
  `Outfits/arty calm.amogus` + `Outfits/arty calm.png` (`.jpg`/`.jpeg` also work). It shows on that
  outfit's tile, and hovering it shows a bigger copy beside the window. No picture = the flat
  crewmate, as before. Tip: in the lobby wear the outfit, grab the character with Win+Shift+S, crop a
  roughly square ~256px area. The **Folder** button opens the Outfits folder; **Reload** (or reopening the
  window) picks up new pictures. Rename/Delete move the picture along with the outfit. Big screenshots
  work but cost memory (the log warns above 512px). Config: `ShowPictures`, `PictureMethod`
  (0 = automatic; if pictures show as blank boxes try 1, then 2).
- The list shows a page of tiles at a time: mouse wheel over the list, or the Prev/Next buttons.
- Left/Right arrows still cycle outfits while the cosmetics menu is open (`ArrowKeysCycle`).

You can generate configs, and grab screenshots, either in game, or with [Crewmate Closet](https://katzklaw.github.io/artysus/CrewmateCloset.html). 
The files generated from this website are in the correct format to drop directly into the outfits folder and use as is.

## Config (`BepInEx/config/dev.vicki.outfitpicker.cfg`)
`OutfitFolder`, `OpenWindow` (any UnityEngine KeyCode), `ArrowKeysCycle`,
`UiScale`, `HoldSeconds`, `ShowPictures`, `PictureMethod`.

Outfits never change your player name (the game kicks for that). The `name` stored
inside the files is for informational purposes only and is simply ignored.

## Outfit hold (non-host fix)
For `HoldSeconds` (default 20) after you wear an outfit, enter a lobby, or the player count changes
(e.g. the host loading in), the mod compares what you're wearing with what you last chose and, if
something else changed it, re-applies your choice (settles ~1.2s, at most 6 tries, then gives up and
says so). Outside that window it never pushes anything, so `/color` etc. still behave normally.
Set `HoldSeconds = 0` to turn it off. Log lines: `OutfitPicker: your hat/color changed without you
choosing it` / `re-applying`.

## Mod stamp
Like the original OutfitChanger, this calls the game's `ShowModStamp()` after `ModManager.LateUpdate`.
Log: `patched ModManager.LateUpdate (mod stamp)`.

## StickyColors
Whenever this mod puts an outfit on you it also writes it into StickyColors' saved outfit (found by
reflection - no DLL dependency; skipped if StickyColors isn't installed), so StickyColors' round-start
restore puts back THIS outfit instead of an old one. Log: `told StickyColors to remember this outfit`.
 Wearing an outfit calls the same game methods OutfitChanger did
(`CmdCheckColor` + `RpcSetHat/Pet/Skin/Visor/NamePlate`), which StickyColors already watches.
The window only works in the lobby, matching StickyColors' `InLobby` rule.


**This mod is not affiliated with Among Us or Innersloth LLC, and the content contained therein is not endorsed or otherwise sponsored by Innersloth LLC. Portions of the materials contained herein are property of Innersloth LLC. © Innersloth LLC.**
