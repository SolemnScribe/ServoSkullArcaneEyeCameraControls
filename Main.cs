using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace ServoSkullCameraControls
{
    // How the focus framing behaves during a conversation (RT's Dialog mode).
    //   Off      - hand off entirely; the game frames the conversation.
    //   Lift     - keep only the gentle world-up pivot/shoulder raise (no dolly), on the game's lowered base.
    //   Tactical - keep the full over-the-shoulder offset incl. dolly, and cancel the game's vertical
    //              reframing (pin the base height to the focal) so the shot matches gameplay; the game
    //              still nudges left/right to frame the speakers.
    public enum DialogFramingMode { Off, Lift, Tactical }

    // One saved camera setup. XML-serialized inside Settings, so views persist
    // across sessions. Yaw is intentionally NOT stored, so a view works no matter
    // which way you are facing.
    public class CameraView
    {
        public bool IsSet = false;
        public float Pitch;     // m_TargetRotate.x, degrees
        public float Zoom;      // CameraZoom scroll position
        public float NearClip = 0.3f;        // per-view near clip plane (vcam lens) when NearClipEnabled
        public bool  NearClipEnabled = true; // cull geometry closer than NearClip on this view
        public float FarClip = 2000f;        // per-view far clip plane when FarClipEnabled (provisional range - calibrate to the game baseline)
        public bool  FarClipEnabled = false; // cull geometry beyond FarClip on this view (off by default; can interact with fog/skybox)
        public bool Mouselook = false; // drive yaw/pitch with the mouse while this view is active
        public bool LockPitch = false; // hold this view's Pitch every frame (cutscene/transition-proof); ignored while Mouselook is on, and on View 1 on a pad
        public float RotateSpeedMult = 1.0f; // per-view keyboard-rotation speed multiplier (1 = unchanged)
        public float PivotHeight = 2.0f;     // per-view world-up pivot height (~shoulder/head); raises the orbit
        public float Shoulder = 0f;          // per-view lateral over-the-shoulder offset along Right (+/- picks the side)
        public float Dolly = 0f;             // per-view dolly-in distance toward the focal at fixed FOV (0 = native ~27u distance)
        public bool LiveFollow = false;      // anchor to the subject's live (smooth, interpolated) ViewTransform instead of the discrete logic-tracking focal; locks the model in frame at close range
        public bool SolidWalls = false;      // suppress RT's occluder see-through on this view, so walls and doors in front of the camera stay solid instead of dissolving
        public float DialogHeight = 1.3f;    // Full-tactical dialogue: world-up framing height for this view (was the global DialogVerticalOffset)
        public bool  DialogZoomEnabled = false; // pin a fixed zoom in Full-tactical dialogue for this view (off = the view's own zoom carries through)
        public float DialogZoom = 0.4f;       // the pinned dialogue zoom (scroll position 0..1) when DialogZoomEnabled
        public bool PadFreeAimCursor = false; // gamepad cursor control: drive the on-screen pointer with the left stick instead of flying the camera, leaving the camera fixed (RT surface exploration, cursor mode only)
    }

    public class Settings : UnityModManager.ModSettings
    {
        // --- Focus offset (per-view world-up pivot + lateral shoulder; applied in every mode) ---
        public bool FramingEnabled = true;
        public bool FramingPauseInCutscenes = false;
        public DialogFramingMode DialogFraming = DialogFramingMode.Tactical;   // behaviour during RT Dialog mode (see enum)
        public float DialogVerticalOffset = 1.3f;   // LEGACY: the old single global dialogue height; kept only so it still deserializes for the one-time migration into each view's DialogHeight. No longer shown or used at runtime.
        public bool  DialogFramingMigrated = false;  // set once DialogVerticalOffset has been copied into the per-view DialogHeight fields
        public bool  PadFreeAimMigrated = false;      // set once the new per-view PadFreeAimCursor default has been seeded (View 1 on)

        // --- Pitch range (widens the band the native Mouse3 drag is clamped to) ---
        public bool PitchRangeEnabled = true;
        public float MinPitchAngle = 15f;       // flattest the drag may reach
        public float MaxPitchAngle = 89f;       // steepest the drag may reach

        // --- Zoom limits (native scroll travels within these) ---
        public bool ZoomLimitsEnabled = true;
        public bool ZoomPauseInCutscenes = true;
        public float ZoomOutFactor = 2.4f;      // 1..3, pull back further
        public float ZoomInFactor = 4f;         // 1..6+, get much closer

        // --- Presets ---
        // Defaults seeded from a tuned setup: View 1 close over-the-shoulder (mouselook + dolly), View 2 wide tactical.
        public CameraView View1 = new CameraView { IsSet = true, Pitch = 37.3301f, Zoom = 0.4f, NearClip = 1.5f, NearClipEnabled = false, Mouselook = true, RotateSpeedMult = 0.1f, PivotHeight = 1.9f, Shoulder = 0.4f, Dolly = 25.5f, LiveFollow = false, SolidWalls = true, PadFreeAimCursor = true };
        public CameraView View2 = new CameraView { IsSet = true, Pitch = -1.05517578f, Zoom = 0.3f, NearClip = 5f, NearClipEnabled = true, FarClip = 4000f, FarClipEnabled = true, Mouselook = false, RotateSpeedMult = 1.5f, PivotHeight = 0f, Shoulder = 0.4f, Dolly = 15f, LiveFollow = false, SolidWalls = false };
        public int SetView1Key = (int)KeyCode.Keypad7;
        public int SetView2Key = (int)KeyCode.Keypad9;
        public int ToggleKey   = (int)KeyCode.CapsLock;
        public int GamepadToggleKey = (int)KeyCode.JoystickButton4;   // gamepad view-toggle; defaults to the Xbox Left Bumper (LB). The D-pad reports as an axis, not a button, so Unity's input can't bind it here; rebindable to any pad button in settings
        public bool GamepadInvertPitch = false;  // invert the right-stick pitch on a pad. Off = stick up looks up; on flips it. Needs its own flag, not MouselookInvertY: the stick vector's Y sign is already opposite Unity's Mouse Y, so the same factor reads the other way round on a pad

        // Which targets the toggle key steps through, in the order View 1 -> View 2 -> Vanilla. A preset
        // is only included when it is also saved; Vanilla is the game's own camera (no preset). Defaults:
        // View 1 and Vanilla on, View 2 off, so the toggle alternates the over-the-shoulder view and stock.
        public bool CycleView1   = true;
        public bool CycleView2   = false;
        public bool CycleVanilla = true;

        // Apply View 1 once when a save is loaded (initial game load), not on area-to-area transitions.
        public bool ApplyView1OnLoad = true;

        // Gamepad only, on-foot: engage the game's "Character control" (the left stick moves your character
        // directly) once when a save loads, instead of stock cursor mode. The player can still click the left
        // stick (L3) to toggle it back to the cursor. Mirrors a single L3 press at load.
        public bool DirectControlOnGamepadLoad = true;

        // --- Mouselook (per-view; a flagged view drives yaw/pitch with the mouse) ---
        public float MouselookSensitivity = 2.5f;   // X (yaw) sensitivity
        public float MouselookSensY = 0.7f;          // Y (pitch) sensitivity
        public bool MouselookInvertY = true;
        public bool MouselookCrosshair = true;
        public int FreeCursorKey = (int)KeyCode.LeftShift;

        // --- Off-screen character markers (the edge portrait pointers) ---
        public bool HideOffscreenUnitMarkers = true;

        public override void Save(UnityModManager.ModEntry modEntry)
            => UnityModManager.ModSettings.Save(this, modEntry);
    }

    // ----------------------------------------------------------------------------------------------------
    // Localization. ToyBox-style: per-locale JSON in a Localization\ folder beside the mod, each file
    //   { "LanguageCode": "..", "Strings": { "<English source>": "<translation>", ... } }
    // The code wraps user-facing strings in Main.L("..."); the lookup keys on the English source and falls
    // back to it, so a missing file or key just shows English and can never break the panel. The active
    // language follows the game: we read the game's current locale by reflection
    // (LocalizationManager(.Instance).CurrentPack.Locale) and map it to one of our files. English needs no
    // file - its keys are already English. The Locale type lives in an assembly we don't reference, so the
    // reflection is defensive: any miss falls back to the OS language, then English. The detected locale
    // and chosen file are logged so the detection can be confirmed in-game.
    public static class Localization
    {
        static Dictionary<string, string> _strings;     // active translations; null => show English
        public static string ActiveCode = "en";

        public static void Init(UnityModManager.ModEntry modEntry)
        {
            try
            {
                string raw = DetectGameLocale();                 // e.g. "frFR", or null
                string sys = SafeSystemLanguage();
                ActiveCode = MapToFile(raw ?? sys);
                string note;
                if (ActiveCode == "en")
                {
                    _strings = null;                              // English: keys are already English
                    note = "English (no file needed)";
                }
                else
                {
                    string path = System.IO.Path.Combine(System.IO.Path.Combine(modEntry.Path, "Localization"), ActiveCode + ".json");
                    if (System.IO.File.Exists(path))
                    {
                        _strings = LoadStrings(path);
                        note = _strings.Count + " strings from " + ActiveCode + ".json";
                    }
                    else
                    {
                        _strings = null;
                        note = ActiveCode + ".json not present - showing English";
                    }
                }
                modEntry.Logger.Log("Localization: game locale '" + (raw ?? "(none)") + "', system '" + sys
                                    + "' -> file '" + ActiveCode + "' (" + note + ").");
            }
            catch (Exception e)
            {
                _strings = null; ActiveCode = "en";
                modEntry.Logger.Error("Localization init failed (showing English): " + e);
            }
        }

        // Look up the translation for an English source string, or return the English unchanged.
        public static string Get(string english)
        {
            if (_strings != null && english != null && _strings.TryGetValue(english, out var t) && !string.IsNullOrEmpty(t))
                return t;
            return english;
        }

        static Dictionary<string, string> LoadStrings(string path)
        {
            var map = new Dictionary<string, string>();
            var root = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
            if (root["Strings"] is Newtonsoft.Json.Linq.JObject strings)
                foreach (var kv in strings)
                    map[kv.Key] = (string)kv.Value;
            return map;
        }

        // Read the game's current locale by reflection: LocalizationManager(.Instance).CurrentPack.Locale,
        // trying a few shapes. Returns the enum's name (e.g. "frFR") or null. Traverse.GetValue is null-safe,
        // so a missing member just yields null and we move on.
        static string DetectGameLocale()
        {
            var lm = AccessTools.TypeByName("Kingmaker.Localization.LocalizationManager");
            if (lm == null) return null;
            var chains = new[]
            {
                new[] { "CurrentLocale" },
                new[] { "CurrentPack", "Locale" },
                new[] { "Instance", "CurrentPack", "Locale" },
                new[] { "Instance", "CurrentLocale" },
            };
            foreach (var chain in chains)
            {
                try
                {
                    var tr = Traverse.Create(lm);
                    object val = null;
                    foreach (var member in chain)
                    {
                        var step = tr.Property(member);
                        val = step.GetValue();
                        if (val == null) { step = tr.Field(member); val = step.GetValue(); }
                        if (val == null) break;
                        tr = Traverse.Create(val);
                    }
                    if (val != null) return val.ToString();
                }
                catch { }
            }
            return null;
        }

        static string SafeSystemLanguage()
        {
            try { return Application.systemLanguage.ToString(); } catch { return "English"; }
        }

        // Map an Owlcat Locale enum name (frFR, deDE, zhCN, jaJP, ptBR, enGB, ...) OR a Unity SystemLanguage
        // name (French, German, ChineseSimplified, ...) to one of our shipped files. We carry Simplified
        // Chinese and Brazilian Portuguese, so any Chinese/Portuguese maps to those.
        static string MapToFile(string s)
        {
            if (string.IsNullOrEmpty(s)) return "en";
            s = s.ToLowerInvariant();
            if (s.Contains("zh") || s.Contains("chin")) return "zh-CN";
            if (s.StartsWith("ja") || s.Contains("japan")) return "ja";
            if (s.StartsWith("pt") || s.Contains("portu") || s.Contains("brazil")) return "pt-BR";
            if (s.StartsWith("fr") || s.Contains("french")) return "fr";
            if (s.StartsWith("it") || s.Contains("ital")) return "it";
            if (s.StartsWith("de") || s.Contains("german") || s.Contains("deutsch")) return "de";
            if (s.StartsWith("es") || s.Contains("span")) return "es";
            if (s.StartsWith("ko") || s.Contains("korea")) return "ko";
            if (s.StartsWith("ru") || s.Contains("russ")) return "ru";
            if (s.StartsWith("uk") || s.Contains("ukrain")) return "uk";
            return "en";
        }
    }

    public static class Main
    {
        public static Settings settings;
        public static UnityModManager.ModEntry.ModLogger Log;

        // Localize a user-facing string (returns the English unchanged if there's no active translation).
        internal static string L(string s) => Localization.Get(s);
        public static UnityModManager.ModEntry ModEntry;
        public static Harmony HarmonyInst;
        public static bool Active = true;

        // Live rig, cached each frame in the UpdateInternal postfix, so the preset
        // hotkeys (handled in OnUpdate) have something to act on.
        public static object CurrentRig;

        // View-1-on-load: a save load arms _loadPending; the next area-did-load converts it into a short frame
        // countdown (so the game's own camera restore settles first), and OnUpdate applies View 1 when it elapses.
        // It also arms a recenter window: the game parks the camera on an establishing shot and only follows your
        // character once you move, so for a brief window we snap the focal onto the selected unit (or main
        // character). The window is time-based and self-terminating - it stops as soon as the focal has sat
        // undisturbed on the subject, so it normally releases in a fraction of a second rather than running full
        // length. Area-to-area transitions never set _loadPending, so they are left alone.
        const int ApplyView1DelayFrames = 4;
        const int ViewAssertStuckFrames = 3;       // on-load: seated pitch/zoom must survive this many consecutive frames to count as settled
        const float ViewAssertMaxSeconds = 1.0f;   // on-load: hard cap on the View-1 re-assert hold
        const int DirectControlDelayFrames = 6;   // gamepad on-load direct-control flip: wait past the area load for the gameplay input layer (SurfaceMainInputLayer.Instance) to come up
        const int PostDialogReapplyFrames = 10;   // wait this many frames after a scripted-shot conversation ends, for the exit blend to settle, before re-stamping the active view (tunable)
        const float RecenterMaxSeconds = 0.8f;     // hard cap on the on-load recenter window
        const float RecenterMinSeconds = 0.2f;     // keep correcting at least this long (covers the establishing-shot set and unit positioning)
        const float RecenterSettleSeconds = 0.2f;  // ...then stop once the focal has sat on the subject this long undisturbed
        static bool _loadPending;
        static int _applyView1Countdown;
        static int _directControlCountdown;   // frames until the gamepad on-load direct-control flip is attempted
        static int _directControlRetries;     // remaining per-frame retries if the input layer isn't up yet at the flip tick
        static int _postDialogReapply;
        static bool _recenterActive;
        static float _recenterElapsed, _recenterSettled;
        static bool _viewAssertActive;    // on-load: holding View 1's pitch/zoom until it sticks or you take control
        static int  _viewAssertStuck;     // consecutive frames the seated pitch/zoom survived unchanged
        static float _viewAssertElapsed;  // seconds the on-load hold has run (against the hard cap)
        internal static void NotifyGameLoad()
        {
            _loadPending = true;
            CameraRig_UpdateInternal_Patch.ClearFollower();
            CutsceneCameraGate.Reset();
            // Clear per-session statics that otherwise survive a return-to-main-menu, so a load after being in
            // game starts as clean as a first load: the stale mouselook seat (it would drive a wrong pitch until
            // the on-load re-assert lands) and the vanilla capture (re-grab this session's stock camera on the
            // next ApplyView rather than keeping the previous session's). Also drop any in-flight on-load hold.
            CameraRig_UpdateInternal_Patch.ResetMouselookSeat();
            _vanillaCaptured = false;
            _viewAssertActive = false;
        }
        internal static void NotifyAreaDidLoad()
        {
            if (!_loadPending) return;
            _loadPending = false;
            if (settings != null && settings.ApplyView1OnLoad)
            {
                _applyView1Countdown = ApplyView1DelayFrames;
                _recenterActive = true; _recenterElapsed = 0f; _recenterSettled = 0f;
            }
            // Independent of the camera-view apply: arm the gamepad on-load direct-control flip. The gamepad
            // check happens when it fires (not here), so a pad in use at load is covered regardless of View 1.
            if (settings != null && settings.DirectControlOnGamepadLoad)
            {
                _directControlCountdown = DirectControlDelayFrames;
                _directControlRetries = 20;
            }
        }

        // Mouselook runtime state
        public static bool MouselookActive;      // set when a mouselook-flagged view is applied
        public static bool CursorLocked;          // true while we hold the cursor at centre (for the crosshair)
        public static bool MouselookJustEngaged;  // swallow one frame of mouse delta on (re)engage to avoid a jump
        static bool _weLockedCursor;              // we, not the game, put the cursor into Locked
        static Texture2D _blankCursor;            // fully transparent cursor texture, swapped in while we hold the cursor
        static bool _cursorBlanked;               // we've swapped in the transparent cursor (restore on release)

        // Public accessor for sibling mods to read by reflection (the same way RTVR reads CursorLocked): the live
        // world position of the subject the camera orbits - the focal, taken from the follower entity's
        // ViewTransform. Pitch-stable (it does not swing as the camera orbits the focal), unlike camera/head
        // height, so it is the right thing to clamp world-space placement against. False when there is no follower.
        public static bool TryGetSubjectWorldPosition(out Vector3 pos)
            => CameraRig_UpdateInternal_Patch.TryGetLiveSubjectPos(out pos);

        // Limits / steps
        public const float PitchHardMin = 5f, PitchHardMax = 89f;

        // Mouselook is free-look: it may pitch this far above the horizon (negative = up), independent of the
        // Mouse3-drag band. The screen-basis Up-fix keeps WASD / edge-scroll movement correct above the horizon.
        public const float MouselookPitchMin = -85f;
        public const float MinPitchLo = 5f, MinPitchHi = 45f;
        public const float MaxPitchLo = 45f, MaxPitchHi = 89f, PitchStep = 1f;
        public const float ZoomOutMin = 1f, ZoomOutMax = 3f;
        public const float ZoomInMin = 1f, ZoomInMax = 10f, ZoomStep = 0.1f;
        public const float NearClipMin = 0.1f, NearClipMax = 18f, NearClipStep = 0.1f;
        public const float FarClipMin = 50f, FarClipMax = 4000f, FarClipStep = 25f;   // provisional - calibrate FarClipMax to the game's logged baseline far
        public const float MouseSensMin = 0.2f, MouseSensMax = 10f, MouseSensStep = 0.1f;
        public const float RotMultMin = 0.1f, RotMultMax = 2f, RotMultStep = 0.05f;
        public const float FollowYawRate = 140f;   // deg/s the follow yaw may slew at when mult=1 (stock follow turn measured at ~131 deg/s); the per-view multiplier scales this down
        public const float PivotMin = 0f, PivotMax = 8f, PivotStep = 0.1f;
        public const float ShoulderMin = -8f, ShoulderMax = 8f, ShoulderStep = 0.1f;
        public const float DollyMin = 0f, DollyMax = 32f, DollyStep = 0.5f;   // dolly-in distance toward focal (native cam-to-focal ~27.5u; max pushes to just past the model for first-person/VR)
        public const float ViewZoomMin = 0f, ViewZoomMax = 1f, ViewZoomStep = 0.02f;   // per-view saved zoom = camera scroll position, normalised 0..1
        public const float DollyPast = 1.0f;   // how far past the subject a full dolly may punch (near-clip culls the model when you are inside it)
        public const float PivotScrollStep = 2.5f;   // Ctrl+scroll pivot-height change per notch
        public const float DollyScrollStep = 2.0f;   // Ctrl+Shift+scroll dolly change per notch
        public const float DialogHeightMin = 0f, DialogHeightMax = 4f;   // Full-tactical dialogue framing-height slider range (world-up raise)

        static int _bindingTarget;   // 0 none; 1 set-view-1; 2 set-view-2; 3 toggle
        static int _activeView;      // 0 none/vanilla; 1; 2  (toggle state)

        // System/sector map view handoff (see TickMapViewHandoff): on entering a map we drop to the stock camera
        // so it opens vanilla, remember the player's view, and restore it once back in plain surface gameplay.
        static bool _wasInMapMode;
        static int  _viewBeforeMap;
        static bool _mapRestorePending;

        // Vanilla-camera baseline: the live free camera (pitch/zoom) recorded while no preset is active,
        // so a toggle to Vanilla can hand the rig back to a stock-looking framing. Yaw is never stored, so
        // returning to vanilla doesn't swing the player's facing. Runtime state only; recaptured each session.
        static bool  _vanillaCaptured;
        static float _vanillaPitch, _vanillaRoll;
        static float _vanillaZoomPlayer, _vanillaScroll, _vanillaSmoothScroll;

        // Gamepad pitch-hold state (View 1 on a pad): the captured right-stick Y, a held pitch the stick adjusts,
        // and the rate that turns stick deflection into deg/sec. GpRightStick* are written by the input-layer
        // capture patch; the frame stamp lets a centred stick (which fires no Rewired event) read as zero.
        internal static float GpRightStickY;
        internal static int GpRightStickFrame = -100;
        // WotR's input layer has no static Instance; its per-frame OnUpdate patch caches the live layer here
        // so the on-load direct-control flip can reach it. Unused on RT (which resolves Instance statically).
        internal static object CachedInputLayer;
        static float _gpPitch;
        static bool _gpPitchActive;
        public const float GamepadPitchRate = 90f;   // deg/sec at full stick, before the Y-sensitivity multiplier

        // Foreign-patch suppression bookkeeping
        static bool _suppressSettled;
        static float _suppressElapsed;
        static readonly string[] CameraMethods =
        {
            "Kingmaker.View.CameraZoom:TickZoom",
        };

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;
            Log = modEntry.Logger;
            settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            if (!settings.DialogFramingMigrated)   // one-time: the dialogue framing height moved from a single global to per-view
            {
                settings.View1.DialogHeight = settings.DialogVerticalOffset;
                settings.View2.DialogHeight = settings.DialogVerticalOffset;
                settings.DialogFramingMigrated = true;
                settings.Save(modEntry);
            }
            if (!settings.PadFreeAimMigrated)   // one-time: seed the new gamepad free-aim cursor default (View 1 on, View 2 off) for existing installs
            {
                settings.View1.PadFreeAimCursor = true;
                settings.PadFreeAimMigrated = true;
                settings.Save(modEntry);
            }
            Localization.Init(modEntry);   // pick the language file matching the game's current locale
            Compat.Init();                 // detect RT vs WotR reflection targets (logs the UI flavour)

            HarmonyInst = new Harmony(modEntry.Info.Id);
            PatchAllResilient(HarmonyInst, Assembly.GetExecutingAssembly());

            modEntry.OnToggle = OnToggle;
            modEntry.OnUpdate = OnUpdate;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;

            // Persistent IMGUI object that draws the centre crosshair while mouselook holds the cursor.
            if (GameObject.Find("ServoSkullMouselookCrosshair") == null)
            {
                var crosshairGo = new GameObject("ServoSkullMouselookCrosshair");
                UnityEngine.Object.DontDestroyOnLoad(crosshairGo);
                crosshairGo.AddComponent<MouselookCrosshair>();
            }
            return true;
        }

        // Like Harmony.PatchAll, but patches each [HarmonyPatch] class in isolation so that one class whose
        // Prepare/TargetMethod/Patch throws - e.g. a reflection lookup that turns ambiguous on some game
        // version - is logged and skipped instead of aborting every remaining patch and failing the whole
        // mod load. This is the same per-type CreateClassProcessor().Patch() that PatchAll runs internally,
        // just wrapped per class. Targets that can't resolve are still expected to self-disable via their own
        // Prepare() returning false; this only catches the harder failure where resolution itself throws.
        static void PatchAllResilient(Harmony harmony, Assembly asm)
        {
            int applied = 0, skipped = 0;
            foreach (var type in AccessTools.GetTypesFromAssembly(asm))
            {
                try
                {
                    var patched = harmony.CreateClassProcessor(type).Patch();
                    if (patched != null && patched.Count > 0) applied++;
                }
                catch (Exception e)
                {
                    skipped++;
                    Log?.Error("Patch class '" + type.FullName + "' could not be applied on this game version; skipping it (the rest of the mod still loads). " + e.Message);
                }
            }
            if (skipped > 0)
                Log?.Log("Harmony: " + applied + " patch class(es) applied, " + skipped + " skipped. See errors above for the skipped ones.");
        }

        static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Active = value;
            if (!value)
            {
                RestoreOccluderClip();   // mod disabled in UMM: re-enable the game's clip if we were holding it off
                ApplyVanilla();          // and hand the camera back to its stock framing
            }
            return true;
        }

        static void OnUpdate(UnityModManager.ModEntry modEntry, float dt)
        {
            SuppressForeignCameraPatches();
            UpdateCursorLock();
            if (Active) ToyBoxProbe.EnforceCtrlElevationOff();   // force ToyBox's floor-clipping camera-elevation option off while we drive the camera

            if (settings == null) return;
            if (_bindingTarget == 4) { ScanGamepadBind(); return; }   // armed for a gamepad button: IMGUI key events can't see JoystickButtons, so poll them here
            if (_bindingTarget != 0) return;

            if (!Active || CurrentRig == null) return;

            // System/sector map: open on the stock camera, restore the player's view on return to the surface.
            TickMapViewHandoff();

            // Hand the camera back once the conversation that owned a scripted shot has ended. If a scripted shot
            // did own it, arm a short delayed re-stamp of the active view: the conversation's exit blend restores
            // the game's own camera, so (as with the on-load apply below) we wait a few frames for it to settle.
            if (CutsceneCameraGate.ReleaseAndCheckReapply(CameraRig_UpdateInternal_Patch.InDialogMode()) && _activeView != 0)
                _postDialogReapply = PostDialogReapplyFrames;

            // View-1-on-load: armed by a save load and delayed a few frames past the area load so the game's
            // own camera restore settles first; then apply View 1 once. (Area transitions never arm this.)
            if (_applyView1Countdown > 0)
            {
                _applyView1Countdown--;
                if (_applyView1Countdown == 0 && settings.ApplyView1OnLoad && settings.View1.IsSet)
                {
                    _viewAssertActive = true; _viewAssertStuck = 0; _viewAssertElapsed = 0f;
                    ApplyView(settings.View1);
                    _activeView = 1;
                    Log?.Log("Applying View 1 on game load...");
                }
            }
            else if (_viewAssertActive)
            {
                // Hold View 1's pitch/zoom against a still-settling save-load camera restore: re-seat each frame
                // until the values survive unchanged for a few frames (the restore has finished) or you take
                // control, rather than a single write a slow restore could overwrite. Mouselook takes over only
                // once we stop, from the seat we just set. Self-logs the settle time for on-machine confirmation.
                _viewAssertElapsed += Time.unscaledDeltaTime;
                _viewAssertStuck = ViewSeatHeld(settings.View1) ? _viewAssertStuck + 1 : 0;
                bool tookControl = CameraRig_UpdateInternal_Patch.FollowerActive();
                if (_viewAssertStuck >= ViewAssertStuckFrames || tookControl || _viewAssertElapsed >= ViewAssertMaxSeconds)
                {
                    _viewAssertActive = false;
                    string why = tookControl ? "took control" : (_viewAssertStuck >= ViewAssertStuckFrames ? "settled" : "cap");
                    Log?.Log("View 1 seated on load after " + _viewAssertElapsed.ToString("0.00") + "s (" + why + ").");
                }
                else
                {
                    ApplyView(settings.View1);
                    _activeView = 1;
                }
            }

            // Gamepad on-load "Character control": once the area has settled, if we're on a pad, switch the
            // on-foot input layer out of cursor mode into direct character movement - the same flip L3 does.
            // One-shot (never re-asserted), so the player keeps full L3 control afterward. If the input layer
            // isn't up yet at the flip tick, retry for a few frames rather than miss the window.
            if (_directControlCountdown > 0)
            {
                _directControlCountdown--;
                if (_directControlCountdown == 0 && settings.DirectControlOnGamepadLoad)
                {
                    if (CameraRig_UpdateInternal_Patch.InGamepadMode())
                    {
                        if (!TryEngageDirectControlOnFoot() && _directControlRetries-- > 0)
                            _directControlCountdown = 1;   // input layer not ready; try again next frame
                    }
                    // Not on a pad: leave the cursor alone (mouse/keyboard players don't want this), no retry.
                }
            }

            // Post-dialogue re-stamp: an in-dialogue scripted shot left the camera on the game's restored
            // pitch/zoom, so the per-frame focus offset was resuming on the wrong base. Re-apply the active view
            // once the exit blend has settled (mirrors the on-load apply above). Skip if a new dialogue/cutscene
            // has already taken the camera.
            if (_postDialogReapply > 0)
            {
                _postDialogReapply--;
                if (_postDialogReapply == 0 && _activeView != 0
                    && !CameraRig_UpdateInternal_Patch.InDialogMode()
                    && !CameraRig_UpdateInternal_Patch.InCutscene())
                {
                    ApplyView(ActiveViewObj());
                    Log?.Log("Re-applied active view after dialogue.");
                }
            }

            // Answer-driven re-stamp: the player advanced past a held scripted shot and the next beat turned out
            // to be ordinary dialogue (no new shot superseded the debounce), so bring the active view back now,
            // mid-conversation, without waiting for the whole dialogue to end.
            if (CutsceneCameraGate.TickAnswerReclaim(Time.unscaledDeltaTime) && _activeView != 0)
            {
                ApplyView(ActiveViewObj());
                Log?.Log("Re-stamped active view after dialogue advance.");
            }

            // Recenter window: on a save load the camera sits on the area's establishing shot and the follow only
            // attaches to your character when you first move. Until then, snap the focal onto the selected unit
            // (or main character) so a close View 1 frames it. Stop as soon as you take control (the follower goes
            // live and now holds the focal), or once the focal has sat undisturbed on the subject past a short
            // minimum, or at the hard cap; skip while a cutscene owns the camera.
            if (_recenterActive)
            {
                _recenterElapsed += Time.unscaledDeltaTime;
                if (CameraRig_UpdateInternal_Patch.FollowerActive() || _recenterElapsed >= RecenterMaxSeconds)
                    _recenterActive = false;
                else if (!CameraRig_UpdateInternal_Patch.InCutscene())
                {
                    if (CameraRig_UpdateInternal_Patch.RecenterFocalOnSubject(out bool moved))
                    {
                        _recenterSettled = moved ? 0f : _recenterSettled + Time.unscaledDeltaTime;
                        if (_recenterElapsed >= RecenterMinSeconds && _recenterSettled >= RecenterSettleSeconds)
                            _recenterActive = false;
                    }
                }
            }

            // View keys are inert on the system/sector map: the game owns that camera, and Set/Toggle run
            // ApplyView, a one-shot direct write to the rig (m_TargetRotate etc.) that pokes the map camera's
            // pitch even though the per-frame framing/clip already stand down there. (StarSystem pitch jump.)
            if (!CameraRig_UpdateInternal_Patch.InMapMode())
            {
                if (KeyDown(settings.SetView1Key)) { CaptureView(settings.View1); _activeView = 1; settings.Save(modEntry); }
                else if (KeyDown(settings.SetView2Key)) { CaptureView(settings.View2); _activeView = 2; settings.Save(modEntry); }
                else if (KeyDown(settings.ToggleKey)) ToggleViews();
                else if (KeyDown(settings.GamepadToggleKey)) ToggleViews();
            }

            // Hold RT's occluder see-through off while a solid-walls view is active (restored when we leave it).
            UpdateOccluderClip();
        }

        static bool KeyDown(int k) => k != (int)KeyCode.None && Input.GetKeyDown((KeyCode)k);

        static bool FreeCursorHeld()
            => settings != null && settings.FreeCursorKey != (int)KeyCode.None
               && Input.GetKey((KeyCode)settings.FreeCursorKey);

        // The UMM manager window is an IMGUI overlay the game's UI state knows nothing about,
        // so mouselook must yield the cursor while it is open (to bind keys, tick views, etc.).
        static bool _ummProbed;
        static PropertyInfo _ummInstance, _ummOpened;
        static bool UmmWindowOpen()
        {
            try
            {
                if (!_ummProbed)
                {
                    _ummProbed = true;
                    var ui = typeof(UnityModManager).GetNestedType("UI");
                    if (ui != null)
                    {
                        _ummInstance = ui.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                        _ummOpened = ui.GetProperty("Opened", BindingFlags.Public | BindingFlags.Instance);
                    }
                }
                if (_ummInstance == null || _ummOpened == null) return false;
                var inst = _ummInstance.GetValue(null);
                return inst != null && _ummOpened.GetValue(inst) is bool b && b;
            }
            catch { return false; }
        }

        // Mouselook controls the camera only in plain surface gameplay: a flagged view is active,
        // nothing (UI, dialogue, cutscene, the UMM panel) needs the cursor, and the free key is up.
        public static bool MouselookEngaged()
        {
            if (!Active || settings == null || !MouselookActive) return false;
            if (FreeCursorHeld() || UmmWindowOpen()) return false;
            if (!UIGate.PlainSurface()) return false;
            if (CameraRig_UpdateInternal_Patch.InCutscene()) return false;
            if (CameraRig_UpdateInternal_Patch.InDialogMode()) return false;    // WotR routes dialogue through GameModeType.Dialog; yield the cursor for it (harmless on RT, which yields via the cutscene path)
            if (CameraRig_UpdateInternal_Patch.InMenuMode()) return false;      // esc/pause menu, full-screen UI, bug report, photo mode, rest - the cursor is needed there
            if (CameraRig_UpdateInternal_Patch.InGamepadMode()) return false;   // stick can't drive mouse axes; let the game's native look run
            return true;
        }

        // Lock the cursor to centre while engaged; release it (once) when we stop. Re-asserts each
        // frame so the game cannot quietly steal it back while mouselook is meant to be holding it.
        static void UpdateCursorLock()
        {
            bool engaged = MouselookEngaged();

            if (engaged)
            {
                AssertCursorHidden();
                if (!_weLockedCursor) MouselookJustEngaged = true;   // ignore the delta accrued before the lock
                _weLockedCursor = true;
            }
            else if (_weLockedCursor)
            {
                // Release immediately. Disengage is driven by deliberate, multi-frame states (free-cursor key held,
                // dialogue, UI, gamepad) - not 1-frame noise - so there's nothing to debounce, and holding through
                // a free-cursor press would fight the game's cursor reveal and flicker.
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (_cursorBlanked) { Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); _cursorBlanked = false; }   // restore; game re-skins its cursor on the next update
                _weLockedCursor = false;
            }
            CursorLocked = engaged;
        }

        // A fully transparent 2x2 cursor texture, built once. Swapped in while mouselook holds the cursor.
        static Texture2D BlankCursorTex()
        {
            if (_blankCursor == null)
            {
                _blankCursor = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                var clear = new Color(0f, 0f, 0f, 0f);
                _blankCursor.SetPixels(new[] { clear, clear, clear, clear });
                _blankCursor.Apply();
            }
            return _blankCursor;
        }

        static void AssertCursorHidden()
        {
            if (Cursor.lockState != CursorLockMode.Locked) Cursor.lockState = CursorLockMode.Locked;
            if (Cursor.visible) Cursor.visible = false;
            // WotR only: belt against the UI framework re-showing the hardware cursor on stray frames (a
            // framerate-tied race we can't reliably out-order, and whose per-frame writer lives outside this
            // assembly): a transparent cursor texture renders nothing regardless of visible/lockState, so any
            // stray show is invisible. The game overrides it on hover; we re-apply it each phase we run, and
            // restore the game's own on release. RT never showed the cursor through the crosshair (lockState
            // Locked sufficed), so we leave its cursor texture untouched - no swap, nothing to restore.
            if (Compat.Ui == Compat.UiKind.WotR)
            {
                Cursor.SetCursor(BlankCursorTex(), Vector2.zero, CursorMode.Auto);
                _cursorBlanked = true;
            }
        }

        // Re-assert the hardware cursor hold after the game's own cursor logic. Called from a high-execution-order
        // LateUpdate and from an end-of-frame coroutine (the game's UpdateCursorMode coroutine sets lockState at
        // WaitForEndOfFrame, after LateUpdate), so we re-hide in whichever phase the game last touched it.
        internal static void CursorLateAssert()
        {
            try { if (CursorLocked) AssertCursorHidden(); } catch { }
        }

        // Strip any non-ours prefix off CameraZoom.TickZoom - that is ToyBox's FOV
        // override, which otherwise ignores the FovMin/FovMax our zoom range sets.
        // ToyBox's rig patches (Mouse3 pitch / Ctrl+Mouse3 elevation) are deliberately
        // left in place: RT has no native pitch, so we rely on ToyBox for it.
        // Retries briefly so it is load-order independent.
        static void SuppressForeignCameraPatches()
        {
            if (_suppressSettled || HarmonyInst == null) return;
            _suppressElapsed += Time.unscaledDeltaTime;

            var toRemove = new List<KeyValuePair<MethodBase, MethodInfo>>();
            foreach (var sig in CameraMethods)
            {
                MethodBase m;
                try { m = AccessTools.Method(sig); } catch { m = null; }
                if (m == null) continue;
                var info = Harmony.GetPatchInfo(m);
                if (info?.Prefixes == null) continue;
                foreach (var p in info.Prefixes)
                    if (p.owner != HarmonyInst.Id)
                        toRemove.Add(new KeyValuePair<MethodBase, MethodInfo>(m, p.PatchMethod));
            }

            foreach (var kv in toRemove)
            {
                try { HarmonyInst.Unpatch(kv.Key, kv.Value); Log?.Log("Released camera: removed a foreign patch from " + kv.Key.Name + "."); }
                catch (Exception e) { Log?.Error("Unpatch failed: " + e.Message); }
            }

            if (toRemove.Count > 0 || _suppressElapsed > 12f) _suppressSettled = true;
        }

        // ---- Presets ----
        static void CaptureView(CameraView v)
        {
            if (CurrentRig == null) return;
            try
            {
                var t = Traverse.Create(CurrentRig);
                Vector3 tr = t.Field("m_TargetRotate").GetValue<Vector3>();
                v.Pitch = tr.x;

                var zoom = t.Property("CameraZoom").GetValue<object>();
                if (zoom != null)
                    v.Zoom = Traverse.Create(zoom).Field("m_PlayerScrollPosition").GetValue<float>();

                v.IsSet = true;
                Log?.Log("Saved view: pitch " + v.Pitch.ToString("0.#") + ", zoom " + v.Zoom.ToString("0.##") + ", nearclip " + v.NearClip.ToString("0.0#") + ", pivot " + v.PivotHeight.ToString("0.#") + ", shoulder " + v.Shoulder.ToString("0.#") + ".");
            }
            catch (Exception e) { Log?.Error("Saving view failed: " + e); }
        }

        static void ApplyView(CameraView v)
        {
            if (CurrentRig == null || v == null || !v.IsSet) return;
            if (!_vanillaCaptured) CaptureVanillaState(CurrentRig);   // grab the stock camera before the first view overwrites it
            try
            {
                var t = Traverse.Create(CurrentRig);

                // Pitch: set the rig's target rotation and snap the transform to match,
                // keeping the current yaw so facing is preserved.
                var trF = t.Field("m_TargetRotate");
                Vector3 tr = trF.GetValue<Vector3>();
                tr.x = v.Pitch;
                tr.z = 0f;
                trF.SetValue(tr);
                var comp = CurrentRig as Component;
                if (comp != null) comp.transform.rotation = Quaternion.Euler(tr);

                // Zoom: write the scroll position (and the smooth value so it snaps).
                var zoom = t.Property("CameraZoom").GetValue<object>();
                if (zoom != null)
                {
                    var tz = Traverse.Create(zoom);
                    SetFloat(tz, "m_PlayerScrollPosition", v.Zoom);
                    SetFloat(tz, "m_ScrollPosition", v.Zoom);
                    SetFloat(tz, "m_SmoothScrollPosition", v.Zoom);
                }

                // Mouselook follows the applied view: a flagged view enters it, a normal view leaves it.
                MouselookActive = v.Mouselook;
                // We just seated the camera by hand; clear the mouselook seat so the next frame re-reads pitch
                // from what we applied (and the exit-sync can't stamp the old mouselook pitch back over it).
                CameraRig_UpdateInternal_Patch.ResetMouselookSeat();
                _gpPitchActive = false;   // re-seat the gamepad pitch-hold too, so a re-stamp re-establishes the view on a pad
            }
            catch (Exception e) { Log?.Error("Applying view failed: " + e); }
        }

        // True when the rig's pitch and zoom still match view v within tolerance - i.e. the on-load camera
        // restore has stopped overwriting our seated values. Drives the on-load hold's "settled" exit. Reads the
        // same fields ApplyView writes (m_TargetRotate pitch, the player scroll position); both games expose them.
        static bool ViewSeatHeld(CameraView v)
        {
            if (CurrentRig == null || v == null) return false;
            try
            {
                var t = Traverse.Create(CurrentRig);
                Vector3 tr = t.Field("m_TargetRotate").GetValue<Vector3>();
                if (Mathf.Abs(Mathf.DeltaAngle(tr.x, v.Pitch)) > 0.5f) return false;
                var zoom = t.Property("CameraZoom").GetValue<object>();
                if (zoom != null)
                {
                    float sp = Traverse.Create(zoom).Field("m_PlayerScrollPosition").GetValue<float>();
                    if (Mathf.Abs(sp - v.Zoom) > 0.01f) return false;
                }
                return true;
            }
            catch { return false; }
        }

        internal static void SetFloat(Traverse owner, string field, float val)
        {
            var f = owner.Field(field);
            if (f.FieldExists()) f.SetValue(val);
        }

        // Record the live camera (pitch + zoom) as the vanilla baseline. Called from the rig postfix while no
        // preset is active and we're in normal gameplay, so it always reflects the player's own free camera.
        internal static void CaptureVanillaState(object rig)
        {
            if (rig == null) return;
            try
            {
                var t = Traverse.Create(rig);
                Vector3 tr = t.Field("m_TargetRotate").GetValue<Vector3>();
                _vanillaPitch = tr.x;
                _vanillaRoll  = tr.z;
                var zoom = t.Property("CameraZoom").GetValue<object>();
                if (zoom != null)
                {
                    var tz = Traverse.Create(zoom);
                    _vanillaZoomPlayer   = tz.Field("m_PlayerScrollPosition").GetValue<float>();
                    _vanillaScroll       = tz.Field("m_ScrollPosition").GetValue<float>();
                    _vanillaSmoothScroll = tz.Field("m_SmoothScrollPosition").GetValue<float>();
                }
                _vanillaCaptured = true;
            }
            catch (Exception e) { Log?.Error("Capturing vanilla camera state failed: " + e); }
        }

        // Toggle target: hand the rig back to the vanilla camera. Leaves the active view at 0, which lets the
        // per-frame loop relax the focus offset and per-view clip on its own; here we additionally snap pitch and
        // zoom back to the recorded baseline (keeping the current yaw) and drop mouselook / the pad pitch-hold.
        // The global zoom range and pitch band stay as the user set them - they govern available range, not where
        // the camera rests, and are re-asserted every frame regardless.
        static void ApplyVanilla()
        {
            _activeView = 0;
            MouselookActive = false;
            if (CurrentRig != null && _vanillaCaptured)
            {
                try
                {
                    var t = Traverse.Create(CurrentRig);
                    var trF = t.Field("m_TargetRotate");
                    Vector3 tr = trF.GetValue<Vector3>();
                    tr.x = _vanillaPitch;   // yaw (tr.y) left as-is so facing is preserved
                    tr.z = _vanillaRoll;
                    trF.SetValue(tr);
                    var comp = CurrentRig as Component;
                    if (comp != null) comp.transform.rotation = Quaternion.Euler(tr);

                    var zoom = t.Property("CameraZoom").GetValue<object>();
                    if (zoom != null)
                    {
                        var tz = Traverse.Create(zoom);
                        SetFloat(tz, "m_PlayerScrollPosition", _vanillaZoomPlayer);
                        SetFloat(tz, "m_ScrollPosition",       _vanillaScroll);
                        SetFloat(tz, "m_SmoothScrollPosition", _vanillaSmoothScroll);
                    }
                }
                catch (Exception e) { Log?.Error("Applying vanilla camera failed: " + e); }
            }
            CameraRig_UpdateInternal_Patch.ResetMouselookSeat();
            _gpPitchActive = false;
        }

        // System/sector map handoff. Entering the Koronus Expanse sector map (GlobalMap) or an in-system map
        // (StarSystem) hands the shared rig to the game's own map camera, but a passive stand-down can leave the
        // active view's pitch/zoom sitting on the rig (the "StarSystem pitch jump"). So on the way in we actively
        // drop to the stock camera, remember which view was active, and re-apply it once we're back in ordinary
        // surface gameplay. The restore is gated on PlainSurface(), not merely "not in map mode", so a cutscene
        // played over the map (CutsceneGlobalMap flips InMapMode off) can't trigger a premature re-stamp.
        static void TickMapViewHandoff()
        {
            bool inMap = CameraRig_UpdateInternal_Patch.InMapMode();

            if (inMap && !_wasInMapMode)
            {
                // Don't clobber a pending restore if a map-cutscene blinked InMapMode off and back while on the map.
                if (!_mapRestorePending)
                {
                    _viewBeforeMap = _activeView;
                    if (_activeView != 0)
                    {
                        ApplyVanilla();          // map opens on the stock camera, not the view's framing
                        _mapRestorePending = true;
                    }
                }
            }
            else if (_mapRestorePending && !inMap && UIGate.PlainSurface())
            {
                if (_viewBeforeMap == 1) { ApplyView(settings.View1); _activeView = 1; }
                else if (_viewBeforeMap == 2) { ApplyView(settings.View2); _activeView = 2; }
                _mapRestorePending = false;
            }

            _wasInMapMode = inMap;
        }

        // A scripted in-dialogue shot is taking the camera. The base game has no pitch control and composes
        // these shots assuming the camera sits at its standard level (0) pitch; our view pitch would tilt the
        // shot. So as we hand control over, put the rig pitch back to 0 and leave the facing (yaw) alone.
        // Nothing re-applies the view's pitch while the shot holds (mouselook is released during dialogue and
        // ApplyView is one-shot), so a single zero sticks; the dialogue-end re-stamp restores the view after.
        internal static void ZeroRigPitchForCutscene()
        {
            if (CurrentRig == null) return;
            try
            {
                var t = Traverse.Create(CurrentRig);
                var trF = t.Field("m_TargetRotate");
                Vector3 tr = trF.GetValue<Vector3>();
                tr.x = 0f; tr.z = 0f;
                trF.SetValue(tr);
                var comp = CurrentRig as Component;
                if (comp != null) comp.transform.rotation = Quaternion.Euler(tr);
            }
            catch (Exception e) { Log?.Error("Zero rig pitch for cutscene failed: " + e); }
        }

        // Current right-stick Y for pitch, or zero when the stick is centred. Rewired fires OnMoveRightStick
        // only while the stick is deflected, so a stamp older than this frame means "centred"; a small deadzone
        // guards against residual drift.
        internal static float GamepadPitchInput()
        {
            if (Time.frameCount - GpRightStickFrame > 1) return 0f;
            float y = GpRightStickY;
            return Mathf.Abs(y) < 0.12f ? 0f : y;
        }

        // The pitch-hold runs only for View 1 on a pad with a set view.
        static bool GamepadPitchHoldActive()
        {
            return _activeView == 1 && settings != null && settings.View1 != null && settings.View1.IsSet
                && CameraRig_UpdateInternal_Patch.InGamepadMode();
        }

        // Gamepad View-1 pitch-hold. Mouselook stands aside on a pad, so the mod owns View 1's pitch here the
        // same way mouselook owns it with a mouse: seat on the current pitch, then let the right stick Y (remapped
        // from zoom) adjust a held value we re-assert every frame, keeping the game's stick-X yaw. Zoom is pinned
        // to the view so the remapped Y no longer zooms. Because pitch and zoom are re-asserted every frame, this
        // is also the backstop that keeps View 1 framed across dialogue and area transitions - the role mouselook
        // plays in mouse mode, which a pad otherwise has nothing to fill. Stands down during scripted shots, the
        // map, cutscenes and dialogue (as mouselook does), so those keep their own camera; ApplyView re-seats the
        // hold so a post-dialogue / on-load re-stamp re-establishes the view.
        internal static void TickGamepadPitchHold(bool hardBind)
        {
            if (hardBind || CurrentRig == null || !GamepadPitchHoldActive()
                || CameraRig_UpdateInternal_Patch.InCutscene()
                || CameraRig_UpdateInternal_Patch.InDialogMode()
                || CameraRig_UpdateInternal_Patch.InMapMode()
                || CutsceneCameraGate.CameraCutsceneActive())
            {
                _gpPitchActive = false;
                return;
            }
            try
            {
                var t = Traverse.Create(CurrentRig);
                var trf = t.Field("m_TargetRotate");
                Vector3 tr = trf.GetValue<Vector3>();
                if (!_gpPitchActive)
                {
                    _gpPitch = tr.x;          // seat on the current pitch (the freshly-applied view) - no snap
                    _gpPitchActive = true;
                }
                else
                {
                    float sensY = Mathf.Max(0.01f, settings.MouselookSensY);
                    float dy = GamepadPitchInput() * sensY * GamepadPitchRate * Time.unscaledDeltaTime
                               * (settings.GamepadInvertPitch ? -1f : 1f);   // off keeps the tuned default (+1 = stick up looks up, given the stick's Y sign); on inverts
                    float loP = MouselookPitchMin;
                    float hiP = Mathf.Clamp(settings.MaxPitchAngle, PitchHardMin, PitchHardMax);
                    if (hiP < loP) { float tmp = loP; loP = hiP; hiP = tmp; }
                    _gpPitch = Mathf.Clamp(_gpPitch + dy, loP, hiP);
                }
                tr.x = _gpPitch; tr.z = 0f;   // keep .y (the game's stick-X yaw)
                trf.SetValue(tr);
                var comp = CurrentRig as Component;
                if (comp != null) comp.transform.rotation = Quaternion.Euler(_gpPitch, comp.transform.eulerAngles.y, 0f);

                // Pin zoom to the view so the remapped stick Y can't also zoom.
                var zoom = t.Property("CameraZoom").GetValue<object>();
                if (zoom != null)
                {
                    var tz = Traverse.Create(zoom);
                    SetFloat(tz, "m_PlayerScrollPosition", settings.View1.Zoom);
                    SetFloat(tz, "m_ScrollPosition", settings.View1.Zoom);
                    SetFloat(tz, "m_SmoothScrollPosition", settings.View1.Zoom);
                }
            }
            catch (Exception e) { Log?.Error("Gamepad pitch-hold failed: " + e); }
        }

        // ---- Gamepad on-load direct character control (the L3 "Character control" toggle) -----------------
        // The gameplay input layer carries a CursorEnabled flag (on Owlcat's GamepadInput.InputLayer base):
        // true = the left stick drives the cursor (stock), false = it drives the selected character directly.
        // SurfaceMainInputLayer.SwitchCursorEnabled() is the L3 handler that flips it. We read the current
        // state and only flip when it's still in cursor mode, so we can never turn an already-direct mode off.
        static bool _dcInit;
        static Type _dcLayerType;
        static PropertyInfo _dcInstanceProp;
        static MethodInfo _dcSwitch;

        static void InitDirectControlReflection()
        {
            if (_dcInit) return;
            _dcInit = true;
            _dcLayerType = AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.View.Surface.InputLayers.SurfaceMainInputLayer")
                        ?? AccessTools.TypeByName("Kingmaker.UI._ConsoleUI.InputLayers.InGameLayer.InGameInputLayer");
            if (_dcLayerType != null)
            {
                _dcInstanceProp = AccessTools.Property(_dcLayerType, "Instance");   // RT static singleton; null on WotR (use CachedInputLayer)
                _dcSwitch = AccessTools.Method(_dcLayerType, "SwitchCursorEnabled");
            }
        }

        // Reads the layer's CursorEnabled (property, else backing field m_CursorEnabled on the base). Returns
        // false if neither could be resolved, so the caller leaves the control mode untouched.
        static bool TryReadCursorEnabled(object layer, out bool cursorEnabled)
        {
            cursorEnabled = false;
            var tp = Traverse.Create(layer).Property("CursorEnabled");
            if (tp.PropertyExists()) { cursorEnabled = tp.GetValue<bool>(); return true; }
            var tf = Traverse.Create(layer).Field("m_CursorEnabled");
            if (tf.FieldExists()) { cursorEnabled = tf.GetValue<bool>(); return true; }
            return false;
        }

        // Returns true once it has resolved the input layer and acted (or confirmed it was already direct);
        // returns false only when the layer isn't available yet, so the caller can retry next frame.
        static bool TryEngageDirectControlOnFoot()
        {
            try
            {
                InitDirectControlReflection();
                if (_dcLayerType == null || _dcSwitch == null)
                {
                    Log?.Log("Gamepad direct-control: input-layer API not found; skipping.");
                    return true;   // can't resolve the API; don't spin retries
                }
                // RT exposes a static Instance; WotR has none, so fall back to the layer cached from OnUpdate.
                var layer = _dcInstanceProp != null ? _dcInstanceProp.GetValue(null) : CachedInputLayer;
                if (layer == null) return false;   // on-foot input layer not up yet; allow a retry

                bool cursorEnabled;
                if (!TryReadCursorEnabled(layer, out cursorEnabled))
                {
                    Log?.Log("Gamepad direct-control: couldn't read CursorEnabled; leaving control mode untouched.");
                    return true;
                }
                if (cursorEnabled)
                {
                    _dcSwitch.Invoke(layer, null);   // the game's own L3 flip -> direct character control
                    Log?.Log("Gamepad: engaged direct character control on load (was cursor mode).");
                }
                else
                {
                    Log?.Log("Gamepad: already in direct character control at load; left as-is.");
                }
                return true;
            }
            catch (Exception e) { Log?.Error("Gamepad direct-control on load failed: " + e); return true; }
        }

        // ---- Gamepad free-aim cursor (per-view) -----------------------------------------------------------
        // In RT surface cursor control, the stock left stick scrolls the camera and the on-screen pointer is
        // pinned to centre (the game's SurfaceMainInputLayer.UpdateLeftStickMovement takes its SetToCenter
        // branch). With a view's PadFreeAimCursor ticked we instead leave the camera where character control
        // left it and drive the pointer directly: ConsoleCursor.MoveCursor moves the same pointer the mouse
        // uses, clamped to screen, so interactions resolve at the cursor exactly as a mouse click would. Only
        // the left-stick path is touched; the right stick keeps its normal camera rotate/pitch.
        static bool _ccInit;
        static Type _ccType;
        static PropertyInfo _ccInstanceProp;
        static MethodInfo _ccMoveCursor;

        static void InitConsoleCursorReflection()
        {
            if (_ccInit) return;
            _ccInit = true;
            _ccType = AccessTools.TypeByName("Kingmaker.UI.Pointer.ConsoleCursor");
            if (_ccType != null)
            {
                _ccInstanceProp = AccessTools.Property(_ccType, "Instance");      // static singleton
                _ccMoveCursor   = AccessTools.Method(_ccType, "MoveCursor");   // single overload on ConsoleCursor; takes the stick delta (Vector2)
            }
        }

        // The active view's free-aim flag (false on Vanilla / no active view).
        static bool PadFreeAimViewEnabled()
        {
            if (settings == null) return false;
            if (_activeView == 1) return settings.View1 != null && settings.View1.PadFreeAimCursor;
            if (_activeView == 2) return settings.View2 != null && settings.View2.PadFreeAimCursor;
            return false;
        }

        // True once it has handled the left stick for free-aim (the caller then skips the stock camera-pan
        // path). Active only when: the mod is on, the active view's free-aim is ticked, we're on a pad in
        // plain surface gameplay, and the layer is in cursor control (CursorEnabled). In direct character
        // control, on Vanilla, or off a pad, this returns false and the stock behaviour runs untouched.
        internal static bool TryPadFreeAimLeftStick(object layer)
        {
            try
            {
                if (!Active || settings == null || layer == null) return false;
                if (!PadFreeAimViewEnabled()) return false;
                if (!CameraRig_UpdateInternal_Patch.InGamepadMode()) return false;
                if (!UIGate.PlainSurface()) return false;
                if (!TryReadCursorEnabled(layer, out bool cursorEnabled) || !cursorEnabled) return false;

                InitConsoleCursorReflection();
                if (_ccInstanceProp == null || _ccMoveCursor == null) return false;
                var cursor = _ccInstanceProp.GetValue(null);
                if (cursor == null) return false;

                Vector2 stick = Traverse.Create(layer).Field("m_LeftStickVector").GetValue<Vector2>();
                _ccMoveCursor.Invoke(cursor, new object[] { stick });   // moves the pointer by stick*MoveSpeed, clamped to screen
                return true;
            }
            catch (Exception e) { Log?.Error("Gamepad free-aim left stick failed: " + e); return false; }
        }

        // The toggle key steps through the targets the player ticked, in the fixed order
        // View 1 -> View 2 -> Vanilla, wrapping around. A preset only takes part when it's actually saved;
        // Vanilla (0) always takes part when ticked. If the current state isn't in the ring (e.g. its box was
        // just unticked), we jump to the first ticked target.
        static void ToggleViews()
        {
            if (settings == null) return;

            var ring = new List<int>(3);
            if (settings.CycleView1 && settings.View1 != null && settings.View1.IsSet) ring.Add(1);
            if (settings.CycleView2 && settings.View2 != null && settings.View2.IsSet) ring.Add(2);
            if (settings.CycleVanilla) ring.Add(0);

            if (ring.Count == 0) { Log?.Log("Toggle pressed, but no cycle targets are enabled (or no view is saved)."); return; }

            int idx  = ring.IndexOf(_activeView);
            int next = (idx < 0) ? ring[0] : ring[(idx + 1) % ring.Count];

            if (next == 0) { ApplyVanilla(); return; }
            var v = (next == 1) ? settings.View1 : settings.View2;
            ApplyView(v);
            _activeView = next;
        }

        // Capture a gamepad button for the view-toggle bind. IMGUI's keyboard event (CaptureBindKey) never sees
        // JoystickButton presses, so while armed (target 4) we poll them here each frame; any keyboard key or
        // Escape cancels the bind through CaptureBindKey instead.
        static void ScanGamepadBind()
        {
            for (int b = (int)KeyCode.JoystickButton0; b <= (int)KeyCode.JoystickButton19; b++)
            {
                if (Input.GetKeyDown((KeyCode)b))
                {
                    settings.GamepadToggleKey = b;
                    settings.Save(ModEntry);
                    _bindingTarget = 0;
                    return;
                }
            }
        }

        // ---- GUI ----
        // Foldout state for the per-view setting blocks (UI only; defaults open, resets per session).
        static bool _view1Expanded = true, _view2Expanded = true;

        static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            CaptureBindKey();   // must run before the widgets consume the event

            GUILayout.BeginVertical();

            if (ToyBoxProbe.CtrlElevationOn() == true)
            {
                var warnPrev = GUI.color;
                GUI.color = new Color(1f, 0.55f, 0.2f);
                GUILayout.Label(L("\u26a0  ToyBox: \"Ctrl + Mouse3 Drag To Adjust Camera Elevation\" is ON \u2013 it makes the camera load at a map origin and pan from there. Turn it off in ToyBox \u2192 Camera. (The Mouse3-aim option is fine to keep; pitch needs it.)"));
                GUI.color = warnPrev;
                GUILayout.Space(8f);
            }

            // --- Presets (cross-view) ---
            GUILayout.Label(L("Camera presets:"));
            GUILayout.BeginHorizontal();
            GUILayout.Label("     " + L("Toggle key:"), GUILayout.Width(150f));
            GUILayout.Label(KeyName(settings.ToggleKey), GUILayout.Width(110f));
            if (GUILayout.Button(_bindingTarget == 3 ? L("press a key\u2026") : L("Bind"), GUILayout.Width(110f))) _bindingTarget = 3;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("     " + L("Gamepad toggle:"), GUILayout.Width(150f));
            GUILayout.Label(KeyName(settings.GamepadToggleKey), GUILayout.Width(110f));
            if (GUILayout.Button(_bindingTarget == 4 ? L("press a pad button\u2026") : L("Bind"), GUILayout.Width(110f))) _bindingTarget = 4;
            if (GUILayout.Button(L("Clear"), GUILayout.Width(70f))) { settings.GamepadToggleKey = (int)KeyCode.None; settings.Save(modEntry); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("     " + L("Toggle cycles:"), GUILayout.Width(150f));
            settings.CycleView1   = GUILayout.Toggle(settings.CycleView1,   L("View 1"),  GUILayout.Width(80f));
            settings.CycleView2   = GUILayout.Toggle(settings.CycleView2,   L("View 2"),  GUILayout.Width(80f));
            settings.CycleVanilla = GUILayout.Toggle(settings.CycleVanilla, L("Vanilla"), GUILayout.Width(90f));
            GUILayout.EndHorizontal();
            GUILayout.Label("     " + L("\u2013 the toggle key steps through the ticked targets in order; Vanilla is the game's own camera."));
            settings.GamepadInvertPitch = GUILayout.Toggle(settings.GamepadInvertPitch, "     " + L("Invert gamepad pitch  \u2013 flip the right-stick up/down look direction (View 1 on a pad)"));
            settings.DirectControlOnGamepadLoad = GUILayout.Toggle(settings.DirectControlOnGamepadLoad, "     " + L("Direct character control on gamepad load  \u2013 the left stick moves your character (click the stick to toggle)"));
            settings.ApplyView1OnLoad = GUILayout.Toggle(settings.ApplyView1OnLoad, "  " + L("Apply View 1 on game load (area-to-area transitions keep the current camera)"));

            GUILayout.Space(10f);

            // --- Per-view blocks: everything specific to a view lives in one place ---
            DrawViewBlock(L("View 1"), settings.View1, 1, ref _view1Expanded);
            DrawViewBlock(L("View 2"), settings.View2, 2, ref _view2Expanded);

            GUILayout.Space(14f);

            // --- Camera framing (global): the master toggle that enables each view's pivot/shoulder/dolly ---
            GUILayout.BeginHorizontal();
            settings.FramingEnabled = GUILayout.Toggle(settings.FramingEnabled, "  " + L("Focus offset  (enables each view's pivot + shoulder + dolly)"), GUILayout.Width(360f));
            settings.FramingPauseInCutscenes = GUILayout.Toggle(settings.FramingPauseInCutscenes, L("pause in cutscenes"));
            GUILayout.EndHorizontal();
            GUILayout.Label("     " + L("Ctrl+scroll live-tunes the active view's pivot height; Ctrl+Shift+scroll its dolly. Pivot is world-up, so it holds through turns and never jumps when mouselook toggles."));
            GUILayout.BeginHorizontal();
            GUILayout.Label("     " + L("In dialogue:"), GUILayout.Width(150f));
            settings.DialogFraming = (DialogFramingMode)GUILayout.Toolbar((int)settings.DialogFraming, new[] { L("Off"), L("Lift only"), L("Full tactical") }, GUILayout.Width(360f));
            GUILayout.EndHorizontal();
            GUILayout.Label("     " + L("dialogue framing \u2013 Off hands off to the game; Lift only keeps a gentle raise with no zoom; Full tactical holds an over-the-shoulder framing while the game still frames the speakers left/right. Set the Full-tactical height (and an optional fixed zoom) per view in each view's block above."));

            GUILayout.Space(14f);

            // --- Pitch range (global) ---
            settings.PitchRangeEnabled = GUILayout.Toggle(settings.PitchRangeEnabled, "  " + L("Pitch range  (Mouse3 drag)"));
            GUILayout.Label("     " + L("flattest: ") + settings.MinPitchAngle.ToString("0") + "\u00b0   " + L("\u2013 lower = more horizontal"));
            settings.MinPitchAngle = Snap(GUILayout.HorizontalSlider(settings.MinPitchAngle, MinPitchLo, MinPitchHi), PitchStep);
            GUILayout.Label("     " + L("steepest: ") + settings.MaxPitchAngle.ToString("0") + "\u00b0   " + L("\u2013 higher = more top-down"));
            settings.MaxPitchAngle = Snap(GUILayout.HorizontalSlider(settings.MaxPitchAngle, MaxPitchLo, MaxPitchHi), PitchStep);

            GUILayout.Space(8f);

            // --- Zoom limits (global) ---
            GUILayout.BeginHorizontal();
            settings.ZoomLimitsEnabled = GUILayout.Toggle(settings.ZoomLimitsEnabled, "  " + L("Extend zoom limits  (scroll)"), GUILayout.Width(250f));
            settings.ZoomPauseInCutscenes = GUILayout.Toggle(settings.ZoomPauseInCutscenes, L("pause in cutscenes"));
            GUILayout.EndHorizontal();
            GUILayout.Label("     " + L("zoom-out  \u00d7") + settings.ZoomOutFactor.ToString("0.0") + "   " + L("\u2013 pull back further"));
            settings.ZoomOutFactor = Snap(GUILayout.HorizontalSlider(settings.ZoomOutFactor, ZoomOutMin, ZoomOutMax), ZoomStep);
            GUILayout.Label("     " + L("zoom-in  \u00d7") + settings.ZoomInFactor.ToString("0.0") + "   " + L("\u2013 get much closer"));
            settings.ZoomInFactor = Snap(GUILayout.HorizontalSlider(settings.ZoomInFactor, ZoomInMin, ZoomInMax), ZoomStep);

            GUILayout.Space(8f);

            // --- Mouselook (global tuning; tick a view's "mouselook" in its block above to use it) ---
            GUILayout.Label(L("Mouselook  \u2013 tick a view's \"mouselook\" box in its block above to make it drive yaw/pitch with the mouse (cursor locked to centre)"));
            GUILayout.Label("     " + L("X (yaw) sensitivity: ") + settings.MouselookSensitivity.ToString("0.0"));
            settings.MouselookSensitivity = Snap(GUILayout.HorizontalSlider(settings.MouselookSensitivity, MouseSensMin, MouseSensMax), MouseSensStep);
            GUILayout.Label("     " + L("Y (pitch) sensitivity: ") + settings.MouselookSensY.ToString("0.0"));
            settings.MouselookSensY = Snap(GUILayout.HorizontalSlider(settings.MouselookSensY, MouseSensMin, MouseSensMax), MouseSensStep);
            GUILayout.BeginHorizontal();
            settings.MouselookInvertY = GUILayout.Toggle(settings.MouselookInvertY, L("invert Y"), GUILayout.Width(120f));
            settings.MouselookCrosshair = GUILayout.Toggle(settings.MouselookCrosshair, L("centre crosshair"), GUILayout.Width(170f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("     " + L("hold-to-free-cursor:"), GUILayout.Width(190f));
            GUILayout.Label(KeyName(settings.FreeCursorKey), GUILayout.Width(110f));
            if (GUILayout.Button(_bindingTarget == 5 ? L("press a key\u2026") : L("Bind"), GUILayout.Width(110f))) _bindingTarget = 5;
            GUILayout.EndHorizontal();
            GUILayout.Label("     " + L("hold that key for a normal pointer; dialogue, menus, the global map and this panel free it automatically."));

            GUILayout.Space(14f);

            // --- Interface (global) ---
            GUILayout.Label(L("Interface"));
            settings.HideOffscreenUnitMarkers = GUILayout.Toggle(settings.HideOffscreenUnitMarkers, "  " + L("Hide off-screen character markers  (edge portrait pointers)"));

            GUILayout.Space(10f);
            if (GUILayout.Button(L("Reset pitch & zoom (keeps presets)"), GUILayout.Width(330f)))
            {
                settings.MinPitchAngle = 5f;
                settings.MaxPitchAngle = 89f;
                settings.ZoomOutFactor = 1f;
                settings.ZoomInFactor = 4f;
            }

            GUILayout.Space(12f);
            GUILayout.Label(L("Scripted (hard-bound) camera shots are always left alone, regardless of these settings."));

            GUILayout.EndVertical();
        }

        // Draws one view's collapsible block: a foldout header plus, when open, every setting that
        // belongs to that view. Both views call this, so a new per-view control is added in one place.
        static void DrawViewBlock(string label, CameraView v, int idx, ref bool expanded)
        {
            // Header: foldout toggle + name + saved/empty status.
            GUILayout.BeginHorizontal();
            string status = v.IsSet ? (L("saved \u2013 pitch ") + v.Pitch.ToString("0") + "\u00b0" + L(", zoom ") + v.Zoom.ToString("0.##")) : L("empty");
            if (GUILayout.Button((expanded ? "\u25bc  " : "\u25b6  ") + label, GUILayout.Width(110f))) expanded = !expanded;
            GUILayout.Label(status, GUILayout.Width(240f));
            GUILayout.EndHorizontal();
            if (!expanded) return;

            // Capture / apply / hotkey / mouselook for this view.
            GUILayout.BeginHorizontal();
            GUILayout.Space(20f);
            if (GUILayout.Button(idx == 1 ? L("Set View 1") : L("Set View 2"), GUILayout.Width(95f)))
            { CaptureView(v); _activeView = idx; settings.Save(ModEntry); }
            GUI.enabled = v.IsSet;
            if (GUILayout.Button(L("Apply"), GUILayout.Width(70f))) { ApplyView(v); _activeView = idx; }
            GUI.enabled = true;
            GUILayout.Label(L("key: ") + KeyName(idx == 1 ? settings.SetView1Key : settings.SetView2Key), GUILayout.Width(120f));
            if (GUILayout.Button(_bindingTarget == idx ? L("press\u2026") : L("Bind"), GUILayout.Width(80f))) _bindingTarget = idx;
            v.Mouselook = GUILayout.Toggle(v.Mouselook, L("mouselook"), GUILayout.Width(100f));
            GUILayout.EndHorizontal();

            // Zoom (camera scroll position): a slider so it can be dialled directly - useful on a pad, where the
            // right stick Y now drives pitch and can no longer zoom. Live for View 1 on a pad (the pitch-hold pins
            // it every frame); otherwise it takes effect the next time the view is applied.
            GUILayout.BeginHorizontal();
            GUILayout.Label("     " + L("zoom: ") + (v.Zoom * 100f).ToString("0") + "%", GUILayout.Width(185f));
            v.Zoom = Snap(GUILayout.HorizontalSlider(v.Zoom, ViewZoomMin, ViewZoomMax, GUILayout.Width(235f)), ViewZoomStep);
            GUILayout.EndHorizontal();

            // Pitch (the rig's target tilt) as a slider, plus a lock that re-asserts it every frame so a cutscene
            // or transition can't leave the camera flattened. The lock is greyed on a mouselook view (the mouse
            // owns pitch there); we don't write the stored flag back while it's greyed, so toggling mouselook
            // doesn't wipe the preference.
            GUILayout.BeginHorizontal();
            GUILayout.Label("     " + L("pitch: ") + v.Pitch.ToString("0") + "\u00b0", GUILayout.Width(185f));
            v.Pitch = Snap(GUILayout.HorizontalSlider(v.Pitch, MouselookPitchMin, PitchHardMax, GUILayout.Width(235f)), PitchStep);
            GUILayout.EndHorizontal();
            GUI.enabled = !v.Mouselook;
            bool lockNow = GUILayout.Toggle(v.LockPitch, "     " + L("lock pitch  \u2013 hold this tilt through cutscenes and transitions (mouselook overrides it)"));
            if (!v.Mouselook) v.LockPitch = lockNow;
            GUI.enabled = true;

            // Focus offset (pivot / shoulder / dolly) - requires the global Focus offset toggle below.
            GUILayout.Label("     " + L("focus offset \u2013 needs \"Focus offset\" enabled below:"));
            GUILayout.BeginHorizontal();
            GUILayout.Label("       " + L("pivot height: ") + v.PivotHeight.ToString("0.0"), GUILayout.Width(185f));
            v.PivotHeight = Snap(GUILayout.HorizontalSlider(v.PivotHeight, PivotMin, PivotMax, GUILayout.Width(235f)), PivotStep);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("       " + L("shoulder (0 = centred): ") + v.Shoulder.ToString("0.0"), GUILayout.Width(185f));
            v.Shoulder = Snap(GUILayout.HorizontalSlider(v.Shoulder, ShoulderMax, ShoulderMin, GUILayout.Width(235f)), ShoulderStep);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("       " + L("dolly-in: ") + v.Dolly.ToString("0.0"), GUILayout.Width(185f));
            v.Dolly = Snap(GUILayout.HorizontalSlider(v.Dolly, DollyMin, DollyMax, GUILayout.Width(235f)), DollyStep);
            GUILayout.EndHorizontal();
            // Live follow is inert in WotR (the rig exposes no Position/ViewTransform), so hide the control there.
            if (Compat.Ui != Compat.UiKind.WotR)
                v.LiveFollow = GUILayout.Toggle(v.LiveFollow, "     " + L("live follow  \u2013 lock the model in frame at close range (best on the dollied-in view - but can add movement stutter)"));

            // Full-tactical dialogue framing for this view: the height that replaces the gameplay pivot during a
            // conversation, and an optional fixed zoom (off = the view's own zoom carries through). Active only when
            // the global dialogue mode above is set to Full tactical.
            GUILayout.BeginHorizontal();
            GUILayout.Label("       " + L("dialogue height: ") + v.DialogHeight.ToString("0.0"), GUILayout.Width(185f));
            v.DialogHeight = Snap(GUILayout.HorizontalSlider(v.DialogHeight, DialogHeightMin, DialogHeightMax, GUILayout.Width(235f)), PivotStep);
            GUILayout.EndHorizontal();
            v.DialogZoomEnabled = GUILayout.Toggle(v.DialogZoomEnabled, "       " + L("fixed dialogue zoom"));
            GUI.enabled = v.DialogZoomEnabled;
            GUILayout.BeginHorizontal();
            GUILayout.Label("       " + L("dialogue zoom: ") + (v.DialogZoom * 100f).ToString("0") + "%", GUILayout.Width(185f));
            v.DialogZoom = Snap(GUILayout.HorizontalSlider(v.DialogZoom, ViewZoomMin, ViewZoomMax, GUILayout.Width(235f)), ViewZoomStep);
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            // Keyboard rotation speed for this view.
            GUILayout.BeginHorizontal();
            GUILayout.Label("     " + L("rotate speed (1 = stock): ") + v.RotateSpeedMult.ToString("0.00") + "x", GUILayout.Width(185f));
            v.RotateSpeedMult = Snap(GUILayout.HorizontalSlider(v.RotateSpeedMult, RotMultMin, RotMultMax, GUILayout.Width(235f)), RotMultStep);
            GUILayout.EndHorizontal();

            // Clip planes for this view (each independent; unticked does nothing).
            GUILayout.BeginHorizontal();
            v.NearClipEnabled = GUILayout.Toggle(v.NearClipEnabled, "  " + L("near clip"), GUILayout.Width(110f));
            GUILayout.Label(v.NearClip.ToString("0.0#"), GUILayout.Width(45f));
            v.NearClip = Snap(GUILayout.HorizontalSlider(v.NearClip, NearClipMin, NearClipMax, GUILayout.Width(235f)), NearClipStep);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            v.FarClipEnabled = GUILayout.Toggle(v.FarClipEnabled, "  " + L("far clip"), GUILayout.Width(110f));
            GUILayout.Label(v.FarClip.ToString("0"), GUILayout.Width(45f));
            v.FarClip = Snap(GUILayout.HorizontalSlider(v.FarClip, FarClipMin, FarClipMax, GUILayout.Width(235f)), FarClipStep);
            GUILayout.EndHorizontal();

            // Solid walls for this view.
            v.SolidWalls = GUILayout.Toggle(v.SolidWalls, "  " + L("solid walls  \u2013 stop walls/doors dissolving in front of the camera in this view"));

            // Gamepad free-aim cursor for this view (RT surface, cursor control only).
            v.PadFreeAimCursor = GUILayout.Toggle(v.PadFreeAimCursor, "  " + L("free-aim cursor (gamepad)  \u2013 in cursor control, the left stick moves the pointer in screen space instead of panning the camera"));

            GUILayout.Space(10f);
        }

        static void CaptureBindKey()
        {
            if (_bindingTarget == 0) return;
            var e = Event.current;
            if (e == null || e.type != EventType.KeyDown) return;
            var kc = e.keyCode;
            if (kc == KeyCode.None) return;
            if (kc != KeyCode.Escape)
            {
                switch (_bindingTarget)
                {
                    case 1: settings.SetView1Key = (int)kc; break;
                    case 2: settings.SetView2Key = (int)kc; break;
                    case 3: settings.ToggleKey = (int)kc; break;
                    case 5: settings.FreeCursorKey = (int)kc; break;
                }
            }
            _bindingTarget = 0;
            e.Use();
        }

        static string KeyName(int k) => k == (int)KeyCode.None ? "(none)" : ((KeyCode)k).ToString();

        static float Snap(float value, float step) => Mathf.Round(value / step) * step;

        // Per-view keyboard-rotation multiplier for whichever preset is currently active (1 if none).
        internal static float ActiveViewRotMult()
        {
            if (settings == null) return 1f;
            if (CameraRig_UpdateInternal_Patch.InGamepadMode()) return 1f;   // stick turns at stock speed; the slow-down is a keyboard-strafing tool
            if (_activeView == 1) return Mathf.Clamp(settings.View1.RotateSpeedMult, RotMultMin, RotMultMax);
            if (_activeView == 2) return Mathf.Clamp(settings.View2.RotateSpeedMult, RotMultMin, RotMultMax);
            return 1f;
        }

        // Per-view world-up pivot height for whichever preset is currently active (0 if none).
        internal static float ActiveViewPivotHeight()
        {
            if (settings == null) return 0f;
            if (_activeView == 1) return Mathf.Clamp(settings.View1.PivotHeight, PivotMin, PivotMax);
            if (_activeView == 2) return Mathf.Clamp(settings.View2.PivotHeight, PivotMin, PivotMax);
            return 0f;
        }

        // Per-view lateral shoulder offset for whichever preset is currently active (0 if none).
        internal static float ActiveViewShoulder()
        {
            if (settings == null) return 0f;
            if (_activeView == 1) return Mathf.Clamp(settings.View1.Shoulder, ShoulderMin, ShoulderMax);
            if (_activeView == 2) return Mathf.Clamp(settings.View2.Shoulder, ShoulderMin, ShoulderMax);
            return 0f;
        }

        // Per-view dolly-in distance (toward the focal, fixed FOV) for whichever preset is active (0 if none).
        internal static float ActiveViewDolly()
        {
            if (settings == null) return 0f;
            if (_activeView == 1) return Mathf.Clamp(settings.View1.Dolly, DollyMin, DollyMax);
            if (_activeView == 2) return Mathf.Clamp(settings.View2.Dolly, DollyMin, DollyMax);
            return 0f;
        }

        // Per-view live-follow flag for whichever preset is active (false if none). In gamepad mode View 1 always
        // locks the model in frame: the game's native controller camera lets the view pan loosely behind the
        // character as you move, which reads fine in the wide isometric View 2 but breaks the close over-the-
        // shoulder framing, so we pin it. View 2 is left on its own setting - its loose follow is wanted there.
        internal static bool ActiveViewLiveFollow()
        {
            if (settings == null) return false;
            if (_activeView == 1) return settings.View1.LiveFollow || CameraRig_UpdateInternal_Patch.InGamepadMode();
            if (_activeView == 2) return settings.View2.LiveFollow;
            return false;
        }

        // The active preset object (or null if none) - lets the scroll patch tune it live.
        internal static CameraView ActiveViewObj()
        {
            if (settings == null) return null;
            if (_activeView == 1) return settings.View1;
            if (_activeView == 2) return settings.View2;
            return null;
        }

        // Active view's pitch when a hold should be enforced this frame, else NaN. NaN when there's no active
        // view, the view isn't set, lock-pitch is off, the view uses mouselook (the mouse owns pitch), or it's
        // View 1 on a pad (the right-stick pitch-hold owns it). The caller stands it down during scripted shots.
        internal static float ActiveViewLockedPitch()
        {
            if (settings == null) return float.NaN;
            CameraView v = _activeView == 1 ? settings.View1 : (_activeView == 2 ? settings.View2 : null);
            if (v == null || !v.IsSet || !v.LockPitch || v.Mouselook) return float.NaN;
            if (_activeView == 1 && CameraRig_UpdateInternal_Patch.InGamepadMode()) return float.NaN;
            return Mathf.Clamp(v.Pitch, MouselookPitchMin, PitchHardMax);
        }

        // Active view's Full-tactical dialogue height (the old global default if no preset is active - it won't be
        // used in that case, since the focus offset stands down without an active view).
        internal static float ActiveViewDialogHeight()
        {
            if (settings == null) return 1.3f;
            if (_activeView == 1) return settings.View1.DialogHeight;
            if (_activeView == 2) return settings.View2.DialogHeight;
            return 1.3f;
        }

        // Active view's pinned dialogue zoom, or NaN when there's no active view or its fixed-zoom toggle is off.
        internal static float ActiveViewDialogZoom()
        {
            if (settings == null) return float.NaN;
            CameraView v = _activeView == 1 ? settings.View1 : (_activeView == 2 ? settings.View2 : null);
            if (v == null || !v.DialogZoomEnabled) return float.NaN;
            return Mathf.Clamp(v.DialogZoom, ViewZoomMin, ViewZoomMax);
        }

        // --- Solid walls (per view): while a view with SolidWalls set is active we hold RT's occluder
        // see-through clip off, so walls and doors in front of the camera stay solid instead of dissolving.
        // We re-assert the off state every frame (an area load restarts the clip service, which would
        // otherwise bring the see-through back), and re-enable it once when we leave such a view.
        // _occluderSuppressed tracks whether we are the ones currently holding it off, so OnToggle can
        // restore it if the mod is disabled mid-view.
        static bool _occluderSuppressed;

        // The active view wants solid walls, minus the standdowns shared by both games' suppression paths:
        // hands off on the map, during hard-bound shots, and during in-dialogue scripted camera shots.
        // Used by RT's occluder-clip hold below and by WotR's feature-flag postfix (OccludedHighlight patch).
        internal static bool SolidWallsWanted()
        {
            bool want = ActiveViewObj()?.SolidWalls ?? false;
            if (CameraRig_UpdateInternal_Patch.InMapMode()) want = false;             // hands off on the system/sector map
            if (CameraRig_UpdateInternal_Patch.LastHardBind) want = false;            // hand back during hard-bound shots (scripted cinematics)
            if (CutsceneCameraGate.CameraCutsceneActive()) want = false;              // hand back during in-dialogue scripted camera shots
            return want;
        }

        static void UpdateOccluderClip()
        {
            if (Compat.Ui == Compat.UiKind.WotR) return;   // WotR suppression is the OccludedHighlight feature-flag postfix, not RT's clip service
            bool want = SolidWallsWanted();
            if (want) { OccluderClip.SetGameClipEnabled(false); _occluderSuppressed = true; }
            else if (_occluderSuppressed) { OccluderClip.SetGameClipEnabled(true); _occluderSuppressed = false; }
        }

        static void RestoreOccluderClip()
        {
            if (!_occluderSuppressed) return;
            OccluderClip.SetGameClipEnabled(true);
            _occluderSuppressed = false;
        }

        // Active view index (0/1/2); exposed so the patch classes can read it for the trace.
        internal static int ActiveViewNum => _activeView;

        static void OnSaveGUI(UnityModManager.ModEntry modEntry) => settings.Save(modEntry);
    }

    // Captures each area's default zoom endpoints (set per-area in
    // ResetCurrentModeSettings) and re-applies them scaled by the user's factors.
    // normalize 0 = zoomed out (FovMax / PhysicalZoomMin); 1 = zoomed in (FovMin / PhysicalZoomMax).
    static class ZoomLimits
    {
        static float _fovMin, _fovMax, _physMin, _physMax;
        static bool _captured;

        static object GetZoom(object rig) => Traverse.Create(rig).Property("CameraZoom").GetValue<object>();

        // CameraZoom exposes the zoom endpoints as properties on RT but as plain fields on WotR, so resolve
        // either. Returns NaN when the member is absent (e.g. WotR has no PhysicalZoom*), so Apply skips it.
        static float GetMember(Traverse tz, string name)
        {
            var p = tz.Property(name);
            if (p.PropertyExists()) return p.GetValue<float>();
            var f = tz.Field(name);
            if (f.FieldExists()) return f.GetValue<float>();
            return float.NaN;
        }

        static void SetMember(Traverse tz, string name, float v)
        {
            var p = tz.Property(name);
            if (p.PropertyExists()) { p.SetValue(v); return; }
            var f = tz.Field(name);
            if (f.FieldExists()) f.SetValue(v);
        }

        public static void CaptureBaseline(object rig)
        {
            var zoom = GetZoom(rig);
            if (zoom == null) return;
            var tz = Traverse.Create(zoom);
            _fovMin = GetMember(tz, "FovMin");
            _fovMax = GetMember(tz, "FovMax");
            _physMin = GetMember(tz, "PhysicalZoomMin");   // RT only; NaN on WotR, whose zoom is FOV-only
            _physMax = GetMember(tz, "PhysicalZoomMax");
            _captured = true;
        }

        public static void Apply(object rig, bool restoreDefault)
        {
            if (!_captured) { CaptureBaseline(rig); if (!_captured) return; }
            var zoom = GetZoom(rig);
            if (zoom == null) return;
            var tz = Traverse.Create(zoom);

            float outF = restoreDefault ? 1f : Mathf.Clamp(Main.settings.ZoomOutFactor, Main.ZoomOutMin, Main.ZoomOutMax);
            float inF  = restoreDefault ? 1f : Mathf.Clamp(Main.settings.ZoomInFactor,  Main.ZoomInMin,  Main.ZoomInMax);

            // FOV mode: wider FOV = more zoomed out, narrower = more zoomed in.
            if (!float.IsNaN(_fovMax)) SetMember(tz, "FovMax", Mathf.Clamp(_fovMax * outF, 5f, 110f));
            if (!float.IsNaN(_fovMin)) SetMember(tz, "FovMin", Mathf.Clamp(_fovMin / inF, 2f, 110f));   // floor lowered from 5 so high zoom-in factors can bite

            // Physical mode (RT only; absent on WotR, so skipped). Best-effort; flip if a slider feels inverted in-game.
            if (!float.IsNaN(_physMin)) SetMember(tz, "PhysicalZoomMin", _physMin * outF);
            if (!float.IsNaN(_physMax)) SetMember(tz, "PhysicalZoomMax", _physMax / inF);
        }
    }

    // Widens the angle band the native Mouse3 pitch is clamped to. Both Surface and
    // Space pairs are set (the orbit clamp reads the Space pair; setting both is safe
    // and covers whichever the active mode actually uses).
    static class PitchRange
    {
        public static void Apply(object rig, float min, float max)
        {
            float lo = Mathf.Clamp(min, Main.PitchHardMin, Main.PitchHardMax);
            float hi = Mathf.Clamp(max, Main.PitchHardMin, Main.PitchHardMax);
            if (hi < lo) { float tmp = lo; lo = hi; hi = tmp; }
            var t = Traverse.Create(rig);
            Set(t, "MinSurfaceCameraAngle", lo);
            Set(t, "MaxSurfaceCameraAngle", hi);
            Set(t, "MinSpaceCameraAngle", lo);
            Set(t, "MaxSpaceCameraAngle", hi);
        }
        static void Set(Traverse t, string field, float v)
        {
            var f = t.Field(field);
            if (f.FieldExists()) f.SetValue(v);
        }
    }

    // Drives the Cinemachine virtual-camera lens near/far clip planes. The game only ever
    // writes FieldOfView to that lens, never the clip planes, so our values persist. The
    // LensSettings is a struct: read it (boxed), set the field(s), write it back. Also sets
    // the camera directly as a fallback for any mode without a virtual camera. Near and far
    // are independent - either may be overridden while the other rides the game's baseline.
    static class ClipPlane
    {
        static float _baseNear, _baseFar;
        static bool _captured, _customActive, _triedFields, _loggedBase;
        static bool _wotrNearOn;        // WotR: is a near-clip view live this frame (set in Apply/Restore, read by the StaticPreRender transpiler)
        static float _wotrNear = 1f;    // WotR: the near value to pin when _wotrNearOn
        static FieldInfo _fLens, _fNcp, _fFcp;

        static object GetZoom(object rig) => Traverse.Create(rig).Property("CameraZoom").GetValue<object>();
        static object GetVcam(object zoom) => zoom == null ? null : Traverse.Create(zoom).Field("m_VirtualCamera").GetValue<object>();
        static Camera GetCam(object zoom) => zoom == null ? null : Traverse.Create(zoom).Field("m_Camera").GetValue<Camera>();

        static void EnsureFields(object vcam)
        {
            if (_triedFields || vcam == null) return;
            _triedFields = true;
            _fLens = AccessTools.Field(vcam.GetType(), "m_Lens");
            if (_fLens != null)
            {
                _fNcp = AccessTools.Field(_fLens.FieldType, "NearClipPlane");
                _fFcp = AccessTools.Field(_fLens.FieldType, "FarClipPlane");
            }
            if (_fNcp == null) Main.Log?.Error("Clip: m_Lens.NearClipPlane not found on " + vcam.GetType().Name + " - near clip will be inert.");
            if (_fFcp == null) Main.Log?.Error("Clip: m_Lens.FarClipPlane not found on " + vcam.GetType().Name + " - far clip will be inert.");
        }

        static void WriteVcam(object vcam, float near, float far)
        {
            if (_fLens == null) return;
            object lens = _fLens.GetValue(vcam);   // boxed copy of the struct
            if (_fNcp != null) _fNcp.SetValue(lens, near);
            if (_fFcp != null) _fFcp.SetValue(lens, far);
            _fLens.SetValue(vcam, lens);           // write the struct back
        }

        // Capture the game's untouched near/far once (per-plane: the lens field if present, else the
        // live camera) and log them so the far range can be calibrated. Never leaves a baseline at zero.
        static void Capture(object vcam, Camera cam)
        {
            if (_captured) return;
            object l = (vcam != null && _fLens != null) ? _fLens.GetValue(vcam) : null;
            if (l != null && _fNcp != null) _baseNear = (float)_fNcp.GetValue(l);
            else if (cam != null) _baseNear = cam.nearClipPlane;
            if (l != null && _fFcp != null) _baseFar = (float)_fFcp.GetValue(l);
            else if (cam != null) _baseFar = cam.farClipPlane;
            if (l != null || cam != null) _captured = true;
            if (_captured && !_loggedBase)
            {
                _loggedBase = true;
                Main.Log?.Log("Clip baseline (game defaults): near=" + _baseNear.ToString("0.##") + ", far=" + _baseFar.ToString("0.#"));
            }
        }

        public static void Apply(object rig, bool nearOn, float nearVal, bool farOn, float farVal)
        {
            var zoom = GetZoom(rig);
            var vcam = GetVcam(zoom);
            var cam = GetCam(zoom);
            EnsureFields(vcam);
            Capture(vcam, cam);
            float n = nearOn ? Mathf.Clamp(nearVal, Main.NearClipMin, Main.NearClipMax) : _baseNear;
            float f = farOn ? Mathf.Clamp(farVal, Main.FarClipMin, Main.FarClipMax) : _baseFar;
            _wotrNearOn = nearOn; _wotrNear = n;   // WotR near lever: the StaticPreRender transpiler reads this (RT ignores it)
            if (vcam != null) WriteVcam(vcam, n, f);
            if (cam != null) { cam.nearClipPlane = n; cam.farClipPlane = f; }
            _customActive = true;
        }

        public static void Restore(object rig)
        {
            if (!_customActive || !_captured) return;
            var zoom = GetZoom(rig);
            var vcam = GetVcam(zoom);
            var cam = GetCam(zoom);
            EnsureFields(vcam);
            if (vcam != null) WriteVcam(vcam, _baseNear, _baseFar);
            if (cam != null) { cam.nearClipPlane = _baseNear; cam.farClipPlane = _baseFar; }
            _customActive = false;
            _wotrNearOn = false;
        }

        // Called only from the WotR StaticPreRender transpiler below (RT keeps the game's own near via its
        // Cinemachine lens, so it never calls this). Returns the active view's near when a near-clip view is
        // live this frame - decided by Apply/Restore in the per-frame postfix, which runs before pre-render -
        // else the game's own default, so it is vanilla-identical when no near-clip view is active.
        public static float WotRNearPin(float gameDefault) => _wotrNearOn ? _wotrNear : gameDefault;
    }

    // WotR near clip. WotR's CameraZoom has no Cinemachine virtual camera (the lever RT's near clip rides),
    // so the only near lever is the Unity camera's nearClipPlane - which Kingmaker.Visual.RenderingManager
    // .StaticPreRender hard-pins to a constant 1.0 each pre-render frame and then feeds into depth
    // reconstruction and screen-space reflections. A direct camera write from our postfix loses to that pin,
    // and a postfix on StaticPreRender would win the render near but desync reconstruction/SSR (both read the
    // near live, within StaticPreRender, before our postfix could run). So we transpile the pinned value
    // itself: replace the constant that StaticPreRender pushes into Camera.set_nearClipPlane with our
    // provider, setting near BEFORE the reconstruction/SSR reads so the whole depth pipeline stays consistent
    // at our value. RT-gated out (its near rides the vcam lens; its StaticPreRender pin is not the lever),
    // leaving RT byte-identical.
    [HarmonyPatch]
    static class RenderingManager_StaticPreRender_WotRNearPatch
    {
        static bool Prepare() => Compat.Ui == Compat.UiKind.WotR
            && AccessTools.Method("Kingmaker.Visual.RenderingManager:StaticPreRender") != null;

        static MethodBase TargetMethod() => AccessTools.Method("Kingmaker.Visual.RenderingManager:StaticPreRender");

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var setNear = AccessTools.Method("UnityEngine.Camera:set_nearClipPlane");
            var pin = AccessTools.Method(typeof(ClipPlane), "WotRNearPin");
            if (setNear != null && pin != null)
            {
                for (int i = 0; i < codes.Count; i++)
                {
                    var op = codes[i].opcode;
                    if ((op == System.Reflection.Emit.OpCodes.Callvirt || op == System.Reflection.Emit.OpCodes.Call)
                        && setNear.Equals(codes[i].operand))
                    {
                        // Stack here is [camera, near]; our static float(float) consumes the near and pushes
                        // ours, leaving [camera, near'] for the setter. Insert before the setter so the value
                        // is in place for the reconstruction/SSR reads that follow in the same method.
                        codes.Insert(i, new CodeInstruction(System.Reflection.Emit.OpCodes.Call, pin));
                        Main.Log?.Log("Clip: WotR near-pin transpiler applied to StaticPreRender - near clip is now live on Wrath.");
                        return codes;
                    }
                }
            }
            Main.Log?.Error("Clip: WotR near-pin transpiler did not find Camera.set_nearClipPlane in StaticPreRender - near clip stays at the game default on WotR.");
            return codes;
        }
    }

    // Toggles RT's occluder see-through - the render-pipeline service that dissolves walls and doors
    // between the camera and your party so you can see units behind cover. Disabling it leaves that
    // geometry solid. The lever is the static Owlcat.Runtime.Visual.OcclusionGeometryClip.System.SetEnabled,
    // which stops/starts the whole clip service; it no-ops when the value is unchanged, so re-asserting it
    // each frame is cheap. Resolved by reflection so the mod needs no reference to the visual assembly.
    static class OccluderClip
    {
        static MethodInfo _setEnabled;
        static bool _resolved, _warned;

        static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            var t = AccessTools.TypeByName("Owlcat.Runtime.Visual.OcclusionGeometryClip.System");
            if (t != null) _setEnabled = AccessTools.Method(t, "SetEnabled", new[] { typeof(bool) });
            if (_setEnabled == null && !_warned)
            {
                _warned = true;
                Main.Log?.Error("OccluderClip: OcclusionGeometryClip.System.SetEnabled not found - the solid-walls toggle will do nothing.");
            }
        }

        // on == true  -> the game's clip service runs (normal see-through)
        // on == false -> service stopped (walls and doors stay solid)
        public static void SetGameClipEnabled(bool on)
        {
            Resolve();
            if (_setEnabled == null) return;
            try { _setEnabled.Invoke(null, new object[] { on }); }
            catch (Exception e) { Main.Log?.Error("OccluderClip.SetGameClipEnabled failed: " + e); }
        }
    }

    // ===== WotR: solid walls = suppress the OccludedObjectHighlighting feature. =====
    // WotR has no OcclusionGeometryClip service; its camera see-through is the SRP's
    // OccludedObjectHighlighting renderer feature - a depth-clip pass that NOISE-DISSOLVES geometry
    // sitting in front of per-unit clipper blobs (and geometry within NearCameraClipDistance of the
    // camera), plus a silhouette highlight for the occluded units. This is what eats wagons/market
    // stands on a low, dollied-in view: at shoulder height, scenery lines up in front of units far more
    // often than from the vanilla high camera, and the dissolve is distance/position-driven (empirically
    // rotation-insensitive - the blob positions depend on camera POSITION only). WOTR_COMPAT §2's
    // "highlighting, not dissolve - nothing to suppress" was wrong.
    // The engine gates the whole feature in-shader on one global float, _OccludedObjectHighlightingFeatureEnabled:
    // AddRenderPasses sets it to 1 every frame it runs (and its own non-game-camera path sets 0 via
    // DisableFeature), so a 0 is a fully supported off state - no pass skipping, no stale render targets.
    // This postfix lands after the game's 1-write each frame and forces 0 while the active view wants
    // solid walls; the moment it stops (toggle off, cutscene standdown, mod disabled) the game's own
    // per-frame write restores the feature - self-healing, no restore path needed.
    // Trade-off, deliberate: while suppressed the occluded-unit SILHOUETTES are off too (one flag gates
    // the whole feature in-shader).
    [HarmonyPatch]
    static class OccludedHighlight_SolidWalls_WotRPatch
    {
        static MethodBase TargetMethod() => AccessTools.Method(
            "Owlcat.Runtime.Visual.RenderPipeline.RendererFeatures.OccludedObjectHighlighting.OccludedObjectHighlightingFeature:AddRenderPasses");

        static bool Prepare() => Compat.Ui == Compat.UiKind.WotR && TargetMethod() != null;

        static bool _logged;

        static void Postfix()
        {
            try
            {
                if (!Main.Active || Main.settings == null) return;
                if (!Main.SolidWallsWanted()) return;
                Shader.SetGlobalFloat("_OccludedObjectHighlightingFeatureEnabled", 0f);
                if (!_logged)
                {
                    _logged = true;
                    Main.Log?.Log("SolidWalls (WotR): suppressing the occluded-object dissolve/highlight feature while this view is active.");
                }
            }
            catch { }   // never let a render-setup hook throw into the pipeline
        }
    }

    // ===== FIX: project overtips through the LIVE render camera. =====
    // The mod's focus offset -- a world-up pivot, a lateral shoulder shift, and a large (~25u on View 1)
    // dolly -- is applied to the rig in CameraRig.UpdateInternal's POSTFIX, AFTER the game has built
    // m_WorldToClipMatrixCached, the matrix the overtips read. So the overtips were projected from the
    // un-shifted camera (~27u out) while the units render from the shifted, dollied-in camera (~2u out):
    // a large FIXED parallax error, present even when the camera is still, whose screen direction flips
    // with yaw (a fixed camera displacement projects differently as the view turns) -- not rotation lag.
    // The live camera's own projection includes the offset and so lands where the units actually render,
    // so we project the overtips through it instead of the stale cached matrix. This also corrects the
    // other rig.WorldToViewport consumers (selection box, rulers, scanner), which the offset displaced
    // the same way. Behind-camera points are pushed off-screen so the game hides them.
    [HarmonyPatch]
    static class CameraRig_WorldToViewport_LiveProjectionPatch
    {
        static bool Prepare() => AccessTools.Method("Kingmaker.View.CameraRig:WorldToViewport", new[] { typeof(Vector3) }) != null;
        static MethodBase TargetMethod() => AccessTools.Method("Kingmaker.View.CameraRig:WorldToViewport", new[] { typeof(Vector3) });

        static void Postfix(object __instance, Vector3 __0, ref Vector2 __result)
        {
            Camera cam = null;
            try { cam = Traverse.Create(__instance).Property("Camera").GetValue<Camera>(); } catch { }
            if (cam == null) return;
            Vector3 vp = cam.WorldToViewportPoint(__0);                                // live render projection (includes the offset)
            __result = (vp.z > 0f) ? new Vector2(vp.x, vp.y) : new Vector2(-1f, -1f);  // behind camera -> off-screen (hidden)
        }
    }

    // Restore the rig's clean position in the prefix (so the game's follow/scroll
    // smoothing never sees our framing offset and can't fight it), then re-apply
    // framing in the postfix purely for the rendered frame. Pitch range + zoom limits
    // are maintained here too. Rotation is left entirely to the game (native pitch).
    [HarmonyPatch]
    static class CameraRig_UpdateInternal_Patch
    {
        static MethodBase TargetMethod() => AccessTools.Method("Kingmaker.View.CameraRig:UpdateInternal");

        static bool Prepare()
        {
            if (TargetMethod() != null) return true;
            Main.Log?.Error("Kingmaker.View.CameraRig:UpdateInternal not found - camera adjustments are off.");
            return false;
        }

        static Vector3 _cleanPos;
        static bool _hasClean;
        static float _followYaw;      // mod-driven slewed follow yaw; overrides the transform while limiting
        static bool _followYawValid;
        static float _mlYaw;          // mouselook rendered yaw accumulator (mouse + rate-limited follow)
        static float _mlPitch;        // mouselook rendered pitch accumulator
        static float _mlPrevFacing;   // follower facing (m_TargetRotate.y) from last mouselook frame
        static bool _mlActive;        // was mouselook engaged last frame, to catch enter/exit transitions

        // --- Live-follow anchor: cached handles to the camera's unit follower and the followed entity ---
        // CameraUnitFollower is a plain class (not a Component) nested in CameraController, so we can't
        // FindObjectOfType it. Instead a tiny postfix on its Follow/TryFollow hands us the live instance.
        static object _follower;                                  // CameraController+CameraUnitFollower instance
        static System.Reflection.FieldInfo _followerEntityField;  // CameraUnitFollower.m_Entity
        static System.Reflection.PropertyInfo _entityViewTfProp;  // AbstractUnitEntity.ViewTransform (resolved off the live entity type)
        static System.Type _entityTypeCached;                     // entity type the prop was resolved against

        // Player-API handles (Game.Instance.Player.MainCharacterEntity), resolved lazily and cached.
        static System.Reflection.MethodInfo _gameInstGetter; static bool _gameInstResolved;
        static System.Reflection.MethodInfo _playerGetter, _mainCharGetter;
        static System.Reflection.PropertyInfo _charPosProp; static System.Type _charPosType;
        static System.Reflection.FieldInfo _selCtrlField; static bool _selCtrlResolved;
        static System.Reflection.MethodInfo _firstSelGetter; static System.Type _selUnitType; static System.Reflection.PropertyInfo _selUnitPosProp;

        // Called by the follower-capture patch with the live CameraUnitFollower instance.
        internal static void NotifyFollower(object follower)
        {
            if (follower == null) return;
            if (!ReferenceEquals(_follower, follower))
            {
                _follower = follower;
                _followerEntityField = AccessTools.Field(follower.GetType(), "m_Entity");
            }
        }

        // Drop the cached follower so it is re-captured fresh. Called at the start of a save load, after which the
        // game re-attaches the follow only when the player first takes control - which is exactly the signal we use.
        internal static void ClearFollower()
        {
            _follower = null; _followerEntityField = null;
            _entityTypeCached = null; _entityViewTfProp = null;
        }

        // True once the camera follower is live and pointed at a real unit - i.e. the player has taken control and
        // the follower now drives the focal. Used to know when to stop the on-load recenter and hand off.
        internal static bool FollowerActive() => TryGetLiveSubjectPos(out _);

        // Resolve the Game singleton once, then reuse it for the player/selection lookups below.
        static bool TryGetGame(out object game)
        {
            game = null;
            if (!_gameInstResolved)
            {
                _gameInstResolved = true;
                System.Type gt = AccessTools.TypeByName("Kingmaker.Game");
                if (gt != null) _gameInstGetter = AccessTools.Method(gt, "get_Instance");
            }
            if (_gameInstGetter == null) return false;
            game = _gameInstGetter.Invoke(null, null);
            return game != null;
        }

        // The currently selected unit's world position, via Game.Instance.SelectionCharacter.FirstSelectedUnit.
        // This is what the camera follows, so it is the right thing to frame even in scenes where you are driving
        // a companion rather than the main character. Null/empty selection returns false (the caller falls back).
        static bool TryGetSelectedUnitPos(out Vector3 pos)
        {
            pos = Vector3.zero;
            try
            {
                if (!TryGetGame(out object game)) return false;
                if (!_selCtrlResolved) { _selCtrlResolved = true; _selCtrlField = AccessTools.Field(game.GetType(), "SelectionCharacter"); }
                object sel = _selCtrlField?.GetValue(game);
                if (sel == null) return false;
                if (_firstSelGetter == null) _firstSelGetter = AccessTools.Method(sel.GetType(), "get_FirstSelectedUnit");
                object unit = _firstSelGetter?.Invoke(sel, null);
                if (unit == null) return false;
                System.Type ut = unit.GetType();
                if (!ReferenceEquals(ut, _selUnitType)) { _selUnitType = ut; _selUnitPosProp = AccessTools.Property(ut, "Position"); }
                if (_selUnitPosProp == null) return false;
                object pv = _selUnitPosProp.GetValue(unit);
                if (pv is Vector3) { pos = (Vector3)pv; return pos.sqrMagnitude > 1f; }
                return false;
            }
            catch { return false; }
        }

        // The party main character's world position, via Game.Instance.Player.MainCharacterEntity.Position.
        static bool TryGetMainCharacterPos(out Vector3 pos)
        {
            pos = Vector3.zero;
            try
            {
                if (!TryGetGame(out object game)) return false;
                if (_playerGetter == null) _playerGetter = AccessTools.Method(game.GetType(), "get_Player");
                object player = _playerGetter?.Invoke(game, null);
                if (player == null) return false;
                if (_mainCharGetter == null) _mainCharGetter = AccessTools.Method(player.GetType(), "get_MainCharacterEntity");
                object ent = _mainCharGetter?.Invoke(player, null);
                if (ent == null) return false;
                System.Type et = ent.GetType();
                if (!ReferenceEquals(et, _charPosType)) { _charPosType = et; _charPosProp = AccessTools.Property(et, "Position"); }
                if (_charPosProp == null) return false;
                object pv = _charPosProp.GetValue(ent);
                if (pv is Vector3) { pos = (Vector3)pv; return pos.sqrMagnitude > 1f; }
                return false;
            }
            catch { return false; }
        }

        // The unit the on-load camera should frame: the selected unit if there is one, else the party main
        // character. Both are alive from the moment a save finishes loading, unlike the camera follower.
        static bool TryGetSubjectPos(out Vector3 pos)
        {
            if (TryGetSelectedUnitPos(out pos)) return true;
            return TryGetMainCharacterPos(out pos);
        }

        // Snap the focal onto the subject (selected unit / main character). On a save load the game parks the
        // camera on an area establishing shot and only attaches the follow to your character when you first move;
        // this overrides that so a close/over-the-shoulder View 1 frames the character immediately. Returns true
        // once it has a valid subject position (so the caller can keep retrying until the unit is positioned);
        // 'moved' reports whether the focal actually needed shifting this frame, which the load window uses to
        // tell when the focal has settled. Validity-guarded: a missing/degenerate position leaves the focal alone.
        internal static bool RecenterFocalOnSubject(out bool moved)
        {
            moved = false;
            try
            {
                if (Main.CurrentRig == null) return false;
                if (!TryGetSubjectPos(out Vector3 target)) return false;
                var f = Traverse.Create(Main.CurrentRig).Field("m_TargetPosition");
                Vector3 cur = f.GetValue<Vector3>();
                if (Vector3.Distance(cur, target) > 0.3f)
                {
                    bool big = Vector3.Distance(cur, target) > 1f;
                    f.SetValue(target);
                    moved = true;
                    if (big) Main.Log?.Log(string.Format("Recentered focal on load: {0} -> {1}", cur.ToString("F1"), target.ToString("F1")));
                }
                return true;
            }
            catch (Exception e) { Main.Log?.Error("Recenter focal failed: " + e); return false; }
        }

        // Live (undamped) world position of the model the camera follows: follower.m_Entity.ViewTransform.position.
        // Returns false (leaving the caller on the damped focal) if any link is missing.
        internal static bool TryGetLiveSubjectPos(out Vector3 pos)
        {
            pos = Vector3.zero;
            try
            {
                if (_follower == null || _followerEntityField == null) return false;
                object entity = _followerEntityField.GetValue(_follower);
                if (entity == null) return false;
                System.Type et = entity.GetType();
                if (!ReferenceEquals(et, _entityTypeCached))
                {
                    _entityTypeCached = et;
                    _entityViewTfProp = AccessTools.Property(et, "ViewTransform");
                }
                if (_entityViewTfProp == null) return false;
                Transform vt = _entityViewTfProp.GetValue(entity) as Transform;
                if (vt == null) return false;
                pos = vt.position;
                return true;
            }
            catch { return false; }
        }

        // Clear the mouselook seat after the camera state is set externally (e.g. ApplyView). Next frame the
        // mouselook block re-seats pitch/yaw from what was just applied, and - critically - the exit-sync branch
        // will not fire to stamp the previous mouselook angles back over the applied view.
        internal static void ResetMouselookSeat() { _mlActive = false; }

        static void Prefix(object __instance)
        {
            if (!_hasClean) return;
            if (!Main.Active || __instance == null) { _hasClean = false; return; }
            var comp = __instance as Component;
            if (comp == null) { _hasClean = false; return; }
            comp.transform.position = _cleanPos;   // rotation deliberately untouched
        }

        static void Postfix(object __instance)
        {
            if (__instance == null) return;
            var comp = __instance as Component;
            if (comp == null) { _hasClean = false; return; }

            Main.CurrentRig = __instance;

            if (!Main.Active || Main.settings == null)
            {
                ZoomLimits.Apply(__instance, restoreDefault: true);
                ClipPlane.Restore(__instance);
                _hasClean = false;
                return;
            }

            try
            {
                var s = Main.settings;
                var t = Traverse.Create(__instance);

                bool hardBind = ReadHardBind(t);

                // Overworld/system/sector map shares this rig but wants the game's native camera - treat it
                // like a hard-bound shot so framing, clip and the occluder toggle all stand down.
                if (InMapMode()) hardBind = true;
                LastHardBind = hardBind;   // exposed for OnUpdate's occluder gate, which runs outside this postfix

                bool needCut = (s.FramingEnabled && s.FramingPauseInCutscenes)
                            || (s.ZoomLimitsEnabled && s.ZoomPauseInCutscenes);
                bool inCut = needCut && InCutscene();

                // While no preset is active and we're in ordinary gameplay (not a scripted/map/cutscene shot),
                // remember the live camera so a toggle to Vanilla can return here.
                if (Main.ActiveViewNum == 0 && !hardBind && !InCutscene())
                    Main.CaptureVanillaState(__instance);

                // Zoom: maintained every frame; reverted when off, hard-bound, during an in-dialogue scripted
                // shot (CameraCutsceneActive is dialogue-only, so this doesn't touch pure cutscenes), and in pure
                // cutscenes only when the pause-in-cutscenes toggle is on - that control stays authoritative there.
                bool zoomRevert = !s.ZoomLimitsEnabled || hardBind
                                || CutsceneCameraGate.CameraCutsceneActive()
                                || (s.ZoomPauseInCutscenes && inCut);
                ZoomLimits.Apply(__instance, restoreDefault: zoomRevert);

                // Pitch range: widen the native drag clamp (only affects manual drag). Stand it down under any
                // scripted camera control too - the clamp otherwise forces an authored low-angle shot back up.
                if (s.PitchRangeEnabled && !hardBind && !InCutscene() && !CutsceneCameraGate.CameraCutsceneActive())
                    PitchRange.Apply(__instance, s.MinPitchAngle, s.MaxPitchAngle);

                // Lock pitch: hold the active view's tilt every frame so a cutscene/transition can't leave the
                // camera flattened. Stood down while any scripted shot owns the camera (hard-bind, cutscene mode,
                // or an in-dialogue camera command) and re-asserted the instant it hands back. Mouselook views and
                // View 1 on a pad are excluded inside the helper.
                float lockPitch = Main.ActiveViewLockedPitch();
                if (!float.IsNaN(lockPitch) && !hardBind && !InCutscene() && !CutsceneCameraGate.CameraCutsceneActive())
                {
                    var lockTr = t.Field("m_TargetRotate");
                    Vector3 lr = lockTr.GetValue<Vector3>();
                    if (Mathf.Abs(lr.x - lockPitch) > 0.001f || Mathf.Abs(lr.z) > 0.001f)
                    {
                        lr.x = lockPitch; lr.z = 0f;
                        lockTr.SetValue(lr);
                        var lockComp = __instance as Component;
                        if (lockComp != null)
                        {
                            float keepYaw = lockComp.transform.eulerAngles.y;   // preserve facing; pin only pitch
                            lockComp.transform.rotation = Quaternion.Euler(lockPitch, keepYaw, 0f);
                        }
                    }
                }

                // Clip planes: per active view, near and far independently. Restore baseline when neither is on,
                // and stand the override down under any scripted camera control so an authored shot keeps the
                // game's own near/far (no user toggle for this, unlike zoom's pause-in-cutscenes).
                CameraView ccv = Main.ActiveViewObj();
                bool clipCutCam = hardBind || InCutscene() || CutsceneCameraGate.CameraCutsceneActive();
                bool nearOn = !clipCutCam && ccv != null && ccv.NearClipEnabled;
                bool farOn  = !clipCutCam && ccv != null && ccv.FarClipEnabled;
                if (nearOn || farOn) ClipPlane.Apply(__instance, nearOn, ccv.NearClip, farOn, ccv.FarClip);
                else ClipPlane.Restore(__instance);

                // Mouselook: drive the camera straight from the mouse, and fold in the follower's facing
                // change (the A/D turn) at a rate capped by the active view's multiplier. We keep our own
                // yaw/pitch accumulators and write ONLY the transform - we deliberately do NOT push the
                // combined yaw back into m_TargetRotate while looking, so the rig's target stays equal to the
                // pure follower facing. That keeps the facing reading clean (so we can slow just the A/D part
                // without touching the mouse) and the rig derives forward/right from the transform, which we
                // do set, so WASD still moves relative to where you're actually looking.
                if (!hardBind && Main.MouselookEngaged())
                {
                    if (Main.MouselookJustEngaged || !_mlActive)
                    {
                        // Entering mouselook: seat the accumulators on the current camera so there is no snap.
                        Vector3 tr0 = t.Field("m_TargetRotate").GetValue<Vector3>();
                        _mlYaw = comp.transform.eulerAngles.y;
                        _mlPitch = tr0.x;
                        _mlPrevFacing = tr0.y;
                        _mlActive = true;
                        Main.MouselookJustEngaged = false;   // drop the first frame's pre-lock delta
                    }
                    else
                    {
                        float sensX = Mathf.Max(0.01f, s.MouselookSensitivity);
                        float sensY = Mathf.Max(0.01f, s.MouselookSensY);
                        float dx = Input.GetAxis("Mouse X") * sensX;
                        float dy = Input.GetAxis("Mouse Y") * sensY * (s.MouselookInvertY ? 1f : -1f);
                        float loP = Main.MouselookPitchMin;   // free-look climbs above the horizon; the screen-basis Up-fix keeps movement correct up there
                        float hiP = Mathf.Clamp(s.MaxPitchAngle, Main.PitchHardMin, Main.PitchHardMax);
                        if (hiP < loP) { float tmp = loP; loP = hiP; hiP = tmp; }

                        // m_TargetRotate.y is the follower's facing only - we never fold our yaw back into it,
                        // so it stays clean for the slew read. We DO keep .x (pitch) in step with our accumulator,
                        // so a view captured mid-mouselook records the pitch you are actually looking at instead
                        // of a stale value left from before mouselook engaged.
                        var mlTrf = t.Field("m_TargetRotate");
                        Vector3 mlTr = mlTrf.GetValue<Vector3>();
                        float facing = mlTr.y;
                        float facingDelta = Mathf.DeltaAngle(_mlPrevFacing, facing);
                        float rotMult = Main.ActiveViewRotMult();
                        if (rotMult < 0.99f)
                        {
                            float maxStep = Main.FollowYawRate * rotMult * Time.deltaTime;
                            facingDelta = Mathf.Clamp(facingDelta, -maxStep, maxStep);
                        }
                        _mlYaw += facingDelta + dx;        // rate-limited A/D turn + full-speed mouse
                        _mlPitch = Mathf.Clamp(_mlPitch + dy, loP, hiP);
                        _mlPrevFacing = facing;

                        mlTr.x = _mlPitch;   // pitch stays authoritative in the rig target; yaw left as the facing
                        mlTr.z = 0f;
                        mlTrf.SetValue(mlTr);
                        comp.transform.rotation = Quaternion.Euler(_mlPitch, _mlYaw, 0f);
                    }
                }
                else if (_mlActive)
                {
                    // Leaving mouselook: hand the rig its target where the camera actually is, so it does not
                    // snap to the character's facing on the way out. (_followYaw is already kept synced to the
                    // transform by the slew block's else-branch every frame, so nothing else to reset here.)
                    var trf = t.Field("m_TargetRotate");
                    Vector3 tr = trf.GetValue<Vector3>();
                    tr.x = _mlPitch; tr.y = _mlYaw; tr.z = 0f;
                    trf.SetValue(tr);
                    comp.transform.rotation = Quaternion.Euler(tr);
                    _mlActive = false;
                }

                // Gamepad: with mouselook standing aside on a pad, drive View 1's pitch from the right stick Y
                // and pin zoom here. Because it re-asserts pitch+zoom every frame, this also doubles as the
                // backstop that holds the view across dialogue/area transitions (no mouselook to carry it on a pad).
                Main.TickGamepadPitchHold(hardBind);

                // Follow-yaw slew limiter. The A/D turn is the follower ramping m_TargetRotate.y toward the
                // character facing (measured ~131 deg/s), with the rig's rubber-band tracking it ~3 deg behind;
                // both confirmed by the trace. We can't slow it by editing the target (the follower overwrites
                // it inside UpdateInternal before the slerp), so we take over the final transform yaw - the way
                // mouselook does - and slew it toward the target at a rate capped by the active view's
                // multiplier. Gated to follower-driven turns only: not a mouse drag, not our mouselook, not a
                // scripted/cutscene shot. mult >= ~1 (or no active view) leaves the stock turn untouched.
                // Slowing a follow necessarily lags it, so the camera trails during the turn and finishes
                // settling just after release; lower mult = slower = more trail.
                {
                    var trY = t.Field("m_TargetRotate");
                    Vector3 trv = trY.GetValue<Vector3>();
                    float tgtYaw = trv.y;
                    float xfYaw = comp.transform.eulerAngles.y;

                    float rotMult = Main.ActiveViewRotMult();
                    bool byMouse = false;
                    try { byMouse = t.Field("m_RotationByMouse").GetValue<bool>(); } catch { }
                    bool slew = rotMult < 0.99f && !byMouse && !hardBind && !Main.MouselookEngaged() && !InCutscene();

                    if (slew)
                    {
                        if (!_followYawValid) { _followYaw = xfYaw; _followYawValid = true; }
                        float gap = Mathf.DeltaAngle(_followYaw, tgtYaw);
                        float maxStep = Main.FollowYawRate * rotMult * Time.deltaTime;
                        _followYaw += Mathf.Clamp(gap, -maxStep, maxStep);
                        Vector3 e = comp.transform.eulerAngles;          // keep pitch (x) and roll (z), override yaw only
                        comp.transform.rotation = Quaternion.Euler(e.x, _followYaw, e.z);
                        xfYaw = _followYaw;                              // report what we actually rendered
                    }
                    else
                    {
                        _followYaw = xfYaw; _followYawValid = true;      // stay synced so engaging the limiter is seamless
                    }
                }

                // Focus offset: a world-up pivot raise (yaw-independent, so the orbit centre sits at the
                // character's shoulders and doesn't wander as you turn) plus a lateral shoulder shift along
                // Right (true over-the-shoulder framing). Applied in every mode now - mouselook on or off
                // uses the same offset, so dropping out of mouselook (Left Shift, an incidental toggle)
                // can't make the focus jump. Re-applied every frame so the unit-follower recentering can't
                // wipe it when you move with WASD.
                bool inDialog = UIGate.HasDialog() || InDialogMode();
                DialogFramingMode dmode = s.DialogFraming;
                bool framingDialogExempt = inDialog && dmode != DialogFramingMode.Off;   // keep the offset through this conversation
                float ph = Main.ActiveViewPivotHeight();
                float shoulder = Main.ActiveViewShoulder();
                float dolly = Main.ActiveViewDolly();
                // The dolly is tuned for the ~27u gameplay camera distance. In a conversation the game drops the
                // camera onto a lower base, so the dolly's push along the (downward) view ray drags the framing
                // onto the feet. In Lift mode we drop the dolly and keep only the gentle pivot/shoulder raise; in
                // Tactical mode we keep the full dolly but cancel the vertical reframing at the write below, so the
                // over-the-shoulder shot matches gameplay. (In Off mode the whole offset is paused by the gate.)
                bool dialogLift = inDialog && dmode == DialogFramingMode.Lift;
                float effDolly = dialogLift ? 0f : dolly;
                // Tactical dialogue: the game frames the conversation partner, so the gameplay over-the-shoulder
                // shift would push *them* off-centre - drop it (the game's own left/right framing still applies).
                // The vertical framing is taken from the Dialogue framing height slider rather than the gameplay
                // pivot, because the dolly's downward pull in dialogue is pitch-dependent and no single constant
                // suits every angle; the base-Y pin at the write still matches gameplay height underneath.
                bool dialogTactical = inDialog && dmode == DialogFramingMode.Tactical;
                float effPivot = dialogTactical ? Main.ActiveViewDialogHeight() : ph;
                float effShoulder = dialogTactical ? 0f : shoulder;

                // Static dialogue zoom (opt-in per view): pin the scroll position in Full-tactical dialogue so the
                // conversation keeps a consistent zoom rather than the game's. Stands down for scripted shots.
                if (dialogTactical && !hardBind && !CutsceneCameraGate.CameraCutsceneActive())
                {
                    float dlgZoom = Main.ActiveViewDialogZoom();
                    if (!float.IsNaN(dlgZoom))
                    {
                        var dz = t.Property("CameraZoom").GetValue<object>();
                        if (dz != null)
                        {
                            var tdz = Traverse.Create(dz);
                            Main.SetFloat(tdz, "m_PlayerScrollPosition", dlgZoom);
                            Main.SetFloat(tdz, "m_ScrollPosition", dlgZoom);
                            Main.SetFloat(tdz, "m_SmoothScrollPosition", dlgZoom);
                        }
                    }
                }

                // Live subject position (the model's ViewTransform) - read once per frame for the live-follow
                // anchor. haveLive stays false (caller falls back to the damped focal) until the follower-capture
                // patch has handed us the follower and the entity links resolve.
                Vector3 liveNow = Vector3.zero; bool haveLive = false;
                if (Main.ActiveViewLiveFollow())
                    haveLive = TryGetLiveSubjectPos(out liveNow);

                // Shoulder shifts along the camera's horizontal facing-right so it tracks the view as you
                // turn. RT's CameraRig.Right is exactly that (set in FigureOutScreenBasis from the live camera),
                // but WotR populates Right from a scroll-projection basis that doesn't follow our yaw, so it
                // reads world-fixed ("relative to north"). Derive it from the live camera in both games - this
                // reproduces RT's value identically (RT unchanged) and makes the WotR shoulder camera-relative.
                Camera shCam = null;
                try { shCam = t.Property("Camera").GetValue<Camera>(); } catch { }
                Vector3 shRight = (shCam != null)
                    ? Vector3.ProjectOnPlane(shCam.transform.right, Vector3.up).normalized
                    : t.Property("Right").GetValue<Vector3>();   // fallback = prior behaviour if the camera is unavailable
                Vector3 baseOffset = Vector3.up * effPivot + shRight * effShoulder;
                bool applyOffset = s.FramingEnabled
                    && (Mathf.Abs(ph) > 0.0001f || Mathf.Abs(shoulder) > 0.0001f || Mathf.Abs(effDolly) > 0.0001f || Main.ActiveViewLiveFollow())
                    && !(inDialog && dmode == DialogFramingMode.Off)
                    && (!hardBind || framingDialogExempt)
                    && !(s.FramingPauseInCutscenes && inCut && !framingDialogExempt)
                    && !CutsceneCameraGate.CameraCutsceneActive();   // stand the offset down during scripted camera shots
                if (!applyOffset) { _hasClean = false; return; }

                Transform rigT = comp.transform;
                _cleanPos = rigT.position;
                _hasClean = true;

                // Dolly: the rig transform sits ON the focal, and the actual camera is parented to the rig ~27u out
                // (confirmed by the trace). So to move the camera toward the subject we shift the *rig* along the
                // camera->focal axis - the camera, being a child, translates the same amount, straight in along its
                // own view ray. That grows the subject in place (it stays on the ray) and keeps the OTS composition,
                // exactly like pivot/shoulder which also move the rig. The push is capped just past the subject so a
                // full dolly can sit at/just past the model (first-person / VR); near-clip culls the model when inside it.
                Vector3 offset = baseOffset;
                if (Mathf.Abs(effDolly) > 0.0001f)
                {
                    try
                    {
                        Camera dollyCam = t.Property("Camera").GetValue<Camera>();
                        if (dollyCam != null)
                        {
                            // Direction: the camera's own forward. It's rotation-derived and smooth (FOV-zoom shows no
                            // rotational shimmer), whereas (focal - camPos) rides m_TargetPosition, which steps ~0.15u
                            // per frame with the discrete logic position - feeding THAT into the dolly is what shimmers.
                            // When stationary the two are identical, so framing is unchanged; they only diverge while moving.
                            Vector3 viewDir = dollyCam.transform.forward;
                            // Stop-distance: measure to the live (smooth) subject when we have it, else the focal.
                            Vector3 subj = (Main.ActiveViewLiveFollow() && haveLive)
                                ? liveNow : t.Field("m_TargetPosition").GetValue<Vector3>();
                            float dist = Vector3.Distance(subj, dollyCam.transform.position);   // ~27u
                            if (dist > 0.01f && viewDir.sqrMagnitude > 0.0001f)
                            {
                                float applied = Mathf.Min(effDolly, dist + Main.DollyPast);   // up to just past the subject (full push = first-person/VR)
                                if (applied > 0f) offset = baseOffset + viewDir.normalized * applied;
                            }
                        }
                    }
                    catch (Exception de) { Main.Log?.Error("Dolly offset failed: " + de); }
                }

                // Live-follow anchor: lock the model in frame by shifting the rig onto the subject's live transform.
                // The rig sits on m_TargetPosition, so adding (live - m_TargetPosition) lands it exactly on the live
                // model position regardless of any baked focal offset; the framing offsets above then sit on top.
                // m_TargetPosition is the discrete logic position (steps ~0.15u/frame while moving); the ViewTransform
                // is the smooth interpolated one the model renders at, so this is what removes the rotational shimmer.
                if (Main.ActiveViewLiveFollow() && haveLive)
                {
                    try
                    {
                        Vector3 focalLive = t.Field("m_TargetPosition").GetValue<Vector3>();
                        Vector3 corr = liveNow - focalLive;
                        // Sanity guard: a real anchor correction is sub-unit (the model lags the focal ~0.1u). A large
                        // value means the live position is degenerate - e.g. on a save load an entity not yet positioned,
                        // or a stale follower handle pointing at a disposed unit, sits at the world origin - so skip it
                        // rather than fling the rig there. Also covers the one-frame gap during a teleport.
                        if (corr.sqrMagnitude < 9f) offset += corr;   // skip if > 3u
                    }
                    catch (Exception le) { Main.Log?.Error("Live-follow anchor failed: " + le); }
                }

                // Tactical dialogue mode: the game leans the camera into the conversation by dropping the rig
                // base (the focal stays put). Cancel just that vertical drop - pin the base height back to the
                // focal's, its gameplay relationship - so the over-the-shoulder height matches gameplay. X/Z are
                // left as the game set them, so it can still frame the speakers left/right. The full dolly/pivot/
                // shoulder offset then sits on top exactly as in gameplay.
                Vector3 finalBase = _cleanPos;
                if (inDialog && dmode == DialogFramingMode.Tactical)
                {
                    try { finalBase.y = t.Field("m_TargetPosition").GetValue<Vector3>().y; } catch { }
                }
                rigT.position = finalBase + offset;
            }
            catch (Exception e)
            {
                Main.Log?.Error("Camera adjustment failed: " + e);
                _hasClean = false;
            }
        }

        // ---- Game-mode + controller-mode detection (cached reflection, fails safe to "not in that mode") ----
        static PropertyInfo _piInstance, _piCurrentMode, _piControllerMode;
        static object _cutscene, _cutsceneGlobalMap, _dialog, _starSystem, _globalMap, _gamepadMode;
        static object _escMode, _fullScreenUi, _bugReport, _gameOver, _photoMode, _rest;   // menu / full-screen UI modes - mouselook yields the cursor for these
        static bool _modeInit;
        internal static bool LastHardBind;   // last hardBind (after the map force); read by OnUpdate's occluder gate

        static void InitModes()
        {
            if (_modeInit) return;
            _modeInit = true;
            var gameType = AccessTools.TypeByName("Kingmaker.Game");
            if (gameType != null)
            {
                _piInstance = AccessTools.Property(gameType, "Instance");
                _piCurrentMode = AccessTools.Property(gameType, "CurrentMode");
                // Game.ControllerMode is the game's own Mouse/Gamepad flag - the same signal it branches its
                // native input and camera behaviour on. Read the Gamepad enum value off the property's own type
                // so we needn't name the enum's namespace.
                _piControllerMode = AccessTools.Property(gameType, "ControllerMode");
                var cmt = _piControllerMode?.PropertyType;
                if (cmt != null) _gamepadMode = AccessTools.Field(cmt, "Gamepad")?.GetValue(null);
            }
            var gmt = AccessTools.TypeByName("Kingmaker.GameModes.GameModeType");
            if (gmt != null)
            {
                _cutscene = AccessTools.Field(gmt, "Cutscene")?.GetValue(null);
                _cutsceneGlobalMap = AccessTools.Field(gmt, "CutsceneGlobalMap")?.GetValue(null);
                _dialog = AccessTools.Field(gmt, "Dialog")?.GetValue(null);
                _starSystem = AccessTools.Field(gmt, "StarSystem")?.GetValue(null);
                _globalMap = AccessTools.Field(gmt, "GlobalMap")?.GetValue(null);
                // Menu / full-screen UI modes that need the mouse cursor. Any name absent on the running game
                // resolves to null and is skipped by InMenuMode (mode.Equals(null) is false), so this is cross-game safe.
                _escMode = AccessTools.Field(gmt, "EscMode")?.GetValue(null);
                _fullScreenUi = AccessTools.Field(gmt, "FullScreenUi")?.GetValue(null);
                _bugReport = AccessTools.Field(gmt, "BugReport")?.GetValue(null);
                _gameOver = AccessTools.Field(gmt, "GameOver")?.GetValue(null);
                _photoMode = AccessTools.Field(gmt, "PhotoMode")?.GetValue(null);
                _rest = AccessTools.Field(gmt, "Rest")?.GetValue(null);
            }
        }

        // RT marks a scripted (hard-bound) shot with the rig field m_HardBindPositionEnabled. WotR has no
        // such field; instead it sets ControllingDirectorCameraLink (non-null) for the duration of a Timeline
        // director's camera command (set by DirectorCameraLink.Link, cleared by UnLink). Read whichever the
        // running game exposes; either way "true" means a scripted shot owns the camera and the mod stands down.
        static bool ReadHardBind(Traverse t)
        {
            var f = t.Field("m_HardBindPositionEnabled");
            if (f.FieldExists()) return f.GetValue<bool>();
            var p = t.Property("ControllingDirectorCameraLink");
            if (p.PropertyExists()) return p.GetValue<object>() != null;
            return false;
        }

        internal static bool InCutscene()
        {
            try
            {
                InitModes();
                if (_piInstance == null || _piCurrentMode == null) return false;
                var inst = _piInstance.GetValue(null);
                if (inst == null) return false;
                var mode = _piCurrentMode.GetValue(inst);
                if (mode == null) return false;
                return mode.Equals(_cutscene) || mode.Equals(_cutsceneGlobalMap);
            }
            catch { return false; }
        }

        // True while the game mode is Dialog - a reliable "we're in a conversation" signal, unlike the UI dialog
        // box flag which drops out during the camera holds/refocuses inside a conversation.
        internal static bool InDialogMode()
        {
            try
            {
                InitModes();
                if (_piInstance == null || _piCurrentMode == null || _dialog == null) return false;
                var inst = _piInstance.GetValue(null);
                if (inst == null) return false;
                var mode = _piCurrentMode.GetValue(inst);
                return mode != null && mode.Equals(_dialog);
            }
            catch { return false; }
        }

        // True when the current game mode is a menu or full-screen UI that needs the mouse cursor: the Esc/pause
        // menu (EscMode), any full-screen window, bug report, game-over, photo mode, or the rest UI. Mouselook
        // yields the cursor for these. (Service windows / save-load / modals are already caught by PlainSurface;
        // this adds the Esc menu and other full-screen modes that aren't ServiceWindowsType values.)
        internal static bool InMenuMode()
        {
            try
            {
                InitModes();
                if (_piInstance == null || _piCurrentMode == null) return false;
                var inst = _piInstance.GetValue(null);
                if (inst == null) return false;
                var mode = _piCurrentMode.GetValue(inst);
                if (mode == null) return false;
                return mode.Equals(_escMode) || mode.Equals(_fullScreenUi) || mode.Equals(_bugReport)
                    || mode.Equals(_gameOver) || mode.Equals(_photoMode) || mode.Equals(_rest);
            }
            catch { return false; }
        }

        // True while the game is in a 2.5D map view that shares this rig but wants the game's native camera:
        // the in-system StarSystem view and the zoomed-out GlobalMap (Koronus Expanse) sector map. Gated like
        // a hard-bound shot. (GlobalMap is added pre-emptively by name; space combat can join this set once its
        // mode is known.)
        internal static bool InMapMode()
        {
            try
            {
                InitModes();
                if (_piInstance == null || _piCurrentMode == null) return false;
                var inst = _piInstance.GetValue(null);
                if (inst == null) return false;
                var mode = _piCurrentMode.GetValue(inst);
                if (mode == null) return false;
                return (_starSystem != null && mode.Equals(_starSystem))
                    || (_globalMap  != null && mode.Equals(_globalMap));
            }
            catch { return false; }
        }

        // True while the game is in Gamepad (controller) mode rather than Mouse mode. Drives the controller
        // exemptions: mouselook stands aside (the stick cannot feed mouse axes), and the per-view rotation
        // slow-down is bypassed so the right stick turns at stock speed.
        internal static bool InGamepadMode()
        {
            try
            {
                InitModes();
                if (_piInstance == null || _piControllerMode == null || _gamepadMode == null) return false;
                var inst = _piInstance.GetValue(null);
                if (inst == null) return false;
                var mode = _piControllerMode.GetValue(inst);
                return mode != null && mode.Equals(_gamepadMode);
            }
            catch { return false; }
        }
    }

    // Capture each area's default zoom endpoints right after the game sets them.
    [HarmonyPatch]
    static class CameraRig_ResetCurrentModeSettings_Patch
    {
        static MethodBase TargetMethod() => AccessTools.Method("Kingmaker.View.CameraRig:ResetCurrentModeSettings");
        static bool Prepare() => TargetMethod() != null;
        static void Postfix(object __instance)
        {
            try { ZoomLimits.CaptureBaseline(__instance); }
            catch (Exception e) { Main.Log?.Error("Zoom baseline capture failed: " + e); }
        }
    }

    // When Ctrl is held, the scroll wheel adjusts vertical framing instead of zoom; we
    // consume the wheel and skip the zoom tick for that frame. Otherwise the game's own
    // TickZoom runs and zooms within our extended FovMin/FovMax.
    [HarmonyPatch]
    static class CameraZoom_TickZoom_Patch
    {
        static MethodBase TargetMethod() => AccessTools.Method("Kingmaker.View.CameraZoom:TickZoom");
        static bool Prepare() => TargetMethod() != null;

        static bool Prefix()
        {
            if (!Main.Active || Main.settings == null || !Main.settings.FramingEnabled) return true;
            if (!(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))) return true;

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                // Ctrl+scroll live-tunes the active view's world-up pivot height; Ctrl+Shift+scroll tunes the
                // dolly-in distance instead. Only meaningful with a preset active; fixed steps stay in range.
                CameraView v = Main.ActiveViewObj();
                if (v != null)
                {
                    bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                    if (shift)
                        v.Dolly = Mathf.Clamp(v.Dolly + scroll * Main.DollyScrollStep, Main.DollyMin, Main.DollyMax);
                    else
                        v.PivotHeight = Mathf.Clamp(v.PivotHeight + scroll * Main.PivotScrollStep, Main.PivotMin, Main.PivotMax);
                }
            }
            return false;   // suppress zoom while Ctrl is held
        }
    }

    // Cross-game (RT vs WotR) reflection targets, resolved once at load. The RT name is tried
    // first and the WotR name is the fallback, so one compiled binary serves both games (see
    // WOTR_COMPAT.md). A target that resolves under neither game leaves its feature self-disabled.
    internal static class Compat
    {
        internal enum UiKind { Unknown, RT, WotR }
        internal static UiKind Ui = UiKind.Unknown;

        // CommandControlCamera.OnRun has version-dependent overloads. WotR's CommandBase declares BOTH
        // OnRun(player, skipping) and OnRun(player, track, skipping); on some game builds CommandControlCamera
        // overrides both, on others only the two-arg form. A bare AccessTools.Method("...:OnRun") does a
        // name-only reflection lookup that throws AmbiguousMatchException the moment two overloads are visible
        // on the type - and because that threw inside a Harmony Prepare(), it aborted the ENTIRE PatchAll and
        // the mod failed to load (reported on Wrath). Resolve the intended lifecycle entry - the
        // (CutscenePlayerData player, bool skipping) form the mod has always gated on - deterministically by
        // enumeration, derived type first then base, so overload count never matters.
        internal static MethodBase ResolveCommandControlCameraOnRun()
        {
            var t = AccessTools.TypeByName("Kingmaker.AreaLogic.Cutscenes.Commands.CommandControlCamera");
            var m = PickTwoArgOnRun(t);
            if (m == null && t != null) m = PickTwoArgOnRun(t.BaseType);
            return m;
        }

        static MethodBase PickTwoArgOnRun(Type t)
        {
            if (t == null) return null;
            MethodInfo fallback = null;
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (m.Name != "OnRun") continue;
                var ps = m.GetParameters();
                if (ps.Length == 2 && ps[1].ParameterType == typeof(bool)) return m;   // the entry the mod gates on
                if (fallback == null) fallback = m;
            }
            return fallback;
        }

        // RootUIContext: RT "Kingmaker.Code.UI.MVVM.RootUIContext", WotR "Kingmaker.UI.MVVM.RootUIContext".
        internal static Type RootUIContext;

        static bool _inited;
        internal static void Init()
        {
            if (_inited) return;
            _inited = true;

            RootUIContext = AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.RootUIContext");
            if (RootUIContext != null) Ui = UiKind.RT;
            else
            {
                RootUIContext = AccessTools.TypeByName("Kingmaker.UI.MVVM.RootUIContext");
                if (RootUIContext != null) Ui = UiKind.WotR;
            }

            Main.Log?.Log("Compat: UI flavour " + Ui
                + (RootUIContext != null ? " (" + RootUIContext.FullName + ")" : " - RootUIContext not found"));
        }
    }

    // Reads RootUIContext to tell whether the player is in plain surface gameplay with no
    // full-screen window, dialogue, or UI grabbing the cursor - the only state mouselook locks in.
    static class UIGate
    {
        static bool _probed;
        // RT (Code.UI.MVVM) gate members.
        static PropertyInfo _instance, _isSurface, _hasDialog, _fullScreenType;
        // WotR (UI.MVVM) gate members - it has no IsSurface/HasDialog/FullScreenUIType, so the same
        // "free, cursor-free exploration" intent is composed from the members it does expose.
        static PropertyInfo _isInGame, _isTacticalCombat, _isGlobalMap, _isKingdom, _isCityBuilder,
                            _isMainMenu, _currentServiceWindow, _saveLoadShown, _modalShown, _changeVisualShown;

        static void Probe()
        {
            if (_probed) return;
            _probed = true;
            Compat.Init();
            var t = Compat.RootUIContext;
            if (t == null) return;
            _instance = AccessTools.Property(t, "Instance");
            if (Compat.Ui == Compat.UiKind.WotR)
            {
                _isInGame             = AccessTools.Property(t, "IsInGame");
                _isTacticalCombat     = AccessTools.Property(t, "IsTacticalCombat");
                _isGlobalMap          = AccessTools.Property(t, "IsGlobalMap");
                _isKingdom            = AccessTools.Property(t, "IsKingdom");
                _isCityBuilder        = AccessTools.Property(t, "IsCityBuilder");
                _isMainMenu           = AccessTools.Property(t, "IsMainMenu");
                _currentServiceWindow = AccessTools.Property(t, "CurrentServiceWindow");
                _saveLoadShown        = AccessTools.Property(t, "SaveLoadIsShown");
                _modalShown           = AccessTools.Property(t, "ModalMessageWindowShown");
                _changeVisualShown    = AccessTools.Property(t, "ChangeVisualIsShown");
            }
            else
            {
                _isSurface      = AccessTools.Property(t, "IsSurface");
                _hasDialog      = AccessTools.Property(t, "HasDialog");
                _fullScreenType = AccessTools.Property(t, "FullScreenUIType");
            }
        }

        static bool IsTrue(PropertyInfo p, object ctx) => p != null && (bool)p.GetValue(ctx);

        public static bool PlainSurface()
        {
            try
            {
                Probe();
                if (_instance == null) return false;
                var ctx = _instance.GetValue(null);
                if (ctx == null) return false;

                if (Compat.Ui == Compat.UiKind.WotR)
                {
                    // Plain surface = in gameplay, not on a map / in kingdom-or-city management /
                    // tactical combat / the main menu, and no full-screen window or modal up.
                    if (_isInGame != null && !(bool)_isInGame.GetValue(ctx)) return false;
                    if (IsTrue(_isTacticalCombat, ctx) || IsTrue(_isGlobalMap, ctx) || IsTrue(_isKingdom, ctx)
                        || IsTrue(_isCityBuilder, ctx) || IsTrue(_isMainMenu, ctx)) return false;
                    if (_currentServiceWindow != null)
                    {
                        var w = _currentServiceWindow.GetValue(ctx);
                        if (w != null && Convert.ToInt32(w) != 0) return false;   // 0 == ServiceWindowsType.None (none open)
                    }
                    if (IsTrue(_saveLoadShown, ctx) || IsTrue(_modalShown, ctx) || IsTrue(_changeVisualShown, ctx)) return false;
                    return true;
                }

                // RT
                if (_isSurface != null && !(bool)_isSurface.GetValue(ctx)) return false;
                if (_hasDialog != null && (bool)_hasDialog.GetValue(ctx)) return false;
                if (_fullScreenType != null)
                {
                    var v = _fullScreenType.GetValue(ctx);
                    if (v != null && Convert.ToInt32(v) != 0) return false;   // 0 == FullScreenUIType.Unknown (none open)
                }
                return true;
            }
            catch { return false; }
        }

        // True while a dialogue is open (RT routes dialogue through a cutscene mode, so this is
        // the signal that distinguishes a conversation from a real cinematic cutscene).
        public static bool HasDialog()
        {
            try
            {
                Probe();
                if (_instance == null || _hasDialog == null) return false;
                var ctx = _instance.GetValue(null);
                if (ctx == null) return false;
                return (bool)_hasDialog.GetValue(ctx);
            }
            catch { return false; }
        }
    }

    // Read-only probe of ToyBox's "Ctrl + Mouse3 Drag To Adjust Camera Elevation" toggle
    // (ToyBox.Settings.toggleCameraElevation, reached via EnhancedCamera.Settings). That option
    // breaks follow-to-unit on load and pans from a map origin, so we surface a warning when it
    // is on. Fails safe to null (ToyBox absent or field renamed) - no warning, no error.
    static class ToyBoxProbe
    {
        static bool _probed;
        static PropertyInfo _settingsProp;   // EnhancedCamera.Settings (static)
        static FieldInfo _elevField;          // ToyBox.Settings.toggleCameraElevation

        static void Probe()
        {
            if (_probed) return;
            _probed = true;
            var ec = AccessTools.TypeByName("ToyBox.EnhancedCamera");
            if (ec != null) _settingsProp = AccessTools.Property(ec, "Settings");
            var st = AccessTools.TypeByName("ToyBox.Settings");
            if (st != null) _elevField = AccessTools.Field(st, "toggleCameraElevation");
        }

        public static bool? CtrlElevationOn()
        {
            try
            {
                Probe();
                if (_settingsProp == null || _elevField == null) return null;
                var s = _settingsProp.GetValue(null);
                if (s == null) return null;
                return _elevField.GetValue(s) is bool b ? b : (bool?)null;
            }
            catch { return null; }
        }

        // Auto-disable: ToyBox's "Ctrl + Mouse3 Drag To Adjust Camera Elevation" makes the camera load at a map
        // origin and clip through the floor while moving under our camera control - a game-breaking combination,
        // so we force it off whenever we're active rather than only warning. Re-asserted each frame so it stays
        // off even if ToyBox reloads its settings; if the user disables our mod the option is theirs again. Logs
        // once. Fails safe to a no-op (ToyBox absent, field renamed, or not writable).
        static bool _loggedElevDisable;
        public static void EnforceCtrlElevationOff()
        {
            try
            {
                Probe();
                if (_settingsProp == null || _elevField == null) return;
                var s = _settingsProp.GetValue(null);
                if (s == null) return;
                if (_elevField.GetValue(s) is bool b && b)
                {
                    _elevField.SetValue(s, false);
                    if (!_loggedElevDisable)
                    {
                        Main.Log?.Log("ToyBox: disabled \"Ctrl + Mouse3 Drag To Adjust Camera Elevation\" - it clips the camera through the floor under our camera control.");
                        _loggedElevDisable = true;
                    }
                }
            }
            catch { }
        }
    }

    // Tiny centre crosshair drawn while mouselook holds the cursor, so the aim point is visible.
    [DefaultExecutionOrder(31000)]   // LateUpdate runs after the game's own per-frame cursor logic so our hold/hide wins
    public class MouselookCrosshair : MonoBehaviour
    {
        // WotR's UpdateCursorMode coroutine sets Cursor.lockState at WaitForEndOfFrame - after LateUpdate - so we
        // also re-assert the hold at end of frame to close that last gap. Idempotent with the LateUpdate re-assert.
        System.Collections.IEnumerator Start()
        {
            var eof = new WaitForEndOfFrame();
            while (true) { yield return eof; Main.CursorLateAssert(); }
        }

        void LateUpdate() => Main.CursorLateAssert();

        void OnGUI()
        {
            if (!Main.CursorLocked || Main.settings == null || !Main.settings.MouselookCrosshair) return;
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f, len = 10f, th = 2f;
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.65f);
            GUI.DrawTexture(new Rect(cx - len, cy - th * 0.5f, len * 2f, th), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - th * 0.5f, cy - len, th, len * 2f), Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }

    // Hides the off-screen *unit* markers - the party-character portrait pointers that
    // ride the screen edge (the PointMarker system, which also drives co-op ping markers
    // and objective/locator pointers). Only markers whose Unit is set are touched, so pings
    // and entity markers (Unit == null) are left alone. Suppression happens at two points
    // for robustness: the VM's IsVisible state and the view's canvas group.
    static class OffscreenMarkerHide
    {
        public static bool ShouldHide()
            => Main.Active && Main.settings != null && Main.settings.HideOffscreenUnitMarkers;

        public static bool IsUnitMarker(object vm)
        {
            if (vm == null) return false;
            var f = Traverse.Create(vm).Field("Unit");
            return f.FieldExists() && f.GetValue() != null;
        }

        // IsVisible may be a plain bool or a reactive property; handle both.
        public static void ForceInvisible(object vm)
        {
            var f = Traverse.Create(vm).Field("IsVisible");
            if (!f.FieldExists()) return;
            var val = f.GetValue();
            if (val is bool) { f.SetValue(false); return; }
            if (val != null)
            {
                var vp = Traverse.Create(val).Property("Value");
                if (vp.PropertyExists()) vp.SetValue(false);
            }
        }
    }

    [HarmonyPatch]
    static class PointMarkerVM_UpdateVisibility_Patch
    {
        static MethodBase TargetMethod() => AccessTools.Method("Kingmaker.Code.UI.MVVM.VM.PointMarkers.PointMarkerVM:UpdateVisibility");
        static bool Prepare() => TargetMethod() != null;
        static void Postfix(object __instance)
        {
            try
            {
                if (!OffscreenMarkerHide.ShouldHide()) return;
                if (!OffscreenMarkerHide.IsUnitMarker(__instance)) return;
                OffscreenMarkerHide.ForceInvisible(__instance);
            }
            catch (Exception e) { Main.Log?.Error("Marker hide (VM) failed: " + e); }
        }
    }

    [HarmonyPatch]
    static class PointMarkerPCView_SetVisibility_Patch
    {
        static MethodBase TargetMethod() => AccessTools.Method("Kingmaker.Code.UI.MVVM.View.PointMarkers.PointMarkerPCView:SetVisibility");
        static bool Prepare() => TargetMethod() != null;
        static void Postfix(object __instance)
        {
            try
            {
                if (!OffscreenMarkerHide.ShouldHide()) return;
                var vm = Traverse.Create(__instance).Property("ViewModel").GetValue<object>();
                if (!OffscreenMarkerHide.IsUnitMarker(vm)) return;
                var cg = Traverse.Create(__instance).Field("m_CanvasGroup").GetValue<CanvasGroup>();
                if (cg != null) { cg.alpha = 0f; cg.blocksRaycasts = false; cg.interactable = false; }
            }
            catch (Exception e) { Main.Log?.Error("Marker hide (view) failed: " + e); }
        }
    }

    [HarmonyPatch]
    static class PointMarker_Show_Patch   // WotR: the older non-MVVM off-screen party markers
    {
        // WotR drives the edge party-portrait pointers through Kingmaker.UI.PointMarker.PointMarker, whose
        // controller calls Show() each frame a character is off-screen. Show() runs a DOTween fade-in, so we
        // skip it entirely (Prefix returns false) and force the canvas hidden - that way nothing tweens it back.
        static MethodBase TargetMethod() => AccessTools.Method("Kingmaker.UI.PointMarker.PointMarker:Show");
        static bool Prepare() => TargetMethod() != null;
        static bool Prefix(object __instance)
        {
            try
            {
                if (!OffscreenMarkerHide.ShouldHide()) return true;
                var ch = Traverse.Create(__instance).Property("Character");
                if (ch.PropertyExists() && ch.GetValue() == null) return true;   // not a character marker; leave it
                var cg = Traverse.Create(__instance).Property("CanvasGroup").GetValue<CanvasGroup>();
                if (cg != null) { cg.alpha = 0f; cg.blocksRaycasts = false; cg.interactable = false; }
                return false;   // skip the fade-in so the edge pointer never appears
            }
            catch (Exception e) { Main.Log?.Error("Marker hide (WotR) failed: " + e); return true; }
        }
    }

    // View-1-on-load: hook the save-load entry points (NOT area transitions) and arm the deferred apply.
    // Patching all three covers main-menu load, in-game load, and quick-load; whichever fires sets the flag,
    // and it is consumed once on the next area-did-load, so re-entrancy between them is harmless.
    [HarmonyPatch]
    static class Game_LoadGame_Patch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var n in new[] { "LoadGame", "QuickLoadGame", "LoadGameFromMainMenu" })
            {
                var m = AccessTools.Method("Kingmaker.Game:" + n);
                if (m != null) yield return m;
            }
        }
        static bool Prepare() => AccessTools.Method("Kingmaker.Game:LoadGame") != null;
        static void Postfix() { Main.NotifyGameLoad(); }
    }

    // Area finished loading: turn a pending save-load into the View-1 apply countdown (area transitions, which
    // never set the pending flag, fall straight through and are left alone).
    [HarmonyPatch]
    static class CameraRig_OnAreaDidLoad_Patch
    {
        static MethodBase TargetMethod() => AccessTools.Method("Kingmaker.View.CameraRig:OnAreaDidLoad");
        static bool Prepare() => TargetMethod() != null;
        static void Postfix() { Main.NotifyAreaDidLoad(); }
    }

    // Captures the live CameraUnitFollower instance as the game establishes or changes the camera's follow
    // target. CameraUnitFollower is a plain class nested in CameraController (not a Unity component), so it
    // can't be found via FindObjectOfType; a postfix on its Follow/TryFollow hands us the instance directly.
    [HarmonyPatch]
    static class CameraFollower_Capture_Patch
    {
        static Type FollowerType()
        {
            Type t = AccessTools.TypeByName("Kingmaker.Controllers.Rest.CameraController+CameraUnitFollower");
            if (t != null) return t;
            // Namespace metadata read blank for this nested type; fall back to a one-time name scan.
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { foreach (var x in asm.GetTypes()) if (x.Name == "CameraUnitFollower") return x; }
                catch { }
            }
            return null;
        }

        static bool Prepare() => FollowerType() != null;

        static IEnumerable<MethodBase> TargetMethods()
        {
            Type ft = FollowerType();
            if (ft == null) yield break;
            foreach (var m in AccessTools.GetDeclaredMethods(ft))
                if ((m.Name == "Follow" || m.Name == "TryFollow") && !m.IsAbstract)
                    yield return m;
        }

        static void Postfix(object __instance) => CameraRig_UpdateInternal_Patch.NotifyFollower(__instance);
    }




    // ------------------------------------------------------------------------------------------------
    // Camera-cutscene gate.
    //
    // NOTE: the "mod borks cutscene camera moves" issue was diagnosed here - see CUTSCENE_DETECTION.md. It was
    // NOT a detection gap: in-dialogue scripted shots ARE bracketed by this gate (CameraCutsceneActive). The
    // borking was PitchRange, ClipPlane and ZoomLimits applying during the shot - they had been gated only on
    // hardBind, now also stood down on InCutscene/CameraCutsceneActive. DontForceLookAtTarget was a red herring.
    // In-dialogue scripted shots (e.g. CamOnReceiver) are driven by CommandControlCamera; its OnRun marks the
    // start. They emit no usable end event - no OnStop/Interrupt/IsFinished on the command, and on the common
    // path no CutscenePlayerData.Stop/IsFinished either - the camera simply holds the last shot until the
    // conversation ends. So the gate brackets "first scripted shot -> the game leaves Dialog mode": active is
    // set on OnRun (only while in dialogue, so gameplay camera commands and ambient barks never trip it) and
    // cleared by Release() once we are no longer in a dialogue. While active, the framing offset and the
    // see-through-wall suppression stand down so the authored shot renders. Ordinary dialogue with no scripted
    // shot never sets it, so custom framing stays live there. Full cutscenes are left to FramingPauseInCutscenes
    // and the hard-bind standdown, unchanged.
    static class CutsceneCameraGate
    {
        static bool _active;
        static bool _engaged;       // a scripted shot fired during the current conversation (since the last re-stamp)
        static bool _prevInDialog;  // last frame's dialogue state, for end-edge detection
        static float _answerReclaim; // seconds left on the answer debounce; > 0 = a mid-conversation re-stamp is pending

        // After a dialogue advance, wait this long to see whether the next cue brings its own scripted shot before
        // re-stamping the player's view. Time-based so it holds across frame rates; ~the click-to-OnRun latency.
        const float AnswerReclaimSeconds = 0.5f;

        internal static bool CameraCutsceneActive() => _active;
        internal static void Reset() { _active = false; _engaged = false; _prevInDialog = false; _answerReclaim = 0f; }

        // CommandControlCamera.OnRun: a scripted camera shot has begun. Only latch inside a dialogue - a shot in
        // a full cutscene is governed by the existing cutscene handling, and a stray gameplay-mode camera command
        // has no dialogue exit to release on. A new shot also cancels any pending answer re-stamp: this beat is
        // cinematic too, so let it play rather than flashing the player's view between the two shots.
        internal static void OnCamCommandRun()
        {
            if (CameraRig_UpdateInternal_Patch.InDialogMode())
            {
                _active = true;
                _engaged = true;
                _answerReclaim = 0f;
                Main.ZeroRigPitchForCutscene();   // base-game shots assume level pitch; hand the camera back at 0
            }
        }

        // PlayCue: a new cue is on screen, so the dialogue advanced. Arm the re-stamp debounce only if a scripted
        // shot ran since the last re-stamp; if this cue carries its own shot, OnCamCommandRun cancels the debounce
        // before it elapses. Fires for every cue regardless of how the advance happened, so plain continues and
        // auto-chained cues release the held shot too - not just explicit answer picks.
        internal static void OnDialogAdvance()
        {
            if (_engaged) _answerReclaim = AnswerReclaimSeconds;
        }

        // Per-frame tick of the mid-conversation re-stamp. When the debounce elapses with no new shot having
        // superseded it, the next beat is ordinary dialogue, so release the gate as well as re-stamping: the
        // per-frame framing offset (dolly/shoulder/pivot) and the solid-wall hold both stand down while a shot
        // owns the camera, and only clearing _active lets them resume. Without it the pitch returns but the
        // camera stays pulled back and unoccluded - which is what set this apart from the dialogue-end path,
        // where ReleaseAndCheckReapply already clears _active.
        internal static bool TickAnswerReclaim(float dt)
        {
            if (_answerReclaim <= 0f) return false;
            _answerReclaim -= dt;
            if (_answerReclaim <= 0f) { _answerReclaim = 0f; _engaged = false; _active = false; return true; }
            return false;
        }

        // Called once per frame. Releases the gate the frame the conversation ends, and returns true on that same
        // frame when a scripted shot still owned the camera (no answer re-stamp consumed it) - the caller then
        // re-stamps the active view, because the exit blend restores the game's own pitch/zoom and leaves the
        // per-frame focus offset layered on the wrong base.
        internal static bool ReleaseAndCheckReapply(bool inDialog)
        {
            if (!inDialog) { _active = false; _answerReclaim = 0f; }
            bool justEnded = _prevInDialog && !inDialog;
            _prevInDialog = inDialog;
            if (justEnded && _engaged) { _engaged = false; return true; }
            if (!inDialog) _engaged = false;
            return false;
        }
    }

    [HarmonyPatch]
    static class ControlCamera_OnRun_Gate
    {
        static MethodBase TargetMethod() => Compat.ResolveCommandControlCameraOnRun();
        static bool Prepare() => TargetMethod() != null;
        static void Postfix() => CutsceneCameraGate.OnCamCommandRun();
    }

    // The player advancing the dialogue is the only event that brackets an in-dialogue scripted shot (the shots
    // hold until the next click and fire no Stop/Interrupt/IsFinished of their own). Hook the two-argument
    // DialogController.PlayCue(BlueprintCueBase) - fires once for every cue actually shown, however the advance
    // happened (answer pick, continue, or auto-chain). The two SelectAnswer overloads only saw explicit answer
    // selections and missed plain continues entirely, which is why the per-shot reclaim never armed. Single
    // private overload, so the name resolves unambiguously without referencing the game's dialogue types.
    [HarmonyPatch]
    static class DialogController_PlayCue_Reclaim
    {
        static MethodBase TargetMethod() => AccessTools.Method("Kingmaker.Controllers.Dialog.DialogController:PlayCue");
        static bool Prepare() => TargetMethod() != null;
        static void Postfix() => CutsceneCameraGate.OnDialogAdvance();
    }

    // ------------------------------------------------------------------------------------------------
    // Gamepad right-stick capture. Rewired decodes the stick and hands the vector to the exploration input
    // layer's OnMoveRightStick(InputActionEventData, Vector2); we read the already-decoded Y off the Vector2
    // (no raw-axis access) and stamp the frame, so TickGamepadPitchHold can drive View 1's pitch from it.
    // Both RT (SurfaceMainInputLayer) and WotR (InGameInputLayer) put the stick vector at argument index 1,
    // verified against both assemblies, so we bind it positionally as __1. (We deliberately avoid the __args
    // all-arguments injection: the older Harmony bundled with WotR doesn't support it and throws at patch time.)
    // Surface exploration only - that is where View 1 is used; combat/space layers would be separate hooks.
    [HarmonyPatch]
    static class SurfaceRightStick_Capture
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method("Kingmaker.Code.UI.MVVM.View.Surface.InputLayers.SurfaceMainInputLayer:OnMoveRightStick")
            ?? AccessTools.Method("Kingmaker.UI._ConsoleUI.InputLayers.InGameLayer.InGameInputLayer:OnMoveRightStick");
        static bool Prepare() => TargetMethod() != null;
        static void Postfix(Vector2 __1)
        {
            Main.GpRightStickY = __1.y;
            Main.GpRightStickFrame = Time.frameCount;
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Gamepad free-aim cursor (per-view). RT surface exploration drives both the camera pan and the centred
    // pointer from the left stick inside UpdateLeftStickMovement. When the active view has PadFreeAimCursor
    // ticked and we're in cursor control on a pad, we skip that method and move the pointer ourselves, so the
    // camera stays put and the left stick free-aims the cursor. RT-only: WotR has no SurfaceMainInputLayer,
    // so TargetMethod is null there and the class is skipped. The right stick is left to its normal camera use.
    [HarmonyPatch]
    static class SurfaceLeftStick_FreeAimPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method("Kingmaker.Code.UI.MVVM.View.Surface.InputLayers.SurfaceMainInputLayer:UpdateLeftStickMovement");
        static bool Prepare() => TargetMethod() != null;
        static bool Prefix(object __instance) => !Main.TryPadFreeAimLeftStick(__instance);
    }

    // ------------------------------------------------------------------------------------------------
    // Input-layer instance capture (WotR). WotR's InGameInputLayer has no static Instance, so cache the
    // live layer from its per-frame OnUpdate for the on-load direct-control flip. RT resolves Instance
    // statically, so this patch's WotR-only target is absent there and the class is simply skipped.
    [HarmonyPatch]
    static class InputLayerInstance_Capture
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method("Kingmaker.UI._ConsoleUI.InputLayers.InGameLayer.InGameInputLayer:OnUpdate");
        static bool Prepare() => TargetMethod() != null;
        static void Postfix(object __instance) => Main.CachedInputLayer = __instance;
    }

    // ------------------------------------------------------------------------------------------------
    // WASD/stick movement inversion fix.
    //
    // The rig maps stick to a world move as dir = stick.x * CameraRig.Right + stick.y * CameraRig.Up, where Up is
    // the camera's forward flattened onto the ground. Logged across the horizon, Up FLIPS SIGN the instant the
    // look crosses level while Right stays put - so the forward axis reverses and "hold W" walks backward above
    // the horizon. Right is stable, and below the horizon the game's own Up equals Cross(Right, worldUp) exactly;
    // so we recompute Up as that stable perpendicular. No-op below the horizon (matches the game), corrects the
    // flipped sign above it - leaving stick movement and edge-scroll consistent at every pitch.
    [HarmonyPatch]
    static class CameraRig_ScreenBasis_UpFix
    {
        static MethodBase TargetMethod() => AccessTools.Method("Kingmaker.View.CameraRig:FigureOutScreenBasis");
        static bool Prepare() => TargetMethod() != null;
        static void Postfix(object __instance)
        {
            try
            {
                var rig = Traverse.Create(__instance);
                Vector3 right = rig.Property("Right").GetValue<Vector3>();
                Vector3 up = Vector3.Cross(right, Vector3.up);   // == game's Up below the horizon; stays put above it
                float m = up.magnitude;
                if (m > 1e-4f) rig.Property("Up").SetValue(up / m);
            }
            catch (Exception e) { Main.Log?.Error("ScreenBasis Up-fix failed: " + e); }
        }
    }
}
