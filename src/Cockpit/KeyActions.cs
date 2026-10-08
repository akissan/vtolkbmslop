using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;

namespace VirtualJoystick.Cockpit
{
    // Keyboard bindings (Bindings tab): every button of the two hand controllers the game models, and a few named
    // aircraft controls. Keys are global (VirtualJoystickSettings) and work in any aircraft / seat:
    //  - right hand = the nearest active stick/throttle on the pilot's right (normally the flight stick),
    //  - left hand = the nearest one on the left (throttle, helicopter collective, or the EF-24 rear seat's left stick).
    // The left hand's trigger is the game's "modifier" on some seats (AH-94 combat collective, EF-24 rear seat).
    // Works whether or not the virtual joystick is on; stick pitch/roll/rudder keys are handled by the virtual joystick.
    internal static class KeyActions
    {
        // ------------------------------------------------------------------ hand controllers

        // One hand controller: a VRJoystick or a VRThrottle, with the same buttons exposed either way.
        public class Hand
        {
            public readonly string Side;
            public VRJoystick Stick;
            public VRThrottle Throttle;

            public Hand(string side) { Side = side; }

            public bool Present => Stick != null || Throttle != null;

            public string DeviceName
            {
                get
                {
                    if (Stick != null) return "stick";
                    if (Throttle == null) return null;
                    return Throttle.name.IndexOf("collective", StringComparison.OrdinalIgnoreCase) >= 0 ? "collective" : "throttle";
                }
            }

            public UnityEventBase TriggerEvent => Stick != null ? (UnityEventBase)Stick.OnTriggerDown : Throttle?.OnTriggerDown;
            public UnityEventBase MenuEvent => Stick != null ? (UnityEventBase)Stick.OnMenuButtonDown : Throttle?.OnMenuButtonDown;
            public UnityEventBase SecondEvent => Stick?.OnSecondButtonDown;
            public UnityEventBase ThumbEvent => Stick != null ? (UnityEventBase)Stick.OnSetThumbstick : Throttle?.OnSetThumbstick;
            public UnityEventBase PressEvent => Stick != null ? (UnityEventBase)Stick.OnThumbstickButtonDown : Throttle?.OnStickPressDown;
            public UnityEventBase PressHeldEvent => Stick != null ? (UnityEventBase)Stick.OnThumbstickButton : Throttle?.OnStickPressed;

            // Edge state.
            private bool _trigger, _menu, _second, _press, _thumbActive;
            private float _triggerAxis;   // 0..1, ramps up while the trigger key is held

            public bool TriggerHeld => _trigger;

            // triggerRamp: seconds for the trigger axis to go from 0 to full while held (0 = at once).
            public void Update(string triggerKey, string menuKey, string secondKey, string thumbL, string thumbR, string thumbD, string thumbU, string pressKey,
                               float triggerRamp = 0f)
            {
                if (!Present)
                    return;
                bool wasHeld = _trigger;
                Edge(ref _trigger, Held(triggerKey), TriggerDown, TriggerUp);
                if (_trigger)
                {
                    _triggerAxis = triggerRamp <= 0f ? 1f : Mathf.Min(1f, _triggerAxis + (wasHeld ? Time.deltaTime / triggerRamp : 0f));
                    Invoke(Stick != null ? Stick.OnTriggerAxis : Throttle.OnTriggerAxis, _triggerAxis);
                }
                Edge(ref _menu, Held(menuKey),
                    () => (Stick != null ? Stick.OnMenuButtonDown : Throttle.OnMenuButtonDown)?.Invoke(),
                    () => (Stick != null ? Stick.OnMenuButtonUp : Throttle.OnMenuButtonUp)?.Invoke());
                if (Stick != null)
                    Edge(ref _second, Held(secondKey), () => Stick.OnSecondButtonDown?.Invoke(), () => Stick.OnSecondButtonUp?.Invoke());

                Vector3 thumb = new Vector3((Held(thumbR) ? 1f : 0f) - (Held(thumbL) ? 1f : 0f), (Held(thumbU) ? 1f : 0f) - (Held(thumbD) ? 1f : 0f), 0f);
                if (thumb != Vector3.zero)
                {
                    _thumbActive = true;
                    Invoke(Stick != null ? Stick.OnSetThumbstick : Throttle.OnSetThumbstick, thumb);
                }
                else if (_thumbActive)
                    ResetThumb();

                bool press = Held(pressKey);
                if (press)
                    (Stick != null ? Stick.OnThumbstickButton : Throttle.OnStickPressed)?.Invoke();
                Edge(ref _press, press,
                    () => (Stick != null ? Stick.OnThumbstickButtonDown : Throttle.OnStickPressDown)?.Invoke(),
                    () => (Stick != null ? Stick.OnThumbstickButtonUp : Throttle.OnStickPressUp)?.Invoke());
            }

            public void ReleaseAll()
            {
                if (Present)
                {
                    Edge(ref _trigger, false, null, TriggerUp);
                    Edge(ref _menu, false, null, () => (Stick != null ? Stick.OnMenuButtonUp : Throttle.OnMenuButtonUp)?.Invoke());
                    if (Stick != null)
                        Edge(ref _second, false, null, () => Stick.OnSecondButtonUp?.Invoke());
                    Edge(ref _press, false, null, () => (Stick != null ? Stick.OnThumbstickButtonUp : Throttle.OnStickPressUp)?.Invoke());
                    if (_thumbActive)
                        ResetThumb();
                }
                _trigger = _menu = _second = _press = _thumbActive = false;
            }

            // The click fires on press; the axis follows from Update (ramped).
            private void TriggerDown()
            {
                _triggerAxis = 0f;
                (Stick != null ? Stick.OnTriggerDown : Throttle.OnTriggerDown)?.Invoke();
            }

            private void TriggerUp()
            {
                _triggerAxis = 0f;
                Invoke(Stick != null ? Stick.OnTriggerAxis : Throttle.OnTriggerAxis, 0f);
                (Stick != null ? Stick.OnTriggerUp : Throttle.OnTriggerUp)?.Invoke();
            }

            private void ResetThumb()
            {
                _thumbActive = false;
                Invoke(Stick != null ? Stick.OnSetThumbstick : Throttle.OnSetThumbstick, Vector3.zero);
                (Stick != null ? Stick.OnResetThumbstick : Throttle.OnResetThumbstick)?.Invoke();
            }

            private static void Invoke(FloatEvent e, float v) => e?.Invoke(v);
            private static void Invoke(Vector3Event e, Vector3 v) => e?.Invoke(v);
        }

        public static readonly Hand Right = new Hand("right");
        public static readonly Hand Left = new Hand("left");

        // ------------------------------------------------------------------ named aircraft controls

        // An aircraft control found by the game's control name. Levers and detented rotaries both have states.
        // A guarded switch (under a VRSwitchCover) gets its cover lifted before it's moved off state 0.
        public class NamedControl
        {
            public readonly string Title;
            private readonly string[] _names;
            public VRInteractable Interactable;
            private VRLever _lever;
            private VRTwistKnobInt _knob;
            private VRSwitchCover _cover;

            public NamedControl(string title, params string[] names)
            {
                Title = title;
                _names = names;
            }

            public bool Found => Interactable != null && (_lever != null || _knob != null);
            public Vector3 Position => Interactable != null ? Interactable.transform.position : Vector3.zero;
            public int State => _lever != null ? _lever.currentState : _knob != null ? _knob.currentState : 0;
            public int States => _lever != null ? _lever.states : _knob != null ? _knob.states : 0;

            // e.g. "'Landing Gear' lever, 2 positions (now 2)"
            public string Describe() => !Found ? null :
                $"'{Interactable.GetControlReferenceName()}' {(_lever != null ? "lever" : "rotary")}, {States} positions (now {State + 1})";

            public void Set(int state)
            {
                if (!Found)
                    return;
                state = Mathf.Clamp(state, 0, States - 1);
                if (state != 0)
                    OpenCover();
                if (_lever != null) _lever.RemoteSetState(state);
                else _knob.RemoteSetState(state);
            }

            public void Step(int delta) => Set(State + delta);
            public void Cycle() => Set(States > 0 ? (State + 1) % States : 0);
            public void Toggle() => Set(State == 0 ? 1 : 0);

            // The cover enables its switch only when it's open; which of its positions that is varies, so try each.
            private void OpenCover()
            {
                if (_cover == null || _cover.coveredSwitch == null || _cover.coveredSwitch.enabled)
                    return;
                var lever = _cover.GetComponent<VRLever>() ?? _cover.GetComponentInChildren<VRLever>(true);
                if (lever == null)
                    return;
                for (int s = 0; s < lever.states && !_cover.coveredSwitch.enabled; s++)
                    if (s != lever.currentState)
                        Guard("switch cover", () => lever.RemoteSetState(s));
            }

            // The nearest match to the pilot's head, so two-seaters get this seat's control ("Master Arm (Front)").
            public void Find(VRInteractable[] all, VRSwitchCover[] covers, Vector3 head)
            {
                Interactable = null;
                _lever = null;
                _knob = null;
                _cover = null;
                foreach (bool exact in new[] { true, false })
                {
                    float best = float.MaxValue;
                    foreach (var vi in all)
                    {
                        string n = vi.GetControlReferenceName();
                        if (string.IsNullOrEmpty(n) || !Matches(n, exact) || vi.GetComponent<VRSwitchCover>() != null)
                            continue;
                        var lever = vi.GetComponent<VRLever>();
                        var knob = vi.GetComponent<VRTwistKnobInt>();
                        if (lever == null && knob == null)
                            continue;
                        float d = (vi.transform.position - head).sqrMagnitude;
                        if (d >= best)
                            continue;
                        best = d;
                        Interactable = vi;
                        _lever = lever;
                        _knob = knob;
                    }
                    if (Interactable != null)
                        break;
                }
                if (Interactable != null)
                    foreach (var c in covers)
                        if (c != null && c.coveredSwitch == Interactable)
                            _cover = c;
            }

            private bool Matches(string name, bool exact) => NameMatches(_names, name, exact);
        }

        // Exact also allows a seat suffix ("Engine R (Front)"), so both seats' copies compete on distance.
        private static bool NameMatches(string[] names, string name, bool exact)
        {
            if (exact && name.EndsWith(")"))
            {
                int open = name.LastIndexOf(" (", StringComparison.Ordinal);
                if (open > 0 && NameMatches(names, name.Substring(0, open), true))
                    return true;
            }
            foreach (var n in names)
                if (exact ? string.Equals(name, n, StringComparison.OrdinalIgnoreCase) : name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        // A push button found by the game's control name (exact, seat suffix allowed), nearest to the pilot's head.
        public class NamedButton
        {
            private readonly string[] _names;
            public VRInteractable Interactable;

            public NamedButton(params string[] names) => _names = names;

            public bool Found => Interactable != null;
            public Vector3 Position => Interactable != null ? Interactable.transform.position : Vector3.zero;

            // A VR finger push: VRButton listens to OnInteract / OnStopInteract only.
            public void Press()
            {
                if (!Found)
                    return;
                var v = Interactable;
                Guard(_names[0], () => v.OnInteract?.Invoke());
                Guard(_names[0], () => v.OnStopInteract?.Invoke());
            }

            public void Find(VRInteractable[] all, Vector3 head)
            {
                Interactable = null;
                float best = float.MaxValue;
                foreach (var vi in all)
                {
                    string n = vi.GetControlReferenceName();
                    if (string.IsNullOrEmpty(n) || !NameMatches(_names, n, true) || vi.GetComponent<VRButton>() == null)
                        continue;
                    float d = (vi.transform.position - head).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        Interactable = vi;
                    }
                }
            }
        }

        public static readonly NamedControl Flaps = new NamedControl("Flaps", "Flaps");
        public static readonly NamedControl Gear = new NamedControl("Landing gear", "Landing Gear");
        public static readonly NamedControl LaunchBar = new NamedControl("Launch bar", "Launch Bar");
        public static readonly NamedControl Hook = new NamedControl("Arrestor hook", "Arrestor Hook", "Tail Hook");
        public static readonly NamedControl Radar = new NamedControl("Radar power", "Radar Power");
        public static readonly NamedControl Engine1 = new NamedControl("Engine 1", "Left Engine", "Engine 1", "Engine L", "Engine");
        public static readonly NamedControl Engine2 = new NamedControl("Engine 2", "Right Engine", "Engine 2", "Engine R");
        public static readonly NamedControl Apu = new NamedControl("APU", "APU");
        public static readonly NamedControl Battery = new NamedControl("Main battery", "Main Battery", "Battery");
        public static readonly NamedControl Canopy = new NamedControl("Canopy", "Canopy");
        public static readonly NamedControl ParkingBrake = new NamedControl("Parking brake", "Brake Locks", "Brake Lock", "Parking Brake");
        public static readonly NamedControl MasterArm = new NamedControl("Master arm", "Master Arm");
        // Only some aircraft have an RWR mode lever; the rest switch the RWR display directly (Rwr below).
        public static readonly NamedControl RwrSwitch = new NamedControl("RWR", "RWR Mode", "RWR");
        // EF-24: EW / WPN knob, state 0 = WPN, 1 = EW.
        public static readonly NamedControl MasterMode = new NamedControl("Master mode (EW / WPN)", "Master Mode");
        private static readonly NamedControl[] Named = { Flaps, Gear, LaunchBar, Hook, Radar, Engine1, Engine2, Apu, Battery, Canopy, ParkingBrake, MasterArm, RwrSwitch, MasterMode };

        // Aircraft without a master arm switch (EF-24) have an ARM and a SAFE push button instead.
        public static readonly NamedButton ArmButton = new NamedButton("Master Arm");
        public static readonly NamedButton SafeButton = new NamedButton("Master Safe");
        // EF-24 AA / AG buttons, under the MASTER MODE label.
        public static readonly NamedButton AaButton = new NamedButton("AA Mode");
        public static readonly NamedButton AgButton = new NamedButton("AG Mode");
        private static readonly NamedButton[] Buttons = { ArmButton, SafeButton, AaButton, AgButton };

        public static bool HasMasterArm => MasterArm.Found || ArmButton.Found;
        public static Vector3? MasterArmPosition =>
            MasterArm.Found ? MasterArm.Position : ArmButton.Found ? Mid(ArmButton, SafeButton) : (Vector3?)null;
        public static Vector3? ArmingPosition => AaButton.Found ? Mid(AaButton, AgButton) : (Vector3?)null;

        private static Vector3 Mid(NamedButton a, NamedButton b) => b.Found ? (a.Position + b.Position) * 0.5f : a.Position;

        // Master arm state, for the toggle key with ARM / SAFE buttons.
        private static WeaponManager _wm;

        // The RWR display: its mode is 0 = on, 1 = mute (silent), 2 = off.
        public static DashRWR Rwr { get; private set; }

        // Wheel brakes and countermeasures, driven directly (on the controllers they're a trigger axis / a combo).
        private static WheelsController[] _wheels = new WheelsController[0];
        private static CountermeasureManager _cmm;
        private static VTOLVR.Multiplayer.MultiUserVehicleSync _muvs;
        private static bool _wheelBrakeHeld, _cmHeld;
        private static float _wheelBrake;               // 0..1, ramps up while the key is held
        private const float WheelBrakeRampTime = 0.4f;  // seconds from 0 to full brakes

        // Airbrake: the throttle trigger axis, which each aircraft wires to its airbrake (and the multicrew sync).
        // Only the axis is driven, not trigger down/up, so a trigger that's also a modifier isn't pressed.
        private static bool _airbrakeToggled, _airbrakeApplied;
        public static bool HasAirbrake { get; private set; }
        public static bool HasWheels => _wheels.Length > 0;
        public static bool HasCountermeasures => _cmm != null;

        // AA / AG arming mode: only the EF-24 has one (its HOTAS, one per seat).
        public static VTOLVR.DLC.EW.EF24Hotas Arming { get; private set; }

        private static readonly FieldInfo NvgEnabled = AccessTools.Field(typeof(HelmetController), "nvgEnabled");
        private static readonly FieldInfo StickRemoteOnly = AccessTools.Field(typeof(VRJoystick), "remoteOnly");

        // ------------------------------------------------------------------ state

        private static GameObject _vehicle;
        private static float _vehicleSince;
        private static bool _found;
        public static HelmetController Helmet { get; private set; }

        private static VirtualJoystickSettings S => VirtualJoystickSettings.Current;

        public static string VehicleName => _vehicle != null ? _vehicle.name.Replace("(Clone)", "").Trim() : null;
        public static bool Ready => _found;

        public static void Reset()
        {
            ReleaseAll();
            _vehicle = null;
            _found = false;
            Right.Stick = null; Right.Throttle = null;
            Left.Stick = null; Left.Throttle = null;
            Helmet = null;
            Rwr = null;
            Arming = null;
            _wheels = new WheelsController[0];
            _cmm = null;
            _muvs = null;
            HasAirbrake = false;
            _airbrakeToggled = _airbrakeApplied = false;
            _thrTarget = null;
            _thrDriving = false;
            Sweep = null;
            _swTarget = null;
            _swDriving = false;
            SoiKeys.Clear();
            TiltKeys.Clear();
            foreach (var n in Named)
                n.Interactable = null;
            foreach (var b in Buttons)
                b.Interactable = null;
            _wm = null;
        }

        public static void Update(GameObject vehicle, bool inputAllowed)
        {
            if (vehicle != _vehicle)
            {
                Reset();
                _vehicle = vehicle;
                _vehicleSince = Time.unscaledTime;
            }
            if (_vehicle == null)
                return;
            if (!_found && Time.unscaledTime - _vehicleSince > 1.5f)
                FindAll();
            if (!_found)
                return;
            if (!inputAllowed)
            {
                ReleaseAll();
                return;
            }

            Right.Update(S.triggerKey, S.weaponCycleKey, S.stickBKey, S.thumbLeftKey, S.thumbRightKey, S.thumbDownKey, S.thumbUpKey, S.thumbPressKey);
            Left.Update(S.throttleTriggerKey, S.throttleMenuKey, S.leftSecondKey, S.throttleThumbLeftKey, S.throttleThumbRightKey,
                        S.throttleThumbDownKey, S.throttleThumbUpKey, S.throttleThumbPressKey, S.throttleTriggerRamp);
            if (Left.Throttle != null)
                UpdateThrottleLever(Left.Throttle, Time.deltaTime);
            SoiKeys.Update();
            TiltKeys.Update(Time.deltaTime);

            // Engine / APU / battery / canopy / brake lock / master arm: state 1 = on (open), 0 = off (closed).
            OnOffToggle(Engine1, S.engine1OnKey, S.engine1OffKey, S.engine1ToggleKey);
            OnOffToggle(Engine2, S.engine2OnKey, S.engine2OffKey, S.engine2ToggleKey);
            OnOffToggle(Apu, S.apuOnKey, S.apuOffKey, S.apuToggleKey);
            OnOffToggle(Battery, S.batteryOnKey, S.batteryOffKey, S.batteryToggleKey);
            OnOffToggle(Canopy, S.canopyOpenKey, S.canopyCloseKey, S.canopyToggleKey);
            OnOffToggle(ParkingBrake, S.parkingBrakeOnKey, S.parkingBrakeOffKey, S.parkingBrakeToggleKey);
            SetWheelBrake(Held(S.wheelBrakeKey), Time.deltaTime);
            if (Pressed(S.airbrakeToggleKey)) _airbrakeToggled = !_airbrakeToggled;
            SetAirbrake(Held(S.airbrakeHoldKey) || _airbrakeToggled);
            SetCountermeasures(Held(S.countermeasureKey));
            if (MasterArm.Found)
                OnOffToggle(MasterArm, S.masterArmOnKey, S.masterArmOffKey, S.masterArmToggleKey);
            else if (ArmButton.Found)
            {
                bool armed = _wm != null && _wm.isMasterArmed;
                if (Pressed(S.masterArmOnKey) || (Pressed(S.masterArmToggleKey) && !armed)) ArmButton.Press();
                else if (Pressed(S.masterArmOffKey) || (Pressed(S.masterArmToggleKey) && armed)) SafeButton.Press();
            }
            OnOffToggle(MasterMode, S.masterModeEwKey, S.masterModeWpnKey, S.masterModeToggleKey);

            if (Pressed(S.rwrOnKey)) SetRwr(0);
            if (Pressed(S.rwrMuteKey)) SetRwr(1);
            if (Pressed(S.rwrOffKey)) SetRwr(2);
            if (Pressed(S.rwrCycleKey) && Rwr != null) SetRwr(((int)Rwr.mode + 1) % 3);

            if (Arming != null)
            {
                bool aa = Arming.armingMode == VTOLVR.DLC.EW.EF24Hotas.ArmingModes.AA;
                if (Pressed(S.armingAaKey) || (Pressed(S.armingToggleKey) && !aa)) Guard("arming mode", Arming.SetArmingAA);
                else if (Pressed(S.armingAgKey) || (Pressed(S.armingToggleKey) && aa)) Guard("arming mode", Arming.SetArmingAG);
            }

            if (Pressed(S.flapsDownKey)) Flaps.Step(+1);
            if (Pressed(S.flapsUpKey)) Flaps.Step(-1);
            if (Pressed(S.flapsCycleKey)) Flaps.Cycle();
            // Gear lever: state 1 = up, 0 = down. Launch bar / hook: 1 = extended. Radar: 1 = on.
            if (Pressed(S.gearUpKey)) Gear.Set(1);
            if (Pressed(S.gearDownKey)) Gear.Set(0);
            if (Pressed(S.gearToggleKey)) Gear.Toggle();
            if (Pressed(S.launchBarExtendKey)) LaunchBar.Set(1);
            if (Pressed(S.launchBarRetractKey)) LaunchBar.Set(0);
            if (Pressed(S.launchBarToggleKey)) LaunchBar.Toggle();
            if (Pressed(S.hookExtendKey)) Hook.Set(1);
            if (Pressed(S.hookRetractKey)) Hook.Set(0);
            if (Pressed(S.hookToggleKey)) Hook.Toggle();
            if (Sweep != null)
                UpdateSweep(Time.deltaTime);
            if (Pressed(S.radarOnKey)) Radar.Set(1);
            if (Pressed(S.radarOffKey)) Radar.Set(0);
            if (Pressed(S.radarToggleKey)) Radar.Toggle();

            if (Helmet != null)
            {
                if (Pressed(S.visorDownKey) && !Helmet.isVisorDown) Guard("visor", Helmet.ToggleVisor);
                if (Pressed(S.visorUpKey) && Helmet.isVisorDown) Guard("visor", Helmet.ToggleVisor);
                if (Pressed(S.visorToggleKey)) Guard("visor", Helmet.ToggleVisor);
                bool nvgOn = NvgEnabled != null && (bool)NvgEnabled.GetValue(Helmet);
                if (Pressed(S.nvgOnKey) && (NvgEnabled == null || !nvgOn)) Guard("NVG", Helmet.ToggleNVG);
                if (Pressed(S.nvgOffKey) && (NvgEnabled == null || nvgOn)) Guard("NVG", Helmet.ToggleNVG);
                if (Pressed(S.nvgToggleKey)) Guard("NVG", Helmet.ToggleNVG);
            }
        }

        // Wheel brakes while held, ramping from 0 to full over WheelBrakeRampTime; off at once on release
        // (re-applied every frame, since the brake trigger writes the same value).
        private static void SetWheelBrake(bool held, float dt)
        {
            if (!held && !_wheelBrakeHeld)
                return;
            _wheelBrakeHeld = held;
            _wheelBrake = held ? Mathf.Min(1f, _wheelBrake + dt / WheelBrakeRampTime) : 0f;
            foreach (var w in _wheels)
                if (w != null)
                    w.SetBrakes(_wheelBrake);
        }

        // Full airbrake while on (re-applied every frame, as the trigger does); retracted once when it goes off. The
        // throttle trigger key, while held, writes full brakes itself.
        private static void SetAirbrake(bool on)
        {
            if (!HasAirbrake || Left.TriggerHeld || (!on && !_airbrakeApplied))
                return;
            _airbrakeApplied = on;
            Left.Throttle.OnTriggerAxis.Invoke(on ? 1f : 0f);
        }

        // Countermeasures fire while held, at the selected release rate, as the CMS button does. A multicrew seat that
        // doesn't own the aircraft asks the owner to fire.
        private static void SetCountermeasures(bool held)
        {
            if (held == _cmHeld || _cmm == null)
                return;
            _cmHeld = held;
            bool remote = VTOLVR.Multiplayer.VTOLMPUtils.IsMultiplayer() && _muvs != null && !_muvs.isMine;
            Guard("countermeasures", () =>
            {
                if (held)
                {
                    if (remote) _muvs.RemoteStartCM(); else _cmm.FireCM();
                }
                else
                {
                    if (remote) _muvs.RemoteStopCM(); else _cmm.StopFireCM();
                }
            });
        }

        private static void OnOffToggle(NamedControl c, string onKey, string offKey, string toggleKey)
        {
            if (Pressed(onKey)) c.Set(1);
            if (Pressed(offKey)) c.Set(0);
            if (Pressed(toggleKey)) c.Toggle();
        }

        public static bool HasRwr => Rwr != null || RwrSwitch.Found;

        // RWR mode 0 = on, 1 = mute, 2 = off. With a mode lever, the lever is moved to whichever position gives that
        // mode (its wiring isn't the same in every aircraft); otherwise the display is switched directly, as the
        // RWR mode button does.
        private static void SetRwr(int mode)
        {
            if (RwrSwitch.Found)
            {
                if (Rwr == null)
                {
                    Guard("RWR", () => RwrSwitch.Set(mode));
                    return;
                }
                if ((int)Rwr.mode == mode)
                    return;
                Guard("RWR", () => RwrSwitch.Set(mode));
                for (int s = 0; s < RwrSwitch.States && (int)Rwr.mode != mode; s++)
                    if (s != RwrSwitch.State)
                        Guard("RWR", () => RwrSwitch.Set(s));
                if ((int)Rwr.mode == mode)
                    return;
            }
            if (Rwr != null)
                Guard("RWR", () => Rwr.SetMasterMode(mode));
        }

        private static void ReleaseAll()
        {
            SetWheelBrake(false, 0f);
            SetAirbrake(false);
            SetCountermeasures(false);
            Right.ReleaseAll();
            Left.ReleaseAll();
            SoiKeys.ReleaseAll();
        }

        // ------------------------------------------------------------------ discovery

        public static void Rescan()
        {
            if (_vehicle == null)
                return;
            ReleaseAll();
            FindAll();
        }

        private static void FindAll()
        {
            _found = true;
            HintCache.Clear();
            Right.Stick = null; Right.Throttle = null;
            Left.Stick = null; Left.Throttle = null;

            Camera cam = ScreenPointer.Camera != null ? ScreenPointer.Camera : Camera.main;
            Transform root = _vehicle.transform;
            Vector3 head = root.InverseTransformPoint(cam != null ? cam.transform.position : root.position);

            // Candidates: this seat's active controllers (remote-only sticks belong to the other seat in multicrew).
            float bestR = float.MaxValue, bestL = float.MaxValue;
            void Consider(Component c, VRJoystick js, VRThrottle th)
            {
                Vector3 p = root.InverseTransformPoint(c.transform.position) - head;
                float d = p.sqrMagnitude;
                // Right hand: prefer a stick (and BYOJ's choice, the side stick, when there are two).
                if (p.x >= 0f || (js != null && Mathf.Abs(p.x) < 0.08f))
                {
                    float score = d - (js != null ? 1f : 0f) - (js != null && PathHas(c.transform, "side") ? 0.5f : 0f);
                    if (score < bestR)
                    {
                        bestR = score;
                        Right.Stick = js;
                        Right.Throttle = th;
                    }
                }
                else if (d < bestL)
                {
                    bestL = d;
                    Left.Stick = js;
                    Left.Throttle = th;
                }
            }
            foreach (var js in _vehicle.GetComponentsInChildren<VRJoystick>(false))
                if (js.isActiveAndEnabled && !(StickRemoteOnly != null && (bool)StickRemoteOnly.GetValue(js)))
                    Consider(js, js, null);
            foreach (var th in _vehicle.GetComponentsInChildren<VRThrottle>(false))
                if (th.isActiveAndEnabled)
                    Consider(th, null, th);

            Helmet = _vehicle.GetComponentInChildren<HelmetController>(true);
            SoiKeys.Find(_vehicle, root.TransformPoint(head));
            TiltKeys.Find(_vehicle);
            Sweep = _vehicle.GetComponentInChildren<AeroGeometryLever>(true);
            _swTarget = null;
            _swDriving = false;
            var all = _vehicle.GetComponentsInChildren<VRInteractable>(true);
            var covers = _vehicle.GetComponentsInChildren<VRSwitchCover>(true);
            Vector3 headWorld = root.TransformPoint(head);
            foreach (var n in Named)
                n.Find(all, covers, headWorld);
            foreach (var b in Buttons)
                b.Find(all, headWorld);
            _wm = _vehicle.GetComponentInChildren<WeaponManager>(true);
            Rwr = Nearest(_vehicle.GetComponentsInChildren<DashRWR>(true), headWorld);
            Arming = Nearest(_vehicle.GetComponentsInChildren<VTOLVR.DLC.EW.EF24Hotas>(true), headWorld);
            _wheels = _vehicle.GetComponentsInChildren<WheelsController>(true);
            _cmm = _vehicle.GetComponentInChildren<CountermeasureManager>(true);
            _muvs = _vehicle.GetComponentInChildren<VTOLVR.Multiplayer.MultiUserVehicleSync>(true);
            HasAirbrake = Left.Throttle != null && EventHints.HasListener(Left.Throttle.OnTriggerAxis, "Brake");

            var found = new List<string>();
            foreach (var n in Named)
                found.Add(n.Title + (n.Found ? "" : " (missing)"));
            if (ArmButton.Found)
                found.Add("Master arm buttons");
            if (Rwr != null)
                found.Add("RWR display");
            if (Arming != null)
                found.Add("AA/AG arming mode");
            if (Sweep != null)
                found.Add("Wing sweep");
            if (TiltKeys.Tilt != null)
                found.Add($"Engine tilt (0-{TiltKeys.Tilt.maxTilt:0}°)");
            Log.Info($"Key bindings ready in {VehicleName}: right hand {Right.DeviceName ?? "missing"}, left hand {Left.DeviceName ?? "missing"}, " +
                     $"helmet {(Helmet != null ? "ok" : "missing")}; {string.Join(", ", found)}");
        }

        // ------------------------------------------------------------------ hints for the UI

        public static string HintRightTrigger() => Hint(Right, h => h.TriggerEvent);
        public static string HintRightMenu() => Hint(Right, h => h.MenuEvent);
        public static string HintRightSecond() => Right.Present && Right.Stick == null ? "(no second button on this controller)" : Hint(Right, h => h.SecondEvent);
        public static string HintRightThumb() => Hint(Right, h => h.ThumbEvent);
        public static string HintRightPress() => Hint(Right, h => h.PressEvent, h => h.PressHeldEvent);
        public static string HintLeftTrigger() => Hint(Left, h => h.TriggerEvent);
        public static string HintLeftMenu() => Hint(Left, h => h.MenuEvent);
        public static string HintLeftSecond() => Left.Present && Left.Stick == null ? $"(the {Left.DeviceName} has no second button)" : Hint(Left, h => h.SecondEvent);
        public static string HintLeftThumb() => Hint(Left, h => h.ThumbEvent);
        public static string HintLeftPress() => Hint(Left, h => h.PressEvent, h => h.PressHeldEvent);

        public static string HintThrottleMove()
        {
            if (!_found) return null;
            if (Left.Throttle == null) return Left.Present ? $"(the left {Left.DeviceName} has no throttle travel)" : "(no left controller)";
            var th = Left.Throttle;
            return th.abGate ? $"{Left.DeviceName}, afterburner detent at {th.abGateThreshold:0.00}" : $"{Left.DeviceName}, no afterburner detent";
        }

        // Hints are read from the game's event wiring (reflection), so they're cached per scan: the settings window
        // asks for them several times a frame.
        private static readonly Dictionary<UnityEventBase, string> HintCache = new Dictionary<UnityEventBase, string>();

        private static string Hint(Hand h, params Func<Hand, UnityEventBase>[] events)
        {
            if (!_found)
                return null;
            if (!h.Present)
                return $"(no {h.Side} controller in this seat)";
            var list = new UnityEventBase[events.Length];
            for (int i = 0; i < events.Length; i++)
                list[i] = events[i](h);
            if (list[0] != null && HintCache.TryGetValue(list[0], out string cached))
                return cached;
            string text = EventHints.Describe(list) ?? "(nothing wired to this input here)";
            if (list[0] != null)
                HintCache[list[0]] = text;
            return text;
        }

        // ------------------------------------------------------------------ throttle lever

        // Presets move the lever like a hand would: a full idle-to-full sweep is one ~20 cm reach, which takes a
        // person roughly 0.35-0.45 s (average hand speed ~0.5 m/s, peak ~1 m/s). So 2.5 throttle units per second.
        private const float PresetRate = 2.5f;
        // Throttle keys move the lever this many times faster above the afterburner detent.
        private const float AbGateRateMultiplier = 3f;
        private static float _thrCmd;
        private static float? _thrTarget;
        private static bool _thrDriving;

        private static void UpdateThrottleLever(VRThrottle th, float dt)
        {
            if (!th.isActiveAndEnabled)
                return;
            float min = th.minThrottle, max = Mathf.Clamp01(th.throttleLimiter);

            // MIL sits just under the afterburner detent (the game counts up to threshold + 0.01 as below it);
            // without a detent there's no afterburner, so MIL is simply the maximum.
            if (Pressed(S.throttleFullKey)) _thrTarget = max;
            if (Pressed(S.throttleMilKey)) _thrTarget = th.abGate ? Mathf.Clamp(th.abGateThreshold - 0.005f, min, max) : max;
            if (Pressed(S.throttleZeroKey)) _thrTarget = min;

            float dir = (Held(S.throttleUpKey) ? 1f : 0f) - (Held(S.throttleDownKey) ? 1f : 0f);
            if (dir != 0f)
                _thrTarget = null;
            if (dir == 0f && !_thrTarget.HasValue)
            {
                _thrDriving = false;
                return;
            }
            if (!_thrDriving)
                _thrCmd = th.currentThrottle;
            _thrDriving = true;

            if (dir != 0f)
            {
                // Past the afterburner detent (pushing through it, or pulling back out of AB) the lever moves faster.
                bool pastGate = th.abGate && (dir > 0f ? _thrCmd >= th.abGateThreshold : _thrCmd > th.abGateThreshold);
                float rate = S.throttleRate * (pastGate ? AbGateRateMultiplier : 1f);
                _thrCmd = Mathf.Clamp(_thrCmd + dir * rate * dt, min, max);
            }
            else
            {
                _thrCmd = Mathf.MoveTowards(_thrCmd, _thrTarget.Value, PresetRate * dt);
                if (Mathf.Approximately(_thrCmd, _thrTarget.Value))
                    _thrTarget = null;
            }
            ThrottleLever.Move(th, _thrCmd, dt);
        }

        // ------------------------------------------------------------------ wing sweep lever

        // The sweep lever's output is the sweep amount: 0 = lever forward / wings forward (next to the AUTO slot,
        // F-14 style), 1 = lever back / wings swept. The game's RemoteSetManual / RemoteSetAuto snap the lever in one
        // step, so keys move it a little every frame instead, like a hand: presets cover the full travel in ~0.5 s,
        // held increase/decrease at BYOJ's rate (full travel in 2 s). The wings follow at the aircraft's own rate.
        public static AeroGeometryLever Sweep { get; private set; }
        private const float SweepPresetRate = 2f;
        private const float SweepHoldRate = 0.5f;
        private static float _swCmd;
        private static float? _swTarget;
        private static bool _swToAuto;   // after reaching the forward stop, drop into the AUTO slot
        private static bool _swDriving;

        private static void UpdateSweep(float dt)
        {
            if (Pressed(S.sweepMinKey)) { _swTarget = 0f; _swToAuto = false; }
            if (Pressed(S.sweepMaxKey)) { _swTarget = 1f; _swToAuto = false; }
            if (Pressed(S.sweepAutoKey) && !Sweep.CurrentAuto) { _swTarget = 0f; _swToAuto = true; }

            float dir = (Held(S.sweepIncreaseKey) ? 1f : 0f) - (Held(S.sweepDecreaseKey) ? 1f : 0f);
            if (dir != 0f)
            {
                _swTarget = null;
                _swToAuto = false;
            }
            if (dir == 0f && !_swTarget.HasValue)
            {
                _swDriving = false;
                return;
            }

            if (!_swDriving)
            {
                _swCmd = Sweep.CurrentOutput;
                if (Sweep.CurrentAuto)
                {
                    // Out of the AUTO slot first (it sits at the forward stop).
                    _swCmd = 0f;
                    Guard("sweep", () => Sweep.RemoteSetManual(0f));
                    PlaySweepSlot();
                }
            }
            _swDriving = true;

            if (dir != 0f)
                _swCmd = Mathf.Clamp01(_swCmd + dir * SweepHoldRate * dt);
            else
                _swCmd = Mathf.MoveTowards(_swCmd, _swTarget.Value, SweepPresetRate * dt);
            Guard("sweep", () => Sweep.RemoteSetManual(_swCmd));

            if (_swTarget.HasValue && Mathf.Approximately(_swCmd, _swTarget.Value))
            {
                if (_swToAuto)
                {
                    Guard("sweep", Sweep.RemoteSetAuto);
                    PlaySweepSlot();
                }
                _swTarget = null;
                _swToAuto = false;
            }
        }

        // The clunk of the lever entering / leaving the AUTO slot (the game only plays it for a VR hand).
        private static void PlaySweepSlot()
        {
            if (Sweep.autoSlotSource != null && Sweep.autoSlotClip != null)
                Sweep.autoSlotSource.PlayOneShot(Sweep.autoSlotClip);
        }

        // ------------------------------------------------------------------ helpers

        private static void Edge(ref bool state, bool now, Action down, Action up)
        {
            if (now == state)
                return;
            state = now;
            try
            {
                if (now) down?.Invoke();
                else up?.Invoke();
            }
            catch (Exception e)
            {
                Log.Warn("Key binding event threw: " + e.Message);
            }
        }

        internal static bool Held(string key)
        {
            KeyCode k = VirtualJoystickSettings.ParseKeyQuiet(key);
            return k != KeyCode.None && Input.GetKey(k);
        }

        internal static bool Pressed(string key)
        {
            KeyCode k = VirtualJoystickSettings.ParseKeyQuiet(key);
            return k != KeyCode.None && Input.GetKeyDown(k);
        }

        private static T Nearest<T>(T[] all, Vector3 head) where T : Component
        {
            T best = null;
            float bestD = float.MaxValue;
            foreach (var c in all)
            {
                float d = (c.transform.position - head).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = c;
                }
            }
            return best;
        }

        private static bool PathHas(Transform t, string part)
        {
            for (; t != null; t = t.parent)
                if (t.name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        private static void Guard(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log.Warn($"Key binding ({what}) threw: {e.Message}");
            }
        }
    }
}
