# Servo-Skull Camera Controls

A UnityModManager mod for Warhammer 40,000: Rogue Trader. It widens the camera
envelope past the base game, adds the vertical control the game lacks, and lets you
stash two camera setups on hotkeys and flip between them. It is meant to run
alongside ToyBox, not replace it.

## Requirements

Nothing extra to install to run it: Rogue Trader has UnityModManager integration built in
(Owlcat ships it with the game). The mod targets UnityModManager 0.25.0.

**Recommended (not required):**

- **WASD Movement** - the camera is built around third-person movement; with it, your character
  walks the way the camera faces. Without it the mod still works with click-to-move.
- **ToyBox** - not required to load or run, but it's what gives the *non-mouselook* views their
  middle-mouse drag-pitch (base Rogue Trader has no pitch on the drag), and the mod tucks ToyBox's
  FOV override out of the way so the extended zoom range works. Mouselook views get pitch on their
  own, so you can run without ToyBox if you stay in those.

## Compatibility

Built and tested against **Rogue Trader 1.6**. The camera is patched through Harmony, so it should
tolerate minor game updates; a major camera rewrite by Owlcat could need a new build.

## How the controls divide up

- **Yaw + pitch** come from **Mouse3 (middle-click) drag**. Important: base Rogue
  Trader has no pitch at all - only ToyBox provides it. Turn on
  *ToyBox > Camera > Enable Mouse3 Dragging To Aim The Camera* to get pitch. (Elevation,
  if you want it, is ToyBox's *Ctrl+Mouse3* option.)
- **Zoom** is the native scroll wheel, travelling within the extended range this mod sets.
- **Focus offset** is this mod's own control: a per-view world-up pivot raise plus a lateral
  shoulder shift, with **Ctrl + scroll** live-tuning the active view's pivot height.
- **Presets** are this mod's hotkeys.

## What this mod adds

- **Zoom range** - extends how far the scroll wheel zooms. Zoom-in goes up to ~10x the
  area default (the FOV floor is dropped to 2° so the high factors actually bite); zoom-out
  stays modest. To make this stick, the mod removes ToyBox's FOV override (see below).
- **Focus offset (per view)** - shifts where the camera is centred and how close it sits, and it
  now works the same in **every** mode (static views and mouselook alike):
  - **Pivot height** raises the orbit centre straight up in world space to the character's
    shoulders/head. Being world-up, it's yaw-independent, so the framing holds as you turn and
    - critically - it no longer *jumps* when mouselook toggles off (Left Shift, dialogue, an
    incidental drop). **Ctrl + scroll** live-tunes the active view's pivot height.
  - **Shoulder offset** slides the centre sideways along the camera's Right axis, so the
    character sits off to one side for a true over-the-shoulder framing instead of dead-centre.
    Because it rides Right it rotates with the camera (stays over the same shoulder as you turn),
    and it's re-applied every frame, so moving with **WASD** can't snap it back to centre. A
    per-view slider sets the amount (0 = centred; flip the sign if it leans the wrong way).
  - **Dolly** pushes the camera straight in toward the character along its own view ray, for a
    tight over-the-shoulder "zoom" without touching the lens. **Ctrl + Shift + scroll** live-tunes
    the active view's dolly; it stops just short of the model so it won't clip inside it.
  It can pause in cutscenes (toggle). For **conversations**, an **In dialogue** setting picks how
  the framing behaves: *Off* hands the camera to the game's own dialogue framing; *Lift only* keeps
  a gentle raise with no zoom; *Full tactical* keeps the over-the-shoulder zoom at your gameplay
  height while the game still frames the speakers left and right. A **Dialogue height** slider sets
  where the speaker sits in *Full tactical*, and can be dragged with a conversation open to tune it live.
- **Pitch range** - widens the angle band the Mouse3 drag is clamped to (5-89°, so the
  camera can't flip). Mouselook is not bound by this band - it is free-look and can tilt above the
  horizon (see **Mouselook on a view** below). Note: ToyBox's drag may bypass the game's clamp, in
  which case these sliders have little effect - harmless, just set them wide.
- **Near and far clip (per view)** - opt-in sliders that move the camera's clip planes, each with
  its own toggle. The **near** plane pushes outward to cull geometry close to the lens so it stops
  blocking the view at flat angles - blunt by nature, so you'll see "through" whatever it clips (into
  backfaces/void), and pushed past your party at high zoom it clips them too. The **far** plane pulls
  inward to cull distant geometry; it can interact with fog and the skybox, so it's off by default.
  Both are saved per preset and driven through the Cinemachine lens, which the game never overwrites -
  e.g. a high near clip on the flat view, normal on the tactical one.
- **Camera presets** - two saveable views. Compose one live (drag the angle, zoom, set
  near clip), press **Set View 1** (or 2) to stamp it, then **Toggle** alternates
  between them - e.g. a tight over-the-shoulder view and a wide tactical one. **Set** stamps
  pitch + zoom + near clip; each view also carries its **own pivot height and shoulder offset**
  (set with the sliders / Ctrl + scroll) and a **zoom slider**, so you can dial the saved zoom in
  directly instead of re-stamping. Facing is **not** stored, so a view works whichever
  way you're turned. Views persist.
- **Hide off-screen character markers** - optional, on by default: suppresses the party-
  portrait pointers that ride the screen edge for off-screen characters, a common side
  effect of the closer framing this mod allows. Co-op ping markers and objective/locator
  pointers are left alone. Targets the game's PointMarker system, hiding only markers bound
  to a unit.

- **Solid walls (per view)** - optional: holds off RT's occluder see-through, the effect that
  dissolves walls and doors when they come between the camera and your party. The closer, lower
  framing this mod allows makes that dissolve fire constantly; turn it on to keep the geometry solid.
  Saved per preset, so you can hold the walls solid on a tight over-the-shoulder view and let them
  dissolve on a wider one. It steps aside on the world map and during scripted camera shots, so the
  game's own framing is left untouched.

- **Mouselook on a view** - tick **mouselook** on a saved view to make it drive the camera
  like a third-person shooter: moving the mouse turns (yaw) and tilts (pitch) with no click
  and no modifier, and the cursor locks to a centre crosshair (clicks land at screen centre,
  so it doubles as a reticle). Separate **X (yaw)** and **Y (pitch)** sensitivity sliders, plus
  invert-Y, are in the panel. Mouselook is free-look - it tilts above the horizon as well as down,
  independent of the Mouse3-drag band, and WASD and edge-scroll movement stay correct whichever way
  you're looking. Pairs with WASDMovement for shooter-style movement, since WASD follows the camera facing.
  Applying a mouselook view enters the mode; applying a normal view (its hotkey works without
  the cursor) leaves it. **Hold the free-cursor key** (default Left Shift) any time for a normal
  pointer - rotation pauses while it's held. Dialogue, full-screen menus (inventory, character,
  journal, map, vendor, Esc), the global map, space, loading and the mod panel all free the
  cursor automatically and restore it when you're back in the world.
  The **focus offset** above (pivot height + shoulder) applies in mouselook exactly as it does
  in the static views, so the over-the-shoulder framing is identical whether you're mouselooking
  or not - and there's no jump when you drop in or out of it.

- **Turn speed (per view)** - each saved view carries a turn-speed multiplier (1.0 = stock, lower
  = slower) that governs the **A/D follow turn** and the keyboard rotate. With WASDMovement the A/D
  turn is the camera-follower matching the character's facing - it ramps the yaw at a steady rate
  (measured ~131°/s), with the rig's rubber-band tracking it ~3° behind. The mod takes over the
  final camera yaw (the way mouselook does) and slews it toward that target at a capped rate, scaled
  by the multiplier. Because a slower follow must fall behind a steady ramp, the camera **trails**
  during the turn and finishes settling just after you release - lower = slower = more trail.
  Mouselook and the Mouse3 drag are untouched, and a view must be active for it to bite (with no
  preset active the multiplier is 1.0).

- **Controller support** - in the game's gamepad mode the camera adapts to the pad. On **View 1** the
  right stick's vertical axis tilts the camera up and down: the mod holds and drives that pitch the way
  mouselook does with a mouse - including across dialogue and area transitions, where there is otherwise
  nothing on a pad to keep the view - while the horizontal axis turns at the stock stick speed. An
  **invert gamepad pitch** toggle flips up/down for those who prefer it, separate from the mouse invert.
  View 1 also keeps your character locked in frame rather than drifting loosely behind them. The wide
  tactical **View 2** keeps the native rotate-and-zoom and its looser follow. Because the stick now
  pitches rather than zooms on View 1, each view carries a **zoom slider** in the panel to dial the
  framing in directly. A controller button alternates the two views - **Left Bumper** by default,
  rebindable to any pad button in the panel. (The D-pad reports as an axis rather than a button, so it
  can't be bound here.) The keyboard hotkeys still work on a pad.

Scripted camera shots are always left alone - both the hard-bound cinematics and the in-dialogue cuts
a conversation triggers (the swings onto an object or a speaker that hold until you pick the next line).
Once a conversation makes one of those cuts, the focus offset and the solid-walls hold step aside for
the rest of that conversation and come back when it ends, so the authored shot frames the way it was
meant to. Ordinary dialogue with no scripted cut still follows your **In dialogue** setting.

## ToyBox interaction

> **Disable ToyBox's "Ctrl + Mouse3 Drag To Adjust Camera Elevation"** (ToyBox &rarr; Camera,
> the fourth option). With it on, the camera loads attached to a map origin instead of your
> character, and panning with the camera keys snaps to that origin rather than panning from
> where your character is. It's ToyBox's own behaviour, woven into how it composes the camera
> position, so it isn't something this mod can cleanly intercept - just turn it off. You lose
> nothing for the over-the-shoulder setup, and the **Mouse3-to-aim** option (the third one),
> which this mod relies on for pitch, is unaffected. The mod panel also flags a warning when it
> detects this option enabled.

The only thing this mod suppresses is ToyBox's **FOV multiplier**, which is a Harmony
prefix on `CameraZoom.TickZoom` that recomputes the field of view from its own constants
and ignores the `FovMin`/`FovMax` this mod sets. On load the mod strips that one foreign
prefix (the game's own `TickZoom` then runs and honours our zoom range). Everything else
ToyBox does to the camera - pitch, elevation, rotate-on-all-maps, free camera - is left
untouched, because RT has no native pitch and we depend on ToyBox for it.

Side effect: removing ToyBox's `TickZoom` prefix also disables its "zoomable local maps."
If that matters, the mod can re-add "zoom everywhere" inside its own patch.

WASDMovement is unaffected (it only reads the camera).

## Controls

| Action            | Default                                        |
|-------------------|------------------------------------------------|
| Rotate            | Mouse3 (middle-click) drag - native            |
| Pitch             | Mouse3 drag - **requires** ToyBox's Mouse3 aim option |
| Pitch (gamepad)   | Right stick Y on View 1 - invertable in the panel |
| Zoom              | Scroll wheel - native                          |
| Pivot height (focus) | Ctrl + scroll                               |
| Dolly (zoom-in, focus) | Ctrl + Shift + scroll                       |
| Set View 1        | Keypad 7                                        |
| Set View 2        | Keypad 9                                        |
| Toggle views      | Caps Lock                                       |
| Toggle views (gamepad) | Left Bumper - rebindable; works in the game's gamepad mode |
| Mouselook         | per-view toggle (applying that view enters it)  |
| Free cursor       | hold Left Shift (pauses look, restores pointer) |

All hotkeys are rebindable in the mod panel (click **Bind**, press a key; Esc cancels), the
free-cursor key included.

## Defaults

Ships seeded with a tuned setup: framing on, the pitch band at 15-89°, and the zoom range at
2.4x out / 4x in (the FOV floor is dropped so the inward factor bites). Dialogue framing is set to
*Full tactical* at height 1.3, so the over-the-shoulder shot carries into conversations and is held
at your gameplay height.

The two preset views are a close over-the-shoulder one and a wide tactical one. **View 1** is
mouselook, pitch ~37°, zoom 40%, pivot height 1.9, shoulder 0.4, dolly 25.5, near and far clip off,
solid walls on, turn speed 0.1x (a slow, heavy follow). **View 2** is non-mouselook, set just above
the horizon (pitch ~-1°), zoom 30%, pivot height 0, shoulder 0.4, dolly 15, near clip on at 5, far
clip on at 4000, solid walls off, turn speed 1.5x. Mouselook ships at yaw sensitivity 2.5, pitch 0.7,
Y inverted, with the centre crosshair on; off-screen character markers are hidden. Everything is
live-tunable in the panel and saved across sessions.

The in-panel *Reset pitch & zoom* button returns the pitch band to 5-89° and the zoom to a
neutral 1x out / 4x in; it leaves near and far clip, the focus offset, and saved presets alone.

## Languages

The settings panel follows the game's language automatically. It ships with English plus
French, Italian, German, Spanish, Simplified Chinese, Japanese, Korean, Russian, Ukrainian and
Brazilian Portuguese. On load the mod reads the game's current language and loads the matching
file from its `Localization` folder; anything a translation hasn't covered falls back to English,
and a language with no bundled file simply shows English. The detected language is noted in the
UnityModManager log.

The non-English files are **machine-generated first drafts** and haven't had a native pass yet -
the camera jargon especially (pivot, shoulder, dolly, mouselook, clip planes) may read awkwardly.
Corrections are very welcome: each string lives in `Localization/<code>.json` keyed by its English
text, so you can copy `en.json`, translate the values, and post it on the mod page or send it my
way. Nothing in a translation can break the panel - a missing or malformed entry just falls back
to English.

## Build

Requires the .NET SDK. From the project folder:

    dotnet build -c Release

Output: `bin/Release/ServoSkullCameraControls.dll`. The project references
`UnityEngine.CoreModule`, `UnityEngine.IMGUIModule`, `UnityEngine.InputLegacyModule`,
and `0Harmony` from the game's `...\WH40KRT_Data\Managed` folder, and `UnityModManager.dll`
from the UMM folder, via the `$(Managed)` / `$(UMMDir)` paths in the .csproj.

## Install

1. Install [Modfinder](https://www.nexusmods.com/warhammer40kroguetrader/mods/146), the mod
   manager for Rogue Trader.
2. Download the mod archive and drag it onto Modfinder's "Drag zips here to install" box.
3. Launch the game and open the UnityModManager overlay with **Ctrl+F10** to confirm it
   loaded, then configure it there.
4. (Optional) For drag-pitch in the non-mouselook view, enable ToyBox's "Mouse3 drag to aim"
   option.

## AI disclosure

This mod was built collaboratively with an AI assistant (Anthropic's Claude). Most of the C# was
written by the assistant working from my direction - reverse-engineering Rogue Trader's camera rig
from the decompiled assemblies, iterating on behaviour, and refining the settings - while the
design decisions, in-game testing, debugging, and every release build were done by me. No game
assets or third-party content were generated; this is original code that hooks the game through
UnityModManager and Harmony.

## License

MIT - see [LICENSE](LICENSE). You're welcome to modify and redistribute it; please keep the
copyright notice. The mod-page permissions are set to match: feel free to make and share your own
changes.

## Credits

- **SolemnScribe** - author.
- Built on **UnityModManager** and **Harmony** (0Harmony).
- UI translations - initial machine-generated drafts; native/community corrections welcome.
- Developed with AI assistance (see above).
