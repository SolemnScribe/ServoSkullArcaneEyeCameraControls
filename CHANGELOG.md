# Changelog

All notable changes to Servo-Skull Camera Controls. Versions follow [semantic
versioning](https://semver.org).

`1.24.0` through `1.26.0` were internal builds and were never released publicly. `1.27.0`
is the first public release since `1.23.0` and rolls all of them up.


## [1.39.0]

### Added
- **Toggle mode for the free-cursor key.** A checkbox next to the binding: press the key once to free
  the pointer, press again to recapture it - instead of holding. Hold remains the default behaviour.

### Changed
- **Free-cursor key default is now Left Alt.** Left Shift conflicted with game control bindings. A
  one-time migration moves installs whose key is still Left Shift; other bindings are left alone.

### Fixed
- **Freeze when toggling views with "hide own hover highlight" enabled (reported on Wrath).** The
  feature's first activation resolved game types with a lookup that, on a miss, scans every loaded
  assembly - on machines with a broken third-party assembly that scan can freeze the game for minutes
  (reported as a hard crash and closed via Task Manager; turning the feature off avoided it, which was
  the giveaway). Every such lookup in the mod now uses a scan-free resolver, on both games.
- **Vanilla view misplaced after loading a save mid-session.** Loading a different save while a view was
  active captured that view's pitch and zoom as the "stock camera", so toggling back to Vanilla restored
  the wrong pose. The mod now stands down for the load - the loaded save's true stock camera shows
  briefly and is captured correctly - and the previous view (or View 1, with apply-on-load enabled) is
  re-applied once the area settles. A welcome side effect: an active view now persists across
  mid-session loads even with apply-on-load disabled.


## [1.38.4]

### Fixed
- **Wrath: the mod now stands down during dialogue.** Three per-frame holds did not recognise Wrath's
  dialogue mode and stayed live through every conversation with a view active: the dialogue FRAMING
  (built for Rogue Trader's near-static conversation camera - its dolly pushed the camera far out,
  and its per-frame offsets fought Wrath's speaker-panning camera), the zoom-limit extension, and the
  pitch lock. All three now hand the camera fully back for the whole conversation and re-assert the
  instant it ends. Rogue Trader dialogue behaviour is completely unchanged. Thanks to defalco666 and
  Webwalker888 for the reports.


## [1.38.3]

### Fixed
- **System/sector map entered from a view (Rogue Trader).** Opening the star-system or Koronus Expanse
  map from an active view could race the map camera's own set-up: the mod's hand-off stamp sometimes
  landed on top of it, leaving the map rotated (planets hidden under the UI bar), the orbital plane
  rendered edge-on ("flat"), or the zoom stuck extremely close with the scroll wheel unable to recover.
  The mod now releases its state without writing to the camera on map entry, and leaves the map camera's
  zoom entirely alone while a map is open. Thanks to the reporting user for the precise descriptions.


## [1.38.2]

### Fixed
- **Mouselook hover-gate: highlight restore on release.** Since 1.37.0, a highlight cleared when engaging
  mouselook was never restored on releasing it - the gate's own clear erased its restore bookkeeping.
  Fixed; the highlight now returns exactly when the game believed it should.

### Changed
- **Hover-gate edge logging.** Engaging and releasing mouselook now log their steps. This is deliberate:
  a crash has been reported at exactly that moment on Wrath and is under investigation - if you hit it,
  the last log line identifies the failing step, so please include your Player.log lines in a report.


## [1.38.1]

### Fixed
- **Long load stall without WASD Movement installed.** 1.38.0's detection of the WASD Movement mod could
  fall into a repeated deep assembly scan on installs that don't have that mod, stalling the loading
  screen for many minutes (reported at ~88% on Wrath). The deep scan now runs at most once, and only
  when the mod's assembly is actually present. Detection with the mod installed is unchanged.


## [1.38.0]

### Added
- **Steady dialogue volume.** Character voice lines are 3D sounds attached to the speaker, and the game's
  audio listener rides the camera - so with a view applied, switching views mid-conversation changed how
  loud dialogue was. On by default: during conversations the listener is now held at the stock camera
  position (the mod's view offsets removed), so voice volume no longer shifts with the camera. Toggleable
  next to the dialogue-framing options.
- **Per-view WASD camera pan (WASD Movement bypass).** If the WASD Movement mod (by ADDB) is installed,
  each view can now hand WASD back to the game's own camera panning: the bypass idles that mod through
  its own zero-input path, so character movement stops cleanly and the keys fall through to the native
  pan bindings. Defaults: on for Vanilla and View 2, off for View 1 (where WASD keeps moving the
  character under mouselook). Also gates that mod's built-in camera-key suppression while the bypass is
  active, so the pan keys genuinely reach the game (with the bypass off, the suppression behaves exactly
  as its author intended). Does nothing if WASD Movement isn't installed; gamepad controls are
  unaffected.
- **Per-view move/rotate key swap (Rogue Trader).** Each view can swap the camera pan-left/right keys
  with the rotate-left/right keys, crossed (at default bindings: A/D rotate, Q/E pan) - evaluated at
  input time only, so the Controls screen and your saved bindings are never changed. Default: on for
  View 1, off elsewhere. One-time migration: if your bindings show exactly this crossed scheme set up
  by hand (from using earlier versions), they are restored to defaults once - the View 1 swap then
  provides the same controls - and never touched again; any other custom scheme is left entirely alone.
- **Per-view zoom limits, tidier settings.** The zoom-limit sliders (with their enable toggle) now live
  per view - and Vanilla has its own set - because sensible limits depend on each view's dolly. View 1
  keeps the previous defaults (2.4x out, 4x in); View 2 and Vanilla default to a milder 1.3x out, 2x in,
  much closer to the game's own range. Existing installs: View 1 inherits your current values. The
  settings window also gained collapsible Vanilla and Dialogue sections, and every section now starts
  collapsed.
- **ToyBox 2.0 compatibility (Rogue Trader).** The camera-elevation guard - which auto-disables ToyBox's
  floor-clipping Ctrl+Mouse3 elevation drag while this mod drives the camera - now recognises both
  ToyBox 1.x and the ToyBox 2.0 rewrite, whichever is installed. ToyBox 2.0's reworked camera features
  (Mouse3 aim, zoom-anywhere, FoV multiplier) were verified compatible; the Mouse3-aim option remains
  the source of mouselook pitch on Rogue Trader, exactly as before.


## [1.37.0]

### Added
- **Mouselook: hide your own character's hover highlight.** With the camera centred on the character you
  control, the cursor otherwise sits on them constantly, keeping a permanent selection glow and overtip.
  On by default; only the controlled character is affected - deliberately hovering an enemy still
  highlights, and clicking, selection and movement are untouched. Toggling between mouselook and free
  cursor re-evaluates the highlight immediately - engaging clears a stale highlight, releasing restores
  it if the pointer is already on the character. Toggleable in the mouselook settings.


## [1.36.0]

### Added
- **Automatic view change on combat.** Optionally switch to a chosen view when combat begins and another
  when it ends - by default, Vanilla on entering and View 1 on leaving, both toggleable with a per-direction
  Vanilla / View 1 / View 2 picker in the settings panel. Independent of the toggle-cycle targets. Fires
  once as combat starts or ends and leaves scripted shots alone; Rogue Trader space combat isn't covered.


## [1.35.2]

### Fixed
- **Rogue Trader: inconsistent solid walls after switching views** - walls or doors could end up stuck
  see-through (most visibly: geometry that was mid-dissolve when a solid-walls view took over stayed
  dissolved everywhere, even on the solid-walls view, until the area reloaded), and could plausibly stick
  the other way as well. The toggle previously worked by stopping the game's whole occlusion-fade
  service, which freezes every piece of geometry at whatever opacity it had at that instant - and a later
  restart believes frozen geometry is already solid, so it never repaints it. Solid walls now takes the
  fade system's *camera* offline instead, leaving the service running: nothing can be judged in the way
  without a camera, so geometry actively fades back to solid when the view takes over, fades normally
  when it hands back, and nothing is ever frozen. (Wrath of the Righteous uses a different mechanism and
  was unaffected.)


## [1.35.1]

### Fixed
- **Gamepad zoom on View 1.** Hold the right-stick click (R3) and push the stick up or down to zoom.
  View 1's stick already drives pitch rather than zoom, so this fills the gap without a slider trip; the
  view's saved zoom updates live, exactly as if moved in the panel. The game's own R3 functions - toggling
  the pad camera into panning/free-camera mode, and (on Wrath) the skip-time/rest shortcut - are
  suppressed while View 1 is active, so the zoom chord doesn't also flip camera modes or start a rest.
  Other views keep them, and Skip Time and Rest remain available from the menus as normal. The HUD's
  R3 skip-time hint (the icon that fills as you hold) is hidden while the shortcut is suppressed, rather
  than advertising a control that does nothing.


## [1.35.0]

### Added
- **Gamepad deadzone slider.** The right-stick centre deadzone the mod uses for its own reads (the
  pitch-hold and the new turn multiplier) can now be dialled from 0 to 0.5 instead of the fixed 0.12 -
  different sticks and handhelds want different floors. Input above the deadzone is remapped smoothly,
  so there is no response step at the edge. The game's native input keeps its own calibration.
- **Gamepad turn speed.** A right-stick turn-speed multiplier, 0.1x to 8x. At the default 1.0x the game's
  own turn is untouched; at any other value the mod takes over the stick's horizontal axis and turns at a
  proportional rate, suppressing the native turn so the two don't stack. Stands aside during cutscenes,
  dialogue, scripted shots and on the map, like the pitch-hold.

### Fixed
- The mod now logs and carries on if language detection or game-flavour detection throws during load,
  rather than failing to load outright.

### Credits
- The deadzone slider and the turn-speed and pad-zoom features were proposed - and prototyped in code -
  by **@saghm** on GitHub, in the mod's first community pull request. The implementations were reworked
  for cross-game safety, but the itch and the direction are theirs. Thanks!


## [1.34.4]

### Fixed
- **Wrath of the Righteous: the mod could fail to load on some game versions**, logging a Harmony
  patch error about an ambiguous match on `CommandControlCamera:OnRun`. That cutscene-camera hook is
  resolved by name, and on the affected versions the game exposes more than one `OnRun` overload, which
  made the by-name lookup fail and abort the whole load. The hook now selects the intended overload
  directly, and a single patch that cannot resolve on a given version is skipped with a log line rather
  than stopping the rest of the mod from loading. (Rogue Trader was unaffected.)


## [1.34.3]

### Fixed
- **Reloading a save could leave the active view's pitch and zoom unapplied** until you toggled views -
  most often after returning to the main menu and loading again. The on-load view assertion now holds
  through the game's camera restore until it settles (or you take control) instead of a single early write
  a slow restore could overwrite, and stale per-session state is cleared on load. (Both games; reported on
  Rogue Trader.)
- **Wrath of the Righteous: the near clip plane slider had no effect.** Wrath pins the camera's near plane
  every frame for its depth-reconstruction and reflection passes, which overwrote the setting; Arcane Eye
  now sets near clip through that same pre-pass, so the slider works on Wrath as it already did on Rogue
  Trader. (Far clip was unaffected and already worked.)
- **Wrath of the Righteous: scenery dissolved away in front of the camera on the over-the-shoulder view.**
  Wrath fades out geometry - wagons, market stalls and the like - that comes between the camera and a
  nearby character. That is unobtrusive from the default high camera, but on a low, dollied-in view it ate
  whole props. A view's *solid walls* toggle now suppresses that fade on Wrath as it already did on Rogue
  Trader, so scenery stays solid. (While it is on, characters behind scenery also stop showing their
  see-through outline.)


## [1.34.2]

### Fixed
- **Wrath of the Righteous:** Compatibility layer

### Changed
- **ToyBox's "Ctrl + Mouse3 Drag To Adjust Camera Elevation" is now disabled automatically while the mod
  is active.** With that option on, the camera can break through the floor while moving; the mod previously
  only warned. Disabling the mod hands the option back to ToyBox.


## [1.34.1]
### Fixed
- **The system and sector maps open on the game's own camera again.** Entering the Koronus Expanse
  sector map or an in-system map now drops to the stock framing rather than carrying the active view's
  pitch and zoom onto the map camera, and your view is restored when you return to the surface.


## [1.34.0]

### Added
- **Per-view free-aim cursor on a controller.** In gamepad cursor control, ticking a view's *free-aim
  cursor* box lets the left stick move the on-screen pointer with the camera held still, so you can aim
  at and select interaction icons directly instead of panning the camera under a centred cursor. The
  right stick still rotates and pitches as before. On by default for View 1, off for View 2; Rogue
  Trader only.

## [1.33.0]

### Added
- **Per-view pitch and zoom sliders, with an optional pitch lock.** Each view's tilt and zoom can now
  be dialled directly with a slider rather than only captured from the live camera - useful on a
  controller, where the right stick drives pitch and can no longer scroll to zoom. A per-view *lock
  pitch* toggle re-asserts the tilt every frame so a cutscene or area transition can't leave the camera
  flattened; it stands aside on a mouselook view, where the mouse owns the pitch.

### Fixed
- **World icons and interactable names now sit on their targets in the over-the-shoulder and dollied
  views.** With the focus offset active - the lateral shoulder shift and especially View 1's deep
  dolly - entity and interaction overtips were projected from the camera's pre-offset position while
  the scene rendered from the shifted one, so they sat off to one side: the gap widened the further the
  dolly pushed in and reversed direction as you turned. They are now projected through the live camera
  and track their targets whether you hold still or pan. The same correction applies to the selection
  box and other world-anchored markers.

### Changed
- **Interface translations updated for the new settings.** The per-view pitch/zoom sliders and
  pitch-lock are covered in all eleven shipped languages, with a few stale entries cleaned up.
  Non-English strings are machine-drafted and flagged for native-speaker review.

## [1.32.0]

### Added
- **Gamepad: direct character control on load.** On a controller, the mod now starts you in the game's
  *Character control* mode - the left stick moves your character directly - whenever a save loads,
  rather than the stock cursor mode you would otherwise switch on each time with the left-stick click.
  Pressing the left stick (L3) still toggles back to the cursor at any time. On by default and on-foot
  only; turn it off under the gamepad settings if you would rather load in cursor mode.

## [1.31.0]

### Added
- **Vanilla camera in the toggle cycle.** The toggle key (keyboard and gamepad) can now include the
  game's own *vanilla* camera as a cycle target alongside the two presets. Three checkboxes under the
  toggle binds choose which targets the key steps through, in the order View 1 → View 2 → Vanilla;
  the defaults are View 1 and Vanilla on, View 2 off, so out of the box the key alternates the
  over-the-shoulder view and the stock camera. Switching to Vanilla hands the rig back to a
  stock-looking framing - it restores the pitch and zoom of your last free camera, keeps your facing,
  and drops mouselook, while the focus offset and per-view clip relax on their own. Disabling the mod
  in UnityModManager now returns the camera to vanilla as well.

### Credits
- The vanilla-camera toggle was suggested - and prototyped in code - by **@byboom** on NexusMods. Thanks!

### Fixed
- **Arcane Eye (Wrath of the Righteous) now loads.** The 1.30.0 Wrath build shipped the binary renamed
  on disk to `ArcaneEyeCameraControls.dll` while the assembly's internal identity was still
  `ServoSkullCameraControls`; UnityModManager resolves a mod by that identity, so it rejected the mod
  with a red "!!!" that restarting couldn't clear. The build now compiles a properly named assembly per
  game (the type namespace is unchanged, so `EntryMethod` still resolves). Wrath users on 1.30.0 should
  update.

## [1.30.0]

### Added
- **Localization.** The settings panel now follows the game's language automatically and ships
  translated into ten languages besides English: French, Italian, German, Spanish, Simplified
  Chinese, Japanese, Korean, Russian, Ukrainian and Brazilian Portuguese. On load the mod detects
  the game's current language and reads the matching file from its `Localization` folder; anything
  a translation hasn't covered falls back to English, and a language with no bundled file shows
  English. The non-English files are machine-generated first drafts open to native/community
  correction - each string is keyed by its English text in `Localization/<code>.json`, so copying
  `en.json` and translating the values is all it takes. Nothing in a translation can break the
  panel; a missing or malformed entry just falls back to English.

### Changed
- Tightened a couple of panel labels and removed two redundant help lines.

## [1.29.0]

### Added
- Right-stick pitch on a controller. With **View 1** active in gamepad mode, the right stick's
  vertical axis now tilts the camera up and down instead of zooming. The mod holds and drives that
  pitch the way mouselook does with a mouse, so the framing stays put across dialogue and area
  transitions rather than reverting to the game's camera. The horizontal axis still turns the view.
  (View 2 keeps the native rotate-and-zoom.)
- An **invert gamepad pitch** toggle, separate from the mouse invert, since controller players carry
  their own up/down muscle memory.
- A **zoom slider** on each view, so the saved zoom can be dialled in directly. This matters on a pad,
  where the right stick now pitches and can no longer zoom; the slider is the way to set it. It also
  saves re-stamping a view just to nudge the zoom on mouse and keyboard.

### Fixed
- View 1 now re-establishes itself after dialogue and after area transitions when you play on a
  controller. In mouse mode mouselook re-asserts the view every frame, which a pad had nothing to
  stand in for, so the re-stamp didn't hold; the right-stick pitch-hold now fills that role and keeps
  the framing across the handover.

## [1.28.0]

### Added
- Initial controller (gamepad) support. The views now adapt when the game is in gamepad mode:
  - View 1's free-look hands back to the game's native right-stick look. Mouselook reads the mouse
    directly, so on a pad it stood aside rather than fighting a dead axis.
  - The right stick turns at full speed instead of the slow keyboard-strafing rate, which was
    only ever meant for WASD.
  - View 1 keeps the character locked in frame rather than letting the camera drift loosely behind
    you as you move. View 2's wider isometric follow is left as the game intends it.
  - A controller button alternates the two views, defaulting to the Left Bumper on an Xbox pad and
    rebindable to any pad button in the settings. (D-pad directions report as an axis rather than a
    button, so they can't be bound here.)

## [1.27.2]

### Fixed
- In-dialogue scripted camera shots now sit at the level pitch the base game expects. The game
  has no pitch control of its own and assumes the camera is level when it takes over for a
  conversation's scripted shot, so a raised view tilted the authored framing. The rig pitch is
  set back to level as each shot takes the camera; your view's pitch returns when the shot does.
- Your active view now also returns between scripted shots within a conversation, not only when
  the whole conversation ends, completing the 1.27.1 fix. Once a shot hands back and the next
  line is ordinary dialogue, View 1 or View 2 is re-stamped in full (pitch, zoom, dolly, and the
  Solid Walls hold), so a long exchange with several cut-ins no longer leaves you parked at the
  last cinematic's framing.

## [1.27.1]

### Fixed
- The camera now returns to your active view after an in-dialogue scripted camera shot. When a
  conversation's scripted shot ended, the per-frame focus offset resumed on the game's restored
  pitch and zoom rather than the view's, so the framing read as though it had not come back. The
  active view (View 1 or View 2) is now re-stamped once the conversation's exit blend has settled.

## [1.27.0]

### Fixed
- WASD and stick movement no longer reverse when the camera looks above the horizon. The
  game derives a screen-movement basis from the camera - a ground-projected "up" vector -
  and that vector flipped sign as the view crossed level, so holding forward sent you
  backward the moment you tilted past the horizon. The basis is now recomputed from the
  camera's Right vector so it stays continuous through level. Edge-scroll direction is steady
  at every pitch as a result.

### Changed
- Retuned the shipped defaults. View 1 is a closer over-the-shoulder with rotation speed
  turned all the way down to create strafing motion; View 2 is a wide tactical view set just
  above the horizon, with near- and far-clip culling on. The default flattest drag angle is
  now 15°, and the default zoom-out is 2.4x.

## [1.26.0]

### Added
- In-dialogue cinematic handling. When a conversation triggers a scripted camera shot - a
  swing onto a speaker or an object that holds until you pick the next line - the focus-offset
  framing and the Solid Walls hold step aside for the rest of that conversation and resume when
  it ends, so the authored shot frames the way it was meant to. Ordinary dialogue with no
  scripted cut still follows the In-dialogue setting.

## [1.25.0]

### Added
- Map stand-down. On the system map and the zoomed-out sector map, the framing, clip, and
  occluder controls hand back to the game, so its own map camera is left untouched.
- Solid walls (per view). Holds off RT's occluder see-through - the effect that dissolves
  walls and doors between the camera and the party - so geometry stays solid on the closer,
  lower framing this mod allows. Saved per preset; steps aside on the map and during scripted
  shots.
- Live follow (per view). Anchors the framing to the subject's live (smooth, interpolated)
  view transform instead of the discrete tracking focal, locking the model in frame at close
  range.

## [1.24.0]

### Added
- Per-view far clip plane, to cull distant geometry. Off by default, since it can interact with
  fog and the skybox. The mod logs the game's baseline near/far on first apply, so the far range
  can be calibrated against it.

### Changed
- Gave the near and far clip planes their own independent per-view enable toggles, replacing the
  earlier single clip toggle. The near clip plane and its per-preset value carried over from
  1.23.0.

## [1.23.0]
- Last public release prior to this rollup.
