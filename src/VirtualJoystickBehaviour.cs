using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using VTOLVR.Multiplayer;

namespace VirtualJoystick
{
    // The flight-control value itself is injected by VehicleInputPatch. This component reads the mouse,
    // draws the overlay, and moves the cockpit stick models to match (late, after BYOJ / VRJoystick Update).
    [DefaultExecutionOrder(10000)]
    public class VirtualJoystickBehaviour : MonoBehaviour
    {
        private const float ToastSeconds = 1.6f;

        private static readonly AccessTools.FieldRef<VRJoystick, bool> RemoteOnlyRef =
            AccessTools.FieldRefAccess<VRJoystick, bool>("remoteOnly");

        public static bool IsActive { get; private set; }
        // Virtual joystick on, but Left Alt held: cursor is free for cockpit clicks, keyboard still flies.
        public static bool ClickMode { get; private set; }
        // FlatScreen 3 hover/click is suppressed while the mouse is flying the aircraft or driving an SOI page.
        public static bool SuppressCockpitHover => (IsActive && !ClickMode) || SoiMode;

        // Stick off, free look in a head mode (or a head-mode press still held): LMB isn't a cockpit click.
        public static bool HeadModeOwnsLmb =>
            !IsActive && (Cockpit.SoiKeys.HeadLmbActive || (Input.GetMouseButton(1) && Cockpit.SoiKeys.InHeadMode));
                // SOI cursor mode: the mouse is the cursor of the SOI MFD page (TGP aiming, radar/ARAD/map cursor), scroll zooms it.
        public static bool SoiMode { get; private set; }

        private readonly SoiCursor _soi = new SoiCursor();
        private static VirtualJoystickBehaviour _instance;

        public static void ShowToastStatic(string text) => _instance?.ShowToast(text);
        // Pitch, yaw, roll in VRJoystick convention; read by VehicleInputPatch.
        public static Vector3 Output { get; private set; }
        public static VehicleInputManager TargetInputManager { get; private set; }
        // Virtual joystick off, but WASD / rudder keys are flying the aircraft (Output holds the keyboard-only value).
        public static bool KeyboardFlying { get; private set; }

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
        private bool _soiToggled;      // SOI mode latched on by the toggle key (T)
        private bool _holdActivated;   // stick control was switched on by holding the hold key (Left Alt)
        private KeyCode[] _releaseKeys = new KeyCode[0];

        private string _toast;
        private float _toastUntil;

        private readonly Vector2[] _outline = new Vector2[4];

        private Texture2D _white;
        private Texture2D _dot;
        private GUIStyle _labelStyle;

        private static VirtualJoystickSettings S => VirtualJoystickSettings.Current;

        private void Awake()
        {
            _instance = this;
            SceneManager.activeSceneChanged += OnSceneChanged;
            if (S.openWindowOnStart)
                SettingsWindow.Open();
            ParseKeys();
        }

        private void ParseKeys()
        {
            _rudderLeft = VirtualJoystickSettings.ParseKey(S.rudderLeftKey);
            _rudderRight = VirtualJoystickSettings.ParseKey(S.rudderRightKey);
            _pitchDown = VirtualJoystickSettings.ParseKey(S.pitchDownKey);
            _pitchUp = VirtualJoystickSettings.ParseKey(S.pitchUpKey);
            _rollLeft = VirtualJoystickSettings.ParseKey(S.rollLeftKey);
            _rollRight = VirtualJoystickSettings.ParseKey(S.rollRightKey);
            _menuKey = VirtualJoystickSettings.ParseKey(S.menuKey);
            _clickKey = VirtualJoystickSettings.ParseKey(S.clickModeKey);
            _tgpKey = VirtualJoystickSettings.ParseKey(S.tgpModeKey);
            _toggleKey = VirtualJoystickSettings.ParseKey(S.toggleKey);
            _toggleKey2 = VirtualJoystickSettings.ParseKey(S.toggleKey2);
            _soiHoldKey = VirtualJoystickSettings.ParseKey(S.soiHoldKey);
            _releaseKeys = new KeyCode[S.releaseKeys.Length];
            for (int i = 0; i < _releaseKeys.Length; i++)
                _releaseKeys[i] = VirtualJoystickSettings.ParseKey(S.releaseKeys[i]);
        }

        private static bool IsHeld(KeyCode k) => k != KeyCode.None && Input.GetKey(k);
        private static bool IsDown(KeyCode k) => k != KeyCode.None && Input.GetKeyDown(k);

        private static float KeyAxis(KeyCode positive, KeyCode negative) =>
            (positive != KeyCode.None && Input.GetKey(positive) ? 1f : 0f) - (negative != KeyCode.None && Input.GetKey(negative) ? 1f : 0f);

        private void OnApplicationQuit() => VirtualJoystickSettings.SaveIfChanged();

        private void OnDestroy()
        {
            VirtualJoystickSettings.SaveIfChanged();
            SceneManager.activeSceneChanged -= OnSceneChanged;
            Deactivate(silent: true);
            if (_white != null) Destroy(_white);
            if (_hmcsTextMaterial != null) Destroy(_hmcsTextMaterial);
            if (_dot != null) Destroy(_dot);
        }

        private void OnSceneChanged(Scene from, Scene to)
        {
            Deactivate(silent: true);
            KeyboardFlying = false;
            _keyboard = Vector2.zero;
            _yaw = 0f;
            if (SoiMode)
                EndSoiWithStickOff();
            _soiToggled = false;
            SettingsWindow.Close();
            ScreenPointer.Reset();
            Cockpit.KeyActions.Reset();
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

        // Middle mouse held for middleHoldSeconds recentres the view (once per hold), in every mode: stick on or off, SOI cursor,
        // clickable, free look, settings window. A short press keeps its normal job (stick / SOI page re-centre), but
        // while the hold is enabled that job fires on release, so a hold doesn't also snap the stick or unlock the radar.
        private float _middleDownAt = -1f;
        private bool _middleFired;
        // This frame's short middle press: release of a press that didn't reach the hold (or press-down with the hold off).
        private bool _middleShortPress;

        private void HandleViewRecenter()
        {
            _middleShortPress = false;
            if (!S.middleHoldRecentersView)
            {
                _middleDownAt = -1f;
                _middleFired = false;
                _middleShortPress = Input.GetMouseButtonDown(2);
                return;
            }

            bool held = Input.GetMouseButton(2) || (Application.isFocused && Win32Mouse.MiddleHeld);
            if (!held)
            {
                _middleShortPress = _middleDownAt >= 0f && !_middleFired;
                _middleDownAt = -1f;
                _middleFired = false;
                return;
            }
            // Timed from the first frame seen held, so a press that began while this wasn't running still counts.
            if (_middleDownAt < 0f)
                _middleDownAt = Time.unscaledTime;
            if (!_middleFired && Time.unscaledTime - _middleDownAt >= Mathf.Clamp(S.middleHoldSeconds, 0.2f, 2f))
            {
                _middleFired = true; // once per hold
                ShowToast(FlatScreenCompat.RecenterView() ? "View recentred" : "View recentre failed (see log)");
            }
        }

        // Settings only change through the settings window: while it's open, save changes about once a second (closing
        // it saves too), so a crash loses at most a second of tweaking.
        private const float SettingsSaveInterval = 1f;
        private float _nextSettingsSave;

        private void Update()
        {
            if (SettingsWindow.IsOpen && Time.unscaledTime >= _nextSettingsSave)
            {
                _nextSettingsSave = Time.unscaledTime + SettingsSaveInterval;
                VirtualJoystickSettings.SaveIfChanged();
            }

            HandleViewRecenter();

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

            // Keyboard bindings (Bindings tab): controller buttons and aircraft controls, with the stick on or off.
            Cockpit.KeyActions.Update(GetPlayerVehicle(), inputAllowed: !SettingsWindow.IsCapturingKey);

            // Stick off, outside SOI mode, in FlatScreen 3's free look (RMB held): LMB is still the head-mode action
            // (TGP HEAD lock / radar head button). With the free cursor (no RMB) LMB stays a cockpit click.
            if (!IsActive && !SoiMode)
                Cockpit.SoiKeys.HeadModeLmb(!SettingsWindow.IsOpen && Input.GetMouseButton(1) && Input.GetMouseButton(0));

            // Cockpit screens (MFD buttons, touchscreens) are clicked through ScreenPointer whenever the free cursor is in
            // use: stick off, or clickable mode. The FlatScreen 3 hook that hands them over is installed up front.
            if (ScreenPointer.Available)
            {
                FlatScreenCompat.TryPatch();
                bool freeCursor = !SuppressCockpitHover && !SettingsWindow.CursorOverWindow && !SoiMode && !HeadModeOwnsLmb;
                ScreenPointer.Update(S.handleScreens, freeCursor, GetPlayerVehicle());
            }

            if (!IsActive)
            {
                UpdateSoiWithStickOff();
                UpdateKeyboardWithStickOff();
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
            // window / cockpit can be used, and freeze the virtual joystick. WASD and rudder keep flying the aircraft.
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

            if (GetPlayerVehicle() == null)
            {
                Deactivate(silent: true);
                ShowToast("Virtual joystick OFF (aircraft lost)");
                return;
            }
            if (!EnsureStick())
            {
                Deactivate();
                ShowToast("Virtual joystick OFF (no stick)");
                return;
            }

            // SOI mode: the mouse is the cursor of the SOI page instead of flying. Cursor stays captured, WASD keeps
            // flying. T toggles it; Mouse button 4 flips it while held. Clickable mode and the menu take priority.
            if (!menuOpen && !ClickMode && IsDown(_tgpKey))
                _soiToggled = !_soiToggled;
            bool wasSoiMode = SoiMode;
            SoiMode = !menuOpen && !ClickMode && (_soiToggled ^ IsHeld(_soiHoldKey));
            if (SoiMode && !wasSoiMode)
            {
                _soi.Begin(GetPlayerVehicle());
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
            // TGP HEAD mode / radar head boresight: LMB is the page's head action (TGP lock / radar BORE).
            if (Cockpit.SoiKeys.HeadModeLmb(lmb))
                lmb = false;

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
                if (_middleShortPress)
                    _soi.Recenter();
            }

            if (S.autoCenterRate > 0f && delta == Vector2.zero)
                _virtualPos = Vector2.MoveTowards(_virtualPos, Vector2.zero, S.autoCenterRate * dt);

            if (S.middleMouseRecenters && !ClickMode && !SoiMode && _middleShortPress)
                _virtualPos = Vector2.zero;

            if (S.circularLimit)
                _virtualPos = Vector2.ClampMagnitude(_virtualPos, 1f);
            else
                _virtualPos = new Vector2(Mathf.Clamp(_virtualPos.x, -1f, 1f), Mathf.Clamp(_virtualPos.y, -1f, 1f));

            UpdateKeyboardAxes(dt);
            ApplyStickPos(CombineMouseAndKeyboard(_virtualPos, _keyboard));

            // Normal flight: LMB is the SOI thumbstick press (radar lock etc.). In SOI mode the page handles it (SoiCursor).
            Cockpit.SoiKeys.LmbSelect(!SoiMode && lmb);
        }

        // Rudder keys -> _yaw; WASD -> _keyboard (in mouse space).
        private void UpdateKeyboardAxes(float dt)
        {
            _yaw = Mathf.MoveTowards(_yaw, KeyAxis(_rudderRight, _rudderLeft), S.rudderRate * dt);

            // WASD springs back to centre on release. W is always stick forward (nose down), whatever invertPitch
            // does to the mouse, so it is converted into mouse space here and back out by the same sign in ApplyStickPos.
            float inv = S.invertPitch ? -1f : 1f;
            Vector2 kbTarget = new Vector2(KeyAxis(_rollRight, _rollLeft), KeyAxis(_pitchDown, _pitchUp) * inv);
            // Per axis (x = roll, A/D; y = pitch, W/S): a held key ramps at that axis' speed; with no key, the axis drifts
            // back to centre at its own return rate, or stays put if that axis' return is off.
            if (kbTarget.x == 0f && !S.keyboardReturnRoll) kbTarget.x = _keyboard.x;
            if (kbTarget.y == 0f && !S.keyboardReturnPitch) kbTarget.y = _keyboard.y;
            _keyboard.x = Mathf.MoveTowards(_keyboard.x, kbTarget.x, (kbTarget.x != 0f ? S.keyboardRateRoll : S.keyboardReturnRateRoll) * dt);
            _keyboard.y = Mathf.MoveTowards(_keyboard.y, kbTarget.y, (kbTarget.y != 0f ? S.keyboardRatePitch : S.keyboardReturnRatePitch) * dt);
        }

        // Mouse + WASD. Per axis, the keyboard moves the stick from the mouse position toward the key's end stop, so full
        // key input is always full deflection that way, even with the mouse held at the opposite edge (free look,
        // clickable mode and SOI mode freeze the mouse). With the mouse centred this is the same as plain addition.
        private Vector2 CombineMouseAndKeyboard(Vector2 mouse, Vector2 kb)
        {
            Vector2 p = new Vector2(
                Mathf.Lerp(mouse.x, Mathf.Sign(kb.x), Mathf.Abs(kb.x)),
                Mathf.Lerp(mouse.y, Mathf.Sign(kb.y), Mathf.Abs(kb.y)));
            if (!S.circularLimit || p.sqrMagnitude <= 1f)
                return p;

            // Circular limit: the axis the keyboard drives harder keeps its deflection and the other gives way, so e.g.
            // full W still reaches full pitch with the mouse parked at full roll. Ties fall to ClampMagnitude.
            float ax = Mathf.Abs(kb.x), ay = Mathf.Abs(kb.y);
            if (ax > ay)
                p.y = Mathf.Sign(p.y) * Mathf.Min(Mathf.Abs(p.y), Mathf.Sqrt(Mathf.Max(0f, 1f - p.x * p.x)));
            else if (ay > ax)
                p.x = Mathf.Sign(p.x) * Mathf.Min(Mathf.Abs(p.x), Mathf.Sqrt(Mathf.Max(0f, 1f - p.y * p.y)));
            return p;
        }

        // Clamps the combined stick position, shapes it and stores the flight-control output.
        private void ApplyStickPos(Vector2 pos)
        {
            _stickPos = S.circularLimit
                ? Vector2.ClampMagnitude(pos, 1f)
                : new Vector2(Mathf.Clamp(pos.x, -1f, 1f), Mathf.Clamp(pos.y, -1f, 1f));

            // VRJoystick axes: x = pitch (+ = stick forward / nose down), y = yaw (+ = right), z = roll (+ = left).
            float inv = S.invertPitch ? -1f : 1f;
            Vector2 shaped = Shape(_stickPos);
            _output = new Vector3(shaped.y * inv, _yaw, -shaped.x);
        }

        // WASD and rudder keys with the virtual joystick off. The flight controls are only overridden while a key is
        // held or an axis is still springing back, so a real joystick (BYOJ) keeps flying the rest of the time.
        private void UpdateKeyboardWithStickOff()
        {
            if (GetPlayerVehicle() == null)
            {
                EndKeyboardFlying();
                return;
            }

            UpdateKeyboardAxes(Time.unscaledDeltaTime);
            bool inUse = _keyboard.sqrMagnitude > 1e-8f || Mathf.Abs(_yaw) > 1e-4f;
            if (!inUse)
            {
                EndKeyboardFlying();
                return;
            }
            if (!KeyboardFlying)
            {
                // Find this aircraft's input manager / sticks (the cached ones may be from an earlier session).
                FindPlayerControls();
                if (TargetInputManager == null && _sticks.Count == 0)
                    return;
                KeyboardFlying = true;
            }
            ApplyStickPos(_keyboard);
        }

        private void EndKeyboardFlying()
        {
            if (!KeyboardFlying)
                return;
            KeyboardFlying = false;
            _keyboard = Vector2.zero;
            _yaw = 0f;
            _output = Vector3.zero;
            // Let go of the stick rather than leaving the last deflection held.
            ApplyStickVisual(Vector3.zero);
        }

        // SOI cursor mode while the virtual joystick is off: same keys and behaviour as with the stick on (T toggles,
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
                FindPlayerControls();
                _soi.Begin(vehicle);
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
            if (Cockpit.SoiKeys.HeadModeLmb(lmb))
                lmb = false;
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
            if (_middleShortPress)
                _soi.Recenter();

        }

        private void EndSoiWithStickOff()
        {
            SoiMode = false;
            _soi.End();
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
            ScreenPointer.LateUpdate();
            if (!IsActive && KeyboardFlying)
                ApplyStickVisual(_output);
            if (!IsActive && SoiMode)
            {
                // SOI mode with the stick off: keep the captured cursor hidden (FlatScreen 3 re-shows it on movement).
                if (!_freeLook && !SettingsWindow.IsOpen)
                    Cursor.visible = false;
                return;
            }
            if (!IsActive || _stick == null)
                return;

            // FlatScreen 3 re-shows the cursor in its LateUpdate whenever the mouse moves; we run after it.
            if (!_freeLook && !SettingsWindow.IsOpen && !ClickMode)
                Cursor.visible = false;

            ApplyStickVisual(_output);
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
            ParseKeys();

            _stick = null;
            if (!EnsureStick())
            {
                ShowToast("Virtual joystick: no flyable stick in this seat");
                return;
            }

            FlatScreenCompat.TryPatch();

            if (S.recenterOnEnable)
            {
                _virtualPos = Vector2.zero;
                _yaw = 0f;
            }
            // Keyboard input already flying (stick was off) carries straight over instead of snapping to centre.
            if (!KeyboardFlying)
                _keyboard = Vector2.zero;
            KeyboardFlying = false;
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
            ShowToast("Virtual joystick ON");
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

            Cockpit.SoiKeys.LmbSelect(false);
            // Let go of the stick rather than leaving the last deflection held.
            ApplyStickVisual(Vector3.zero);
            foreach (var kv in _savedReturnToZero)
            {
                if (kv.Key != null)
                    kv.Key.returnToZeroWhenReleased = kv.Value;
            }
            _savedReturnToZero.Clear();
            _output = Vector3.zero;
            // Keys still held pick up from centre with the stick off (UpdateKeyboardWithStickOff).
            _keyboard = Vector2.zero;
            _yaw = 0f;

            Win32Mouse.Release();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Log.Info($"Deactivated. Mouse travel this session: win32 {_win32Travel:0} px, unity axes {_unityTravel:0.0}");
            if (!silent)
                ShowToast("Virtual joystick OFF");
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

        // The aircraft the player is flying, or null when there is none to fly: not spawned, destroyed, or ejected from.
        // A wreck counting as "no aircraft" switches the stick off on death and lets auto-enable fire again on respawn.
        private static GameObject GetPlayerVehicle()
        {
            GameObject vehicle = null;
            var fsm = FlightSceneManager.instance;
            if (VTOLMPUtils.IsMultiplayer())
            {
                var info = VTOLMPLobbyManager.localPlayerInfo;
                if (info != null && info.vehicleObject != null)
                    vehicle = info.vehicleObject;
            }
            if (vehicle == null && fsm != null && fsm.playerActor != null)
                vehicle = fsm.playerActor.gameObject;
            if (vehicle == null)
                return null;

            Actor actor = vehicle.GetComponent<Actor>();
            if (actor != null && !actor.alive)
                return null;

            // playerHasEjected stays set for the rest of the scene (even across a multiplayer respawn), so remember
            // which aircraft it was set in and only write that one off.
            if (fsm != _ejectFsm)
            {
                _ejectFsm = fsm;
                _ejectLatched = false;
                _ejectedFrom = null;
            }
            if (fsm != null && fsm.playerHasEjected && !_ejectLatched)
            {
                _ejectLatched = true;
                _ejectedFrom = vehicle;
            }
            return _ejectLatched && vehicle == _ejectedFrom ? null : vehicle;
        }

        private static FlightSceneManager _ejectFsm;
        private static bool _ejectLatched;
        private static GameObject _ejectedFrom;

        // VRJoystick poses its model from its stick value in its own Update, and BYOJoystick overwrites that value
        // every Update too (with the physical stick, usually centred), so the model showed BYOJ's stick, not ours.
        // Posing it here in LateUpdate (after every Update, right before rendering) makes the model match.
        private static readonly MethodInfo SetStickAnimationMethod = AccessTools.Method(typeof(VRJoystick), "SetStickAnimation");
        private static bool _warnedStickAnimation;

        // Moves the cockpit stick models. The flight-control value goes in through VehicleInputPatch; stick events
        // are only sent here when there is no VehicleInputManager to patch (or to centre the stick on release).
        private void ApplyStickVisual(Vector3 pyr)
        {
            bool sendEvents = TargetInputManager == null || !(IsActive || KeyboardFlying);
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
                DrawClickModeOverlay();
            else if (IsActive || SettingsWindow.IsOpen)
                DrawStickOverlay();
            else if (SoiMode)
                // SOI mode with the stick off: no stick to show, just the title where the stick overlay puts it.
                DrawOverlayTitle(SoiTitle);

            // Bindings tab: ring the cockpit controls whose cards are open, each joined by a line to the window
            // edge beside its card (the window draws on top, so the line stops at its edge), so you can see which
            // switch is which.
            if (SettingsWindow.IsOpen)
            {
                Color mark = Theme.Menu.Text;
                Rect win = SettingsWindow.WindowRect;
                _ringsDrawn.Clear();
                foreach (var h in SettingsWindow.Highlights)
                {
                    Vector3? world = h.Locate();
                    if (!world.HasValue || !ScreenPointer.WorldToGui(world.Value, out Vector2 g))
                        continue;
                    // Cards on the same control share one ring.
                    bool dup = false;
                    foreach (var other in _ringsDrawn)
                        dup |= (other - g).sqrMagnitude < 1f;
                    _ringsDrawn.Add(g);
                    if (!dup)
                        DrawThickRing(g, HighlightRadius, 6f, mark);
                    if (win.Contains(g))
                        continue;
                    var end = new Vector2(g.x < win.center.x ? win.xMin : win.xMax, h.CardY);
                    Vector2 dir = (end - g).normalized;
                    DrawLine(g + dir * HighlightRadius, end, 2f, mark);
                }
            }

            // Outline of the screen element under the cursor: the real hitbox ScreenPointer will press. Always shown:
            // it's the only sign of what's hovered. Drawn like the MFD's own hover: a hard frame inside the
            // element. Layout preset held (tap loads, a long hold saves): the element fills left to right faintly,
            // full = saved. Whole touch surfaces get a fainter frame.
            float hold = ScreenPointer.PresetHoldProgress;
            if (ScreenPointer.TryGetHoverOutline(_outline))
            {
                Color green = ScreenPointer.HoverColor;
                Color c = ScreenPointer.HoverIsTouchSurface ? new Color(green.r, green.g, green.b, 0.4f) : green;
                if (hold > 0f)
                {
                    Vector2 bottom = Vector2.Lerp(_outline[0], _outline[1], hold);
                    Vector2 top = Vector2.Lerp(_outline[3], _outline[2], hold);
                    FillQuad(_outline[0], bottom, top, _outline[3], new Color(green.r, green.g, green.b, 0.3f));
                }
                FrameQuad(_outline, c);
            }
            if (S.showScreenTooltip && ScreenPointer.HoverInfo != null)
                DrawTooltip(ScreenPointer.HoverInfo);

            if (_toast != null && Time.unscaledTime < _toastUntil)
            {
                // Same style as the overlay title, on the line above it; fades out over its last 0.4 s.
                float alpha = Mathf.Clamp01((_toastUntil - Time.unscaledTime) / 0.4f);
                DrawLabel(TitleLine(1), _toast.ToUpperInvariant(), alpha);
            }
        }

        // Clickable mode: no box or bars, just a small dot on a faint line from centre to the actual stick
        // position, so it stays out of the way. Opacity is its own setting.
        private void DrawClickModeOverlay()
        {
            float a = S.clickModeOpacity;
            if (a <= 0f)
                return;
            float half = S.overlaySize * 0.5f;
            Vector2 c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            Vector2 p = new Vector2(c.x + _stickPos.x * half, c.y - _stickPos.y * half);
            bool inDz = _stickPos.magnitude <= S.deadzone;
            Color color = SettingsWindow.IsOpen && inDz ? new Color(1f, 0.85f, 0.3f, a) : new Color(0.55f, 1f, 0.6f, a);
            if (inDz)
                color.a *= 0.5f; // 50% more transparent inside the deadzone
            DrawLine(c, p, 1.5f, new Color(color.r, color.g, color.b, a * 0.5f));
            float r = 4f;
            GUI.color = color;
            GUI.DrawTexture(new Rect(p.x - r, p.y - r, r * 2f, r * 2f), _dot);
            GUI.color = Color.white;
        }

        // Free look and SOI cursor mode: the mouse isn't flying, so the stick overlay goes grey and fainter.
        private bool OverlayPassive => _freeLook || SoiMode;
        private static Color PassiveColor(float a) => new Color(0.7f, 0.7f, 0.7f, a);
        // "* TGP", "* RADAR" ...: the asterisk marks the SOI page, as the MFDs do. None found: just "NO SOI".
        private string SoiTitle => _soi.Current == SoiCursor.Kind.None ? _soi.Label : "* " + _soi.Label;

        // Text line centred above the control area: 0 = overlay title, 1 = the line above it (toasts). One line is
        // a label box's height plus a small gap.
        private Rect TitleLine(int line)
        {
            float size = S.overlaySize;
            float top = Screen.height * 0.5f - size * 0.5f;
            return new Rect(Screen.width * 0.5f - size * 0.5f - 100f, top - 28f - line * 26f, size + 200f, 22f);
        }

        private void DrawOverlayTitle(string title) => DrawLabel(TitleLine(0), title, 1f);

        private void DrawStickOverlay()
        {
            float size = S.overlaySize;
            float half = size * 0.5f;
            float a = S.overlayOpacity;
            Vector2 c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Rect box = new Rect(c.x - half, c.y - half, size, size);

            bool passive = OverlayPassive;
            Color frame = passive ? PassiveColor(a) : new Color(0.55f, 1f, 0.6f, a);
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
            Color dotColor = passive ? new Color(0.8f, 0.8f, 0.8f, 0.6f * a)
                : Cockpit.KeyActions.Right.TriggerHeld ? new Color(1f, 0.3f, 0.25f, a)
                : inDeadzone ? new Color(1f, 0.85f, 0.3f, a)
                : new Color(0.55f, 1f, 0.6f, a);

            // Only the mouse dot gets a (faint) line from centre; the ring stands alone.
            DrawLine(c, pm, 1.5f, new Color(dotColor.r, dotColor.g, dotColor.b, dotColor.a * 0.35f));

            float r = S.stickDotSize;
            // Inside the deadzone (no output) the dot is drawn 50% more transparent.
            float dotAlpha = dotColor.a * (showCombined ? 0.75f : 1f) * (stickPos.magnitude <= S.deadzone ? 0.5f : 1f);
            GUI.color = new Color(dotColor.r, dotColor.g, dotColor.b, dotAlpha);
            GUI.DrawTexture(new Rect(pm.x - r, pm.y - r, r * 2f, r * 2f), _dot);
            GUI.color = Color.white;

            if (showCombined)
                DrawRing(ps, S.stickDotSize + 2f, dotColor, 1.5f);

            // Title only in non-default states; plain flying shows no text.
            string title = !IsActive ? "PREVIEW"
                : SettingsWindow.IsOpen ? "SETTINGS OPEN"
                : SoiMode ? SoiTitle
                : _freeLook ? "FREE LOOK"
                : null;
            if (title != null)
                DrawOverlayTitle(title);
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
            // The HUD's font once it's loaded (the default bold font until then); the MFD buttons use the same face.
            if (_labelStyle.font != Theme.HudFont && Theme.HudFont != null)
            {
                _labelStyle.font = Theme.HudFont;
                _labelStyle.fontStyle = FontStyle.Normal;
                _labelStyle.fontSize = 14;
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

        private static Material _quadMaterial;

        // Solid quad between four GUI points (y down), any shape: the screen outline is a perspective quad.
        private void FillQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            if (_quadMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null)
                    return;
                _quadMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                _quadMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _quadMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _quadMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                _quadMaterial.SetInt("_ZWrite", 0);
                _quadMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            }
            float h = Screen.height;
            GL.PushMatrix();
            GL.LoadPixelMatrix();
            _quadMaterial.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(color);
            GL.Vertex3(a.x, h - a.y, 0f);
            GL.Vertex3(b.x, h - b.y, 0f);
            GL.Vertex3(c.x, h - c.y, 0f);
            GL.Vertex3(d.x, h - d.y, 0f);
            GL.End();
            GL.PopMatrix();
        }

        // Hard frame on the inside of a quad (GUI points, y down, in order around it), like the MFD's 9-sliced hover
        // sprite: each edge moved inwards by the thickness, mitred corners, no gaps or overlaps. Thickness is 1/26 of
        // the shorter side (the MFD frame on its button), kept between 1.5 and 3 px.
        private void FrameQuad(Vector2[] q, Color color)
        {
            float shortest = float.MaxValue;
            Vector2 centre = Vector2.zero;
            for (int i = 0; i < 4; i++)
            {
                shortest = Mathf.Min(shortest, (q[(i + 1) % 4] - q[i]).magnitude);
                centre += q[i] * 0.25f;
            }
            if (shortest < 1f)
                return;
            float t = Mathf.Min(Mathf.Clamp(shortest / 26f, 1.5f, 3f), shortest * 0.25f);

            // Edge i runs q[i] -> q[i+1], moved towards the centre; inner corner i is where edges i-1 and i meet.
            for (int i = 0; i < 4; i++)
            {
                Vector2 a = q[i], d = q[(i + 1) % 4] - a;
                Vector2 n = new Vector2(-d.y, d.x).normalized;
                if (Vector2.Dot(centre - a, n) < 0f)
                    n = -n;
                _edgeFrom[i] = a + n * t;
                _edgeDir[i] = d;
            }
            for (int i = 0; i < 4; i++)
            {
                int p = (i + 3) % 4;
                float den = _edgeDir[p].x * _edgeDir[i].y - _edgeDir[p].y * _edgeDir[i].x;
                if (Mathf.Abs(den) < 1e-4f)
                {
                    _inner[i] = _edgeFrom[i];
                    continue;
                }
                Vector2 diff = _edgeFrom[i] - _edgeFrom[p];
                _inner[i] = _edgeFrom[p] + _edgeDir[p] * ((diff.x * _edgeDir[i].y - diff.y * _edgeDir[i].x) / den);
            }
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                FillQuad(q[i], q[j], _inner[j], _inner[i], color);
            }
        }

        private readonly Vector2[] _edgeFrom = new Vector2[4], _edgeDir = new Vector2[4], _inner = new Vector2[4];

        private const float HighlightRadius = 16f;
        private readonly System.Collections.Generic.List<Vector2> _ringsDrawn = new System.Collections.Generic.List<Vector2>();

        // Solid annulus of the given width centred on radius, one quad per segment so there are no joints.
        private void DrawThickRing(Vector2 c, float radius, float width, Color color)
        {
            const int segments = 48;
            float rIn = radius - width * 0.5f, rOut = radius + width * 0.5f;
            Vector2 dirPrev = new Vector2(1f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float ang = i * Mathf.PI * 2f / segments;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                FillQuad(c + dirPrev * rIn, c + dirPrev * rOut, c + dir * rOut, c + dir * rIn, color);
                dirPrev = dir;
            }
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
            // The HUD font once it's loaded, like the overlay labels.
            if (_tooltipStyle.font != Theme.HudFont && Theme.HudFont != null)
                _tooltipStyle.font = Theme.HudFont;
            _tooltipStyle.normal.textColor = new Color(0.75f, 0.95f, 1f, 1f);
            Vector2 size = _tooltipStyle.CalcSize(new GUIContent(text));
            Vector2 m = Input.mousePosition;
            float x = Mathf.Min(m.x + 18f, Screen.width - size.x - 4f);
            float y = Mathf.Min(Screen.height - m.y + 18f, Screen.height - size.y - 4f);
            var r = new Rect(x, y, size.x, size.y);
            Fill(r, new Color(0f, 0f, 0f, 0.75f));
            GUI.Label(r, text, _tooltipStyle);
        }

        // Like the HUD / HMCS text: HUD green, no box, through the HMCS's own additive shader (it brightens what's
        // behind it rather than covering it). Alpha fades it.
        private void DrawLabel(Rect line, string text, float alpha)
        {
            Color c = Theme.HudGreen;
            c.a = Theme.OverlayTextOpacity * alpha;
            if (DrawHmcsText(line, text, c))
                return;
            // HMCS shader not found: plain alpha-blended text in the same colour.
            _labelStyle.normal.textColor = c;
            GUI.Label(line, text, _labelStyle);
        }

        private static Material _hmcsTextMaterial;
        private static float _nextHmcsShaderLookup;

        // The game's UI/DefaultOverlay2 shader, found by name or, if Shader.Find can't see it, among the loaded ones.
        // Looked up again every few seconds until it turns up.
        private static Material HmcsTextMaterial
        {
            get
            {
                if (_hmcsTextMaterial != null || Time.unscaledTime < _nextHmcsShaderLookup)
                    return _hmcsTextMaterial;
                _nextHmcsShaderLookup = Time.unscaledTime + 2f;
                Shader shader = Shader.Find(Theme.HmcsShaderName);
                if (shader == null)
                    foreach (var sh in Resources.FindObjectsOfTypeAll<Shader>())
                        if (sh != null && sh.name == Theme.HmcsShaderName) { shader = sh; break; }
                if (shader != null)
                {
                    _hmcsTextMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    // The font atlas is alpha-only; like a UI Text, add white to its colour so the glyphs take the
                    // vertex colour.
                    _hmcsTextMaterial.SetVector("_TextureSampleAdd", new Vector4(1f, 1f, 1f, 0f));
                }
                return _hmcsTextMaterial;
            }
        }

        // Glyph quads in immediate mode, straight onto the screen, centred in the line. False if the shader isn't
        // available.
        private bool DrawHmcsText(Rect line, string text, Color color)
        {
            Material mat = HmcsTextMaterial;
            if (mat == null)
                return false;
            if (Event.current.type != EventType.Repaint)
                return true;

            Font font = _labelStyle.font != null ? _labelStyle.font : GUI.skin.font;
            int size = _labelStyle.fontSize;
            FontStyle style = _labelStyle.fontStyle;
            font.RequestCharactersInTexture(text + "H", size, style);

            float width = 0f;
            foreach (char ch in text)
                if (font.GetCharacterInfo(ch, out CharacterInfo ci, size, style))
                    width += ci.advance;
            font.GetCharacterInfo('H', out CharacterInfo cap, size, style);
            float pen = Mathf.Round(line.center.x - width * 0.5f);
            float baseline = Mathf.Round(line.center.y + cap.maxY * 0.5f);

            mat.mainTexture = font.material.mainTexture;
            // The shader always clips to _ClipRect (in vertex position units, here pixels), even without RectMask2D.
            mat.SetVector("_ClipRect", new Vector4(-1e6f, -1e6f, 1e6f, 1e6f));
            // Its alpha is also scaled by the global _HUDBrightness (the cockpit HUD brightness knob, 0.1 to 1). The
            // overlay text stays at full brightness whatever the knob says: the material's own value overrides it.
            mat.SetFloat("_HUDBrightness", 1f);
            // Passed as is, not converted to linear: a UI Text's vertex colour reaches the shader unconverted too.
            Color vc = color;

            float h = Screen.height;
            GL.PushMatrix();
            GL.LoadPixelMatrix();
            mat.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(vc);
            foreach (char ch in text)
            {
                if (!font.GetCharacterInfo(ch, out CharacterInfo ci, size, style))
                    continue;
                float x0 = pen + ci.minX, x1 = pen + ci.maxX;
                float top = h - (baseline - ci.maxY), bottom = h - (baseline - ci.minY); // GUI y down -> pixel y up
                GL.TexCoord(ci.uvTopLeft); GL.Vertex3(x0, top, 0f);
                GL.TexCoord(ci.uvTopRight); GL.Vertex3(x1, top, 0f);
                GL.TexCoord(ci.uvBottomRight); GL.Vertex3(x1, bottom, 0f);
                GL.TexCoord(ci.uvBottomLeft); GL.Vertex3(x0, bottom, 0f);
                pen += ci.advance;
            }
            GL.End();
            GL.PopMatrix();
            return true;
        }
    }
}
