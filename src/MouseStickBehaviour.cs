using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using VTOLVR.Multiplayer;

namespace MouseStick
{
    // The flight-control value itself is injected by VehicleInputPatch. This component reads the mouse,
    // draws the overlay, and moves the cockpit stick models to match (late, after BYOJ / VRJoystick Update).
    [DefaultExecutionOrder(10000)]
    public class MouseStickBehaviour : MonoBehaviour
    {
        private const float ToastSeconds = 1.6f;

        private static readonly AccessTools.FieldRef<VRJoystick, bool> RemoteOnlyRef =
            AccessTools.FieldRefAccess<VRJoystick, bool>("remoteOnly");

        public static bool IsActive { get; private set; }
        // Mouse stick on, but Left Alt held: cursor is free for cockpit clicks, keyboard still flies.
        public static bool ClickMode { get; private set; }
        // FlatScreen 3 hover/click is suppressed while the mouse is flying the aircraft or driving an SOI page.
        public static bool SuppressCockpitHover => (IsActive && !ClickMode) || SoiMode;
        // Mouse stick on and G held: the mouse aims the targeting pod; keyboard still flies, LMB still fires.
        // G mode: the mouse is the cursor of the SOI MFD page (TGP aiming, radar/ARAD/map cursor), scroll zooms it.
        public static bool SoiMode { get; private set; }

        private readonly SoiCursor _soi = new SoiCursor();
        private static MouseStickBehaviour _instance;

        public static void ShowToastStatic(string text) => _instance?.ShowToast(text);
        // Pitch, yaw, roll in VRJoystick convention; read by VehicleInputPatch.
        public static Vector3 Output { get; private set; }
        public static VehicleInputManager TargetInputManager { get; private set; }

        // Primary stick: receives trigger events. All local sticks get the deflection for animation.
        private VRJoystick _stick;
        private readonly List<VRJoystick> _sticks = new List<VRJoystick>();
        private readonly Dictionary<VRJoystick, bool> _savedReturnToZero = new Dictionary<VRJoystick, bool>();

        private Vector2 _virtualPos;  // raw mouse-driven position, -1..1 per axis
        private float _yaw;
        private Vector3 _output
        {
            get => Output;
            set => Output = value;
        }
        private bool _triggerHeld;
        private bool _freeLook;
        private bool _skipNextDelta;

        // Diagnostics, logged on deactivate: total movement seen by each mouse source.
        private float _win32Travel;
        private float _unityTravel;

        private bool _ctrlTracking;
        private bool _ctrlTapValid;
        private float _ctrlDownTime;

        private Vector2 _keyboard;    // WASD contribution, in mouse space, -1..1 per axis
        private Vector2 _stickPos;    // mouse + keyboard, what the dot shows and what gets shaped

        private KeyCode _rudderLeft;
        private KeyCode _rudderRight;
        private KeyCode _pitchDown, _pitchUp, _rollLeft, _rollRight;
        private KeyCode _menuKey;
        private KeyCode _clickKey;
        private KeyCode _tgpKey;
        private KeyCode _toggleKey, _toggleKey2;
        private KeyCode _soiHoldKey;
        private bool _soiToggled;      // SOI mode latched on by the toggle key (G)
        private bool _holdActivated;   // stick control was switched on by holding the hold key (Left Alt)
        private KeyCode[] _releaseKeys = new KeyCode[0];

        private string _toast;
        private float _toastUntil;

        private readonly Vector2[] _outline = new Vector2[4];

        private Texture2D _white;
        private Texture2D _dot;
        private GUIStyle _labelStyle;

        private static MouseStickSettings S => MouseStickSettings.Current;

        private void Awake()
        {
            _instance = this;
            SceneManager.activeSceneChanged += OnSceneChanged;
            ParseKeys();
        }

        private void ParseKeys()
        {
            _rudderLeft = MouseStickSettings.ParseKey(S.rudderLeftKey);
            _rudderRight = MouseStickSettings.ParseKey(S.rudderRightKey);
            _pitchDown = MouseStickSettings.ParseKey(S.pitchDownKey);
            _pitchUp = MouseStickSettings.ParseKey(S.pitchUpKey);
            _rollLeft = MouseStickSettings.ParseKey(S.rollLeftKey);
            _rollRight = MouseStickSettings.ParseKey(S.rollRightKey);
            _menuKey = MouseStickSettings.ParseKey(S.menuKey);
            _clickKey = MouseStickSettings.ParseKey(S.clickModeKey);
            _tgpKey = MouseStickSettings.ParseKey(S.tgpModeKey);
            _toggleKey = MouseStickSettings.ParseKey(S.toggleKey);
            _toggleKey2 = MouseStickSettings.ParseKey(S.toggleKey2);
            _soiHoldKey = MouseStickSettings.ParseKey(S.soiHoldKey);
            _releaseKeys = new KeyCode[S.releaseKeys.Length];
            for (int i = 0; i < _releaseKeys.Length; i++)
                _releaseKeys[i] = MouseStickSettings.ParseKey(S.releaseKeys[i]);
        }

        private static bool IsHeld(KeyCode k) => k != KeyCode.None && Input.GetKey(k);
        private static bool IsDown(KeyCode k) => k != KeyCode.None && Input.GetKeyDown(k);

        private static float KeyAxis(KeyCode positive, KeyCode negative) =>
            (positive != KeyCode.None && Input.GetKey(positive) ? 1f : 0f) - (negative != KeyCode.None && Input.GetKey(negative) ? 1f : 0f);

        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= OnSceneChanged;
            Deactivate(silent: true);
            if (_white != null) Destroy(_white);
            if (_dot != null) Destroy(_dot);
        }

        private void OnSceneChanged(Scene from, Scene to)
        {
            Deactivate(silent: true);
            if (SoiMode)
                EndSoiWithStickOff();
            _soiToggled = false;
            SettingsWindow.Close();
            ScreenPointer.Reset();
            _stick = null;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!IsActive || SettingsWindow.IsOpen)
                return;
            if (focused)
            {
                Win32Mouse.Capture();
                _skipNextDelta = true;
            }
            else
            {
                Win32Mouse.Release();
            }
        }

        // ---------------------------------------------------------------- input

        private void Update()
        {
            // Rebinding a key in the settings window: swallow input so the mod's own shortcuts don't fire.
            if (SettingsWindow.UpdateKeyCapture())
                return;
            if (SettingsWindow.KeysChanged)
            {
                SettingsWindow.KeysChanged = false;
                ParseKeys();
            }

            HandleMenuKey();
            HandleToggleKey();
            HandleAutoEnable();

            // Cockpit screens (MFD buttons, touchscreens) are clicked through ScreenPointer whenever the free cursor is in
            // use: stick off, or clickable mode. The FlatScreen 3 hook that hands them over is installed up front.
            if (ScreenPointer.Available)
            {
                FlatScreenCompat.TryPatch();
                bool freeCursor = !SuppressCockpitHover && !SettingsWindow.CursorOverWindow && !SoiMode;
                ScreenPointer.Update(S.handleScreens, freeCursor, GetPlayerVehicle());
            }

            if (!IsActive)
            {
                UpdateSoiWithStickOff();
                return;
            }

            foreach (var key in _releaseKeys)
            {
                if (key != KeyCode.None && Input.GetKeyDown(key))
                {
                    // Release keys hand the cursor to FlatScreen 3's menus, so SOI mode must not grab it back.
                    _soiToggled = false;
                    Deactivate();
                    return;
                }
            }

            // Settings window open, or clickable mode (hold Left Alt while the stick is on): hand the cursor back so the
            // window / cockpit can be used, and freeze the mouse stick. WASD and rudder keep flying the aircraft.
            // If Alt itself switched the stick on (it was off), holding it means "stick on", not clickable mode.
            bool menuOpen = SettingsWindow.IsOpen;
            bool wasClickMode = ClickMode;
            ClickMode = !menuOpen && !_holdActivated && IsHeld(_clickKey);
            if (menuOpen || ClickMode)
            {
                Win32Mouse.Release();
                if (menuOpen || !wasClickMode)
                    Cursor.visible = true;
                _skipNextDelta = true;
            }

            if (!EnsureStick())
            {
                Deactivate();
                ShowToast("Mouse stick OFF (no stick)");
                return;
            }

            // SOI mode: the mouse is the cursor of the SOI page instead of flying. Cursor stays captured, WASD keeps
            // flying. G toggles it; Mouse button 4 flips it while held. Clickable mode and the menu take priority.
            if (!menuOpen && !ClickMode && IsDown(_tgpKey))
                _soiToggled = !_soiToggled;
            bool wasSoiMode = SoiMode;
            SoiMode = !menuOpen && !ClickMode && (_soiToggled ^ IsHeld(_soiHoldKey));
            if (SoiMode && !wasSoiMode)
            {
                _soi.Begin(GetPlayerVehicle());
                if (_soi.Current == SoiCursor.Kind.None)
                    ShowToast("No SOI page selected");
            }
            else if (!SoiMode && wasSoiMode)
            {
                _soi.End();
            }

            // FlatScreen 3 uses RMB-drag to look around. Freeze the stick while it is held so looking doesn't fly the jet,
            // and stop warping the cursor so FlatScreen 3 sees normal mouse movement.
            bool wasFreeLook = _freeLook;
            _freeLook = Input.GetMouseButton(1);
            float dt = Time.unscaledDeltaTime;

            _unityTravel += Mathf.Abs(Input.GetAxisRaw("Mouse X")) + Mathf.Abs(Input.GetAxisRaw("Mouse Y"));

            bool lmb = !menuOpen && !ClickMode && Input.GetMouseButton(0);

            Vector2 delta = Vector2.zero;
            if (Application.isFocused && !_freeLook && !menuOpen && !ClickMode)
            {
                if (!Win32Mouse.IsCaptured)
                    Win32Mouse.Capture();
                // First frame after capture, free look or the menu: just re-centre so the stick doesn't jump.
                Vector2 px = Win32Mouse.ReadDelta(recenterOnly: wasFreeLook || _skipNextDelta);
                _skipNextDelta = false;
                _win32Travel += Mathf.Abs(px.x) + Mathf.Abs(px.y);
                if (SoiMode)
                    _soi.Update(px, lmb, S.tgpSensitivity, S.cursorSensitivity);
                else
                    // At sensitivity 1 the dot follows the mouse 1:1, so half the control area = full deflection.
                    delta = px * (S.sensitivity / (S.overlaySize * 0.5f));
            }
            else if (SoiMode)
            {
                // Still lets the pod re-lock / the cursor snap after a free-look pause.
                _soi.Update(Vector2.zero, lmb, S.tgpSensitivity, S.cursorSensitivity);
            }
            _virtualPos += delta;

            // SOI mode: the wheel presses the page's own zoom / range buttons.
            if (SoiMode)
            {
                float scroll = Input.mouseScrollDelta.y;
                if (scroll > 0f) _soi.Zoom(+1);
                else if (scroll < 0f) _soi.Zoom(-1);
                // Middle mouse: the page's re-centre (TGP FWD, map reset, TSD centre, radar unlock, ARAD deselect).
                if (Input.GetMouseButtonDown(2))
                    _soi.Recenter();
            }

            if (S.autoCenterRate > 0f && delta == Vector2.zero)
                _virtualPos = Vector2.MoveTowards(_virtualPos, Vector2.zero, S.autoCenterRate * dt);

            if (S.middleMouseRecenters && !ClickMode && !SoiMode && Input.GetMouseButtonDown(2))
                _virtualPos = Vector2.zero;

            if (S.circularLimit)
                _virtualPos = Vector2.ClampMagnitude(_virtualPos, 1f);
            else
                _virtualPos = new Vector2(Mathf.Clamp(_virtualPos.x, -1f, 1f), Mathf.Clamp(_virtualPos.y, -1f, 1f));

            _yaw = Mathf.MoveTowards(_yaw, KeyAxis(_rudderRight, _rudderLeft), S.rudderRate * dt);

            // WASD springs back to centre on release. W is always stick forward (nose down), whatever invertPitch
            // does to the mouse, so it is converted into mouse space here and back out by the same sign below.
            float inv = S.invertPitch ? -1f : 1f;
            Vector2 kbTarget = new Vector2(KeyAxis(_rollRight, _rollLeft), KeyAxis(_pitchDown, _pitchUp) * inv);
            // Per axis: a held key ramps at keyboardRate; with no key, the axis drifts back to centre at its own return
            // rate (x = roll, A/D; y = pitch, W/S).
            _keyboard.x = Mathf.MoveTowards(_keyboard.x, kbTarget.x, (kbTarget.x != 0f ? S.keyboardRate : S.keyboardReturnRateRoll) * dt);
            _keyboard.y = Mathf.MoveTowards(_keyboard.y, kbTarget.y, (kbTarget.y != 0f ? S.keyboardRate : S.keyboardReturnRatePitch) * dt);

            _stickPos = _virtualPos + _keyboard;
            _stickPos = S.circularLimit
                ? Vector2.ClampMagnitude(_stickPos, 1f)
                : new Vector2(Mathf.Clamp(_stickPos.x, -1f, 1f), Mathf.Clamp(_stickPos.y, -1f, 1f));

            // VRJoystick axes: x = pitch (+ = stick forward / nose down), y = yaw (+ = right), z = roll (+ = left).
            Vector2 shaped = Shape(_stickPos);
            _output = new Vector3(shaped.y * inv, _yaw, -shaped.x);

            // On radar / ARAD / TSD in SOI mode, LMB belongs to the page (handled in SoiCursor), not the trigger.
            bool lmbIsSoiButton = SoiMode && _soi.LmbIsThumbstickPress;
            if (S.leftMouseFiresTrigger)
                SetTrigger(!lmbIsSoiButton && lmb);
        }

        // SOI cursor mode while the mouse stick is off: same keys and behaviour as with the stick on (G toggles,
        // Mouse button 4 flips while held, Esc leaves it), with the cursor captured only while the mode is on.
        private void UpdateSoiWithStickOff()
        {
            bool menuOpen = SettingsWindow.IsOpen;
            GameObject vehicle = GetPlayerVehicle();
            if (vehicle == null)
            {
                _soiToggled = false;
                if (SoiMode)
                    EndSoiWithStickOff();
                return;
            }

            if (!menuOpen && IsDown(_tgpKey))
                _soiToggled = !_soiToggled;
            foreach (var key in _releaseKeys)
                if (key != KeyCode.None && Input.GetKeyDown(key))
                    _soiToggled = false;

            bool want = !menuOpen && (_soiToggled ^ IsHeld(_soiHoldKey));
            if (want && !SoiMode)
            {
                SoiMode = true;
                FindPlayerControls(); // trigger stick, so LMB can still fire on TGP / map pages
                _soi.Begin(vehicle);
                if (_soi.Current == SoiCursor.Kind.None)
                    ShowToast("No SOI page selected");
                Cursor.visible = false;
                Win32Mouse.Capture();
                _skipNextDelta = true;
            }
            else if (!want && SoiMode)
            {
                EndSoiWithStickOff();
            }
            if (!SoiMode)
                return;

            bool lmb = Input.GetMouseButton(0);
            bool wasFreeLook = _freeLook;
            _freeLook = Input.GetMouseButton(1);

            Vector2 px = Vector2.zero;
            if (Application.isFocused && !_freeLook)
            {
                if (!Win32Mouse.IsCaptured)
                    Win32Mouse.Capture();
                px = Win32Mouse.ReadDelta(recenterOnly: wasFreeLook || _skipNextDelta);
                _skipNextDelta = false;
            }
            else
            {
                Win32Mouse.Release(); // free look (RMB) or alt-tabbed
                _skipNextDelta = true;
            }
            _soi.Update(px, lmb, S.tgpSensitivity, S.cursorSensitivity);

            float scroll = Input.mouseScrollDelta.y;
            if (scroll > 0f) _soi.Zoom(+1);
            else if (scroll < 0f) _soi.Zoom(-1);
            if (Input.GetMouseButtonDown(2))
                _soi.Recenter();

            if (S.leftMouseFiresTrigger)
                SetTrigger(!_soi.LmbIsThumbstickPress && lmb);
        }

        private void EndSoiWithStickOff()
        {
            SoiMode = false;
            _soi.End();
            SetTrigger(false);
            Win32Mouse.Release();
            Cursor.visible = true;
        }

        private void HandleMenuKey()
        {
            if (_menuKey != KeyCode.None && Input.GetKeyDown(_menuKey))
            {
                SettingsWindow.Toggle();
                if (SettingsWindow.IsOpen)
                    FlatScreenCompat.TryPatch();
            }
            else if (SettingsWindow.IsOpen && Input.GetKeyDown(KeyCode.Escape))
            {
                SettingsWindow.Close();
            }
        }

        private void LateUpdate()
        {
            if (!IsActive && SoiMode)
            {
                // SOI mode with the stick off: keep the captured cursor hidden (FlatScreen 3 re-shows it on movement).
                if (!_freeLook && !SettingsWindow.IsOpen)
                    Cursor.visible = false;
                if (_triggerHeld && _stick != null)
                    _stick.OnTriggerAxis?.Invoke(1f);
                return;
            }
            if (!IsActive || _stick == null)
                return;

            // FlatScreen 3 re-shows the cursor in its LateUpdate whenever the mouse moves; we run after it.
            if (!_freeLook && !SettingsWindow.IsOpen && !ClickMode)
                Cursor.visible = false;

            ApplyStickVisual(_output);
            if (_triggerHeld)
                _stick.OnTriggerAxis?.Invoke(1f);
        }

        private void HandleToggleKey()
        {
            bool held = IsHeld(_toggleKey) || IsHeld(_toggleKey2);
            bool pressed = IsDown(_toggleKey) || IsDown(_toggleKey2);

            if (S.HoldMode)
            {
                if (pressed && !IsActive)
                    Activate();
                else if (!held && IsActive && !_holdActivated)
                    Deactivate();
                HandleHoldStickKey();
                return;
            }

            // Tap mode: only a clean tap toggles, so CTRL+Z (FlatScreen 3 camera reset), CTRL+scroll zoom
            // and BYOJ keyboard combos keep working.
            if (pressed && !_ctrlTracking)
            {
                _ctrlTracking = true;
                _ctrlTapValid = true;
                _ctrlDownTime = Time.unscaledTime;
            }
            else if (_ctrlTracking && held)
            {
                if (Input.anyKeyDown || Input.mouseScrollDelta != Vector2.zero)
                    _ctrlTapValid = false;
            }

            if (_ctrlTracking && !held)
            {
                _ctrlTracking = false;
                if (_ctrlTapValid && Time.unscaledTime - _ctrlDownTime <= S.tapMaxSeconds)
                {
                    if (IsActive)
                        Deactivate();
                    else
                        Activate();
                }
            }

            HandleHoldStickKey();
        }

        // Turns stick control on once per spawn: when a new player aircraft appears, wait for the cockpit to settle,
        // then activate as soon as its stick can be found. Turning it off afterwards sticks until the next spawn.
        private const float AutoEnableDelay = 1.5f;
        private const float AutoEnableGiveUp = 10f;
        private GameObject _autoVehicle;   // aircraft the current auto-enable attempt is for
        private float _autoSince;
        private bool _autoDone;

        private void HandleAutoEnable()
        {
            if (!S.enableOnSpawn)
                return;
            GameObject vehicle = GetPlayerVehicle();
            if (vehicle != _autoVehicle)
            {
                _autoVehicle = vehicle;
                _autoSince = Time.unscaledTime;
                _autoDone = vehicle == null;
                return;
            }
            if (_autoDone || IsActive)
            {
                _autoDone = true;
                return;
            }
            float waited = Time.unscaledTime - _autoSince;
            if (waited < AutoEnableDelay)
                return;
            if (waited > AutoEnableGiveUp)
            {
                _autoDone = true;
                return;
            }
            FindPlayerControls();
            if (_stick == null)
                return; // cockpit not ready yet; try again next frame
            _autoDone = true;
            Activate();
        }

        // "Stick control while held" (Left Alt): flips stick control for as long as it's held. With the stick off,
        // holding it turns the stick on until release. With the stick on, holding it gives clickable mode (see Update).
        private void HandleHoldStickKey()
        {
            if (IsDown(_clickKey) && !IsActive)
            {
                Activate();
                _holdActivated = IsActive;
            }
            else if (_holdActivated && !IsHeld(_clickKey))
            {
                Deactivate();
            }
        }

        // Output for a stick deflected purely along one axis by a (0..1): the same deadzone + curve as Shape.
        // Used by the settings window graph.
        public static float AxisResponse(float a)
        {
            a = Mathf.Clamp01(a);
            if (a <= S.deadzone)
                return 0f;
            return Curve((a - S.deadzone) / (1f - S.deadzone));
        }

        // Round deadzone: inside it the stick is zero; outside, travel is rescaled so output still starts at 0
        // at the deadzone edge and reaches 1 at the box edge. Then the per-axis centre curve.
        private Vector2 Shape(Vector2 v)
        {
            float m = v.magnitude;
            if (m <= S.deadzone || m < 1e-5f)
                return Vector2.zero;
            v *= (m - S.deadzone) / (1f - S.deadzone) / m;
            return new Vector2(Curve(v.x), Curve(v.y));
        }

        // Inverse cubic: (1-k)x + k(1-(1-x)^3). Slope is 1+2k at centre and 1-k at the edge, so sensitivity is
        // highest near centre; always increasing, and 1 still maps to 1.
        private static float Curve(float v)
        {
            float a = Mathf.Clamp01(Mathf.Abs(v));
            float k = S.curve;
            float b = 1f - a;
            return Mathf.Sign(v) * ((1f - k) * a + k * (1f - b * b * b));
        }

        // ---------------------------------------------------------------- activation

        private void Activate()
        {
            // Keep unsaved changes from the settings window, then pick up any hand edits to the file.
            MouseStickSettings.SaveIfDirty();
            MouseStickSettings.Load();
            ParseKeys();

            _stick = null;
            if (!EnsureStick())
            {
                ShowToast("Mouse stick: no flyable stick in this seat");
                return;
            }

            FlatScreenCompat.TryPatch();

            if (S.recenterOnEnable)
            {
                _virtualPos = Vector2.zero;
                _yaw = 0f;
            }
            _keyboard = Vector2.zero;
            _stickPos = _virtualPos;
            _output = Vector3.zero;

            _savedReturnToZero.Clear();
            foreach (var js in _sticks)
            {
                _savedReturnToZero[js] = js.returnToZeroWhenReleased;
                js.returnToZeroWhenReleased = false;
            }

            IsActive = true;
            _win32Travel = 0f;
            _unityTravel = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = false;
            if (!Win32Mouse.Capture())
                Log.Warn("Could not find the game window to capture the mouse");
            _skipNextDelta = true;
            ShowToast("Mouse stick ON");
            Log.Info($"Activated. Trigger stick: {PathOf(_stick.transform)}; {_sticks.Count} stick(s) animated; " +
                     $"input manager: {(TargetInputManager != null ? PathOf(TargetInputManager.transform) : "NONE (falling back to stick events)")}");
        }

        private void Deactivate(bool silent = false)
        {
            if (!IsActive)
                return;
            IsActive = false;
            ClickMode = false;
            _holdActivated = false;
            // SOI mode outlives the stick: a G-toggled SOI mode carries on with the stick off (picked up again next
            // frame by UpdateSoiWithStickOff). Only this session's state ends here.
            if (SoiMode)
            {
                SoiMode = false;
                _soi.End();
            }

            SetTrigger(false);
            // Let go of the stick rather than leaving the last deflection held.
            ApplyStickVisual(Vector3.zero);
            foreach (var kv in _savedReturnToZero)
            {
                if (kv.Key != null)
                    kv.Key.returnToZeroWhenReleased = kv.Value;
            }
            _savedReturnToZero.Clear();
            _output = Vector3.zero;

            Win32Mouse.Release();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Log.Info($"Deactivated. Mouse travel this session: win32 {_win32Travel:0} px, unity axes {_unityTravel:0.0}");
            if (!silent)
                ShowToast("Mouse stick OFF");
        }

        private bool EnsureStick()
        {
            if (_stick != null && _stick.isActiveAndEnabled && !RemoteOnlyRef(_stick))
                return true;
            FindPlayerControls();
            return _stick != null;
        }

        private void FindPlayerControls()
        {
            _stick = null;
            _sticks.Clear();
            TargetInputManager = null;

            GameObject vehicle = GetPlayerVehicle();
            if (vehicle == null)
                return;

            foreach (var vim in vehicle.GetComponentsInChildren<VehicleInputManager>(false))
            {
                if (vim.isActiveAndEnabled)
                {
                    TargetInputManager = vim;
                    break;
                }
            }

            // Some aircraft (F/A-26B) have both a side and a centre stick active. BYOJ drives the side one when it
            // is active, so the trigger goes there too; otherwise use the local stick nearest the camera.
            Camera cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : vehicle.transform.position;
            VRJoystick nearest = null, side = null;
            float bestDist = float.MaxValue;
            foreach (var js in vehicle.GetComponentsInChildren<VRJoystick>(false))
            {
                if (!js.isActiveAndEnabled || RemoteOnlyRef(js))
                    continue;
                _sticks.Add(js);
                if (side == null && PathOf(js.transform).ToLowerInvariant().Contains("side"))
                    side = js;
                float d = (js.transform.position - eye).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    nearest = js;
                }
            }
            _stick = side ?? nearest;
        }

        private static string PathOf(Transform t)
        {
            string path = t.name;
            for (t = t.parent; t != null; t = t.parent)
                path = t.name + "/" + path;
            return path;
        }

        private static GameObject GetPlayerVehicle()
        {
            if (VTOLMPUtils.IsMultiplayer())
            {
                var info = VTOLMPLobbyManager.localPlayerInfo;
                if (info != null && info.vehicleObject != null)
                    return info.vehicleObject;
            }
            var fsm = FlightSceneManager.instance;
            return fsm != null && fsm.playerActor != null ? fsm.playerActor.gameObject : null;
        }

        // VRJoystick poses its model from its stick value in its own Update, and BYOJoystick overwrites that value
        // every Update too (with the physical stick, usually centred), so the model showed BYOJ's stick, not ours.
        // Posing it here in LateUpdate (after every Update, right before rendering) makes the model match.
        private static readonly MethodInfo SetStickAnimationMethod = AccessTools.Method(typeof(VRJoystick), "SetStickAnimation");
        private static bool _warnedStickAnimation;

        // Moves the cockpit stick models. The flight-control value goes in through VehicleInputPatch; stick events
        // are only sent here when there is no VehicleInputManager to patch (or to centre the stick on release).
        private void ApplyStickVisual(Vector3 pyr)
        {
            bool sendEvents = TargetInputManager == null || !IsActive;
            foreach (var js in _sticks)
            {
                if (js == null)
                    continue;
                js.RemoteSetStick(pyr);
                if (SetStickAnimationMethod != null)
                {
                    try
                    {
                        SetStickAnimationMethod.Invoke(js, null);
                    }
                    catch (Exception e)
                    {
                        if (!_warnedStickAnimation)
                        {
                            _warnedStickAnimation = true;
                            Log.Warn($"Posing stick model on {js.name} failed: {(e.InnerException ?? e).Message}");
                        }
                    }
                }
                else if (!_warnedStickAnimation)
                {
                    _warnedStickAnimation = true;
                    Log.Warn("VRJoystick.SetStickAnimation not found (game update?); cockpit stick model may not follow the mouse");
                }
                if (sendEvents && js.sendEvents)
                {
                    js.OnSetStick?.Invoke(pyr);
                    js.OnSetSteer?.Invoke(pyr.y);
                }
            }
        }

        private void SetTrigger(bool down)
        {
            if (down == _triggerHeld)
                return;
            _triggerHeld = down;
            if (_stick == null)
                return;
            if (down)
            {
                _stick.OnTriggerDown?.Invoke();
                _stick.OnTriggerAxis?.Invoke(1f);
            }
            else
            {
                _stick.OnTriggerAxis?.Invoke(0f);
                _stick.OnTriggerUp?.Invoke();
            }
        }

        private void ShowToast(string text)
        {
            _toast = text;
            _toastUntil = Time.unscaledTime + ToastSeconds;
        }

        // ---------------------------------------------------------------- overlay

        private void OnGUI()
        {
            // The window handles input events, so it runs for every event type; the overlay only paints.
            SettingsWindow.Draw();
            if (Event.current.type != EventType.Repaint)
                return;
            EnsureGuiResources();

            if (ClickMode)
                DrawClickModeOverlay(null);
            else if (SoiMode)
                DrawClickModeOverlay(_soi.Label);
            else if (IsActive || SettingsWindow.IsOpen)
                DrawStickOverlay();

            // Outline of the screen element under the cursor: the real hitbox ScreenPointer will press.
            if (S.showScreenHitbox && ScreenPointer.TryGetHoverOutline(_outline))
            {
                Color c = ScreenPointer.HoverIsTouchSurface ? new Color(0.4f, 0.8f, 1f, 0.35f) : new Color(0.4f, 0.9f, 1f, 0.9f);
                for (int i = 0; i < 4; i++)
                    DrawLine(_outline[i], _outline[(i + 1) % 4], 1.5f, c);
            }
            if (S.showScreenTooltip && ScreenPointer.HoverInfo != null)
                DrawTooltip(ScreenPointer.HoverInfo);

            if (_toast != null && Time.unscaledTime < _toastUntil)
            {
                float alpha = Mathf.Clamp01((_toastUntil - Time.unscaledTime) / 0.4f);
                DrawLabel(new Rect(0f, Screen.height * 0.12f, Screen.width, 24f), _toast, new Color(1f, 1f, 1f, alpha));
            }
        }

        // Clickable / TGP mode: no box or bars, just a small dot on a faint line from centre to the actual stick
        // position (plus an optional small label), so it stays out of the way. Opacity is its own setting.
        private void DrawClickModeOverlay(string label)
        {
            float a = S.clickModeOpacity;
            if (a <= 0f)
                return;
            float half = S.overlaySize * 0.5f;
            Vector2 c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            // Stick dot only while the mouse stick is on (SOI mode can run with it off).
            if (IsActive)
            {
                Vector2 p = new Vector2(c.x + _stickPos.x * half, c.y - _stickPos.y * half);
                Color color = SettingsWindow.IsOpen && _stickPos.magnitude <= S.deadzone ? new Color(1f, 0.85f, 0.3f, a) : new Color(0.55f, 1f, 0.6f, a);
                DrawLine(c, p, 1.5f, new Color(color.r, color.g, color.b, a * 0.5f));
                float r = 4f;
                GUI.color = color;
                GUI.DrawTexture(new Rect(p.x - r, p.y - r, r * 2f, r * 2f), _dot);
                GUI.color = Color.white;
            }

            if (label != null)
                DrawLabel(new Rect(c.x - 60f, c.y + 14f, 120f, 18f), label, new Color(1f, 1f, 1f, a));
        }

        private void DrawStickOverlay()
        {
            float size = S.overlaySize;
            float half = size * 0.5f;
            float a = S.overlayOpacity;
            Vector2 c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Rect box = new Rect(c.x - half, c.y - half, size, size);

            Color frame = _freeLook ? new Color(0.7f, 0.7f, 0.7f, a) : new Color(0.55f, 1f, 0.6f, a);
            Color faint = new Color(1f, 1f, 1f, 0.18f * a);

            Vector2 mousePos = IsActive ? _virtualPos : Vector2.zero;

            // Edges of the travel limit only appear as the mouse nears them: each side fades in from 0% at 60% of the
            // way out to 80% at full deflection. In the settings preview (stick off) all sides show, to judge the size.
            float Edge(float towards) => IsActive ? BorderAlpha * Mathf.Clamp01((towards - BorderStart) / (1f - BorderStart)) : BorderAlpha;
            if (S.circularLimit)
            {
                float ringA = Edge(mousePos.magnitude);
                if (ringA > 0.001f)
                    DrawRing(c, half, new Color(frame.r, frame.g, frame.b, ringA * a));
            }
            else
            {
                const float t = 2f;
                float right = Edge(mousePos.x), left = Edge(-mousePos.x), top = Edge(mousePos.y), bottom = Edge(-mousePos.y);
                if (right > 0.001f) Fill(new Rect(box.xMax - t, box.y, t, size), new Color(frame.r, frame.g, frame.b, right * a));
                if (left > 0.001f) Fill(new Rect(box.x, box.y, t, size), new Color(frame.r, frame.g, frame.b, left * a));
                if (top > 0.001f) Fill(new Rect(box.x, box.y, size, t), new Color(frame.r, frame.g, frame.b, top * a));
                if (bottom > 0.001f) Fill(new Rect(box.x, box.yMax - t, size, t), new Color(frame.r, frame.g, frame.b, bottom * a));
            }

            // Centre cross.
            Fill(new Rect(c.x - half, c.y - 0.5f, size, 1f), faint);
            Fill(new Rect(c.x - 0.5f, c.y - half, 1f, size), faint);

            // Deadzone: shaded disc with a ring around it, only while tuning it in the settings window (F8).
            float dz = S.deadzone * half;
            if (dz >= 1f && SettingsWindow.IsOpen)
            {
                GUI.color = new Color(1f, 0.85f, 0.3f, 0.16f * a);
                GUI.DrawTexture(new Rect(c.x - dz, c.y - dz, dz * 2f, dz * 2f), _dot);
                GUI.color = Color.white;
                DrawRing(c, dz, new Color(1f, 0.85f, 0.3f, 0.55f * a));
            }

            // Filled dot = mouse position only. Hollow ring = actual stick (mouse + WASD), shown while keyboard input
            // is non-zero. Screen up = mouse up. When only previewing from the settings window both sit at centre.
            Vector2 stickPos = IsActive ? _stickPos : Vector2.zero;
            bool showCombined = IsActive && _keyboard.sqrMagnitude > 1e-6f;
            Vector2 pm = new Vector2(c.x + mousePos.x * half, c.y - mousePos.y * half);
            Vector2 ps = new Vector2(c.x + stickPos.x * half, c.y - stickPos.y * half);

            // Colour reflects the actual stick output (deadzone yellow only while the settings window is open).
            bool inDeadzone = SettingsWindow.IsOpen && stickPos.magnitude <= S.deadzone;
            Color dotColor = _freeLook ? new Color(0.8f, 0.8f, 0.8f, 0.6f * a)
                : _triggerHeld ? new Color(1f, 0.3f, 0.25f, a)
                : inDeadzone ? new Color(1f, 0.85f, 0.3f, a)
                : new Color(0.55f, 1f, 0.6f, a);

            // Only the mouse dot gets a (faint) line from centre; the ring stands alone.
            DrawLine(c, pm, 1.5f, new Color(dotColor.r, dotColor.g, dotColor.b, dotColor.a * 0.35f));

            float r = 5f;
            GUI.color = showCombined ? new Color(dotColor.r, dotColor.g, dotColor.b, dotColor.a * 0.75f) : dotColor;
            GUI.DrawTexture(new Rect(pm.x - r, pm.y - r, r * 2f, r * 2f), _dot);
            GUI.color = Color.white;

            if (showCombined)
                DrawRing(ps, 7f, dotColor, 1.5f);

            // Title only in non-default states; plain flying shows no text. (SOI mode draws its own page label.)
            string title = !IsActive ? "MOUSE STICK  ·  PREVIEW"
                : SettingsWindow.IsOpen ? "MOUSE STICK  ·  SETTINGS OPEN"
                : _freeLook ? "MOUSE STICK  ·  FREE LOOK"
                : null;
            if (title != null)
                DrawLabel(new Rect(box.x - 100f, box.y - 24f, size + 200f, 20f), title, new Color(frame.r, frame.g, frame.b, 0.6f * a));
        }

        // Travel-limit edges: hidden until the mouse is this far out, then fading up to BorderAlpha at the edge.
        private const float BorderStart = 0.6f;
        private const float BorderAlpha = 0.8f;

        private void EnsureGuiResources()
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
            if (_dot == null)
            {
                const int n = 64;
                _dot = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
                float rad = n * 0.5f;
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(rad, rad));
                    _dot.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(rad - d)));
                }
                _dot.Apply();
            }
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                };
            }
        }

        private void Fill(Rect r, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(r, _white);
            GUI.color = Color.white;
        }

        private void Outline(Rect r, float t, Color color)
        {
            Fill(new Rect(r.x, r.y, r.width, t), color);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), color);
            Fill(new Rect(r.x, r.y + t, t, r.height - 2f * t), color);
            Fill(new Rect(r.xMax - t, r.y + t, t, r.height - 2f * t), color);
        }

        private void DrawLine(Vector2 from, Vector2 to, float width, Color color)
        {
            Vector2 d = to - from;
            float len = d.magnitude;
            if (len < 0.5f)
                return;
            Matrix4x4 saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, from);
            Fill(new Rect(from.x, from.y - width * 0.5f, len, width), color);
            GUI.matrix = saved;
        }

        private void DrawRing(Vector2 c, float radius, Color color, float width = 1.5f)
        {
            int segments = radius < 20f ? 24 : 48;
            Vector2 prev = c + new Vector2(radius, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float ang = i * Mathf.PI * 2f / segments;
                Vector2 next = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius;
                DrawLine(prev, next, width, color);
                prev = next;
            }
        }

        private GUIStyle _tooltipStyle;

        // Small dark box next to the cursor (kept on screen).
        private void DrawTooltip(string text)
        {
            if (_tooltipStyle == null)
                _tooltipStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = false, alignment = TextAnchor.UpperLeft, padding = new RectOffset(6, 6, 4, 4) };
            _tooltipStyle.normal.textColor = new Color(0.75f, 0.95f, 1f, 1f);
            Vector2 size = _tooltipStyle.CalcSize(new GUIContent(text));
            Vector2 m = Input.mousePosition;
            float x = Mathf.Min(m.x + 18f, Screen.width - size.x - 4f);
            float y = Mathf.Min(Screen.height - m.y + 18f, Screen.height - size.y - 4f);
            var r = new Rect(x, y, size.x, size.y);
            Fill(r, new Color(0f, 0f, 0f, 0.75f));
            GUI.Label(r, text, _tooltipStyle);
        }

        private void DrawLabel(Rect r, string text, Color color)
        {
            _labelStyle.normal.textColor = new Color(0f, 0f, 0f, color.a * 0.8f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, _labelStyle);
            _labelStyle.normal.textColor = color;
            GUI.Label(r, text, _labelStyle);
        }
    }
}
