using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VirtualJoystick
{
    // SOI cursor mode: the mouse becomes the cursor of whichever MFD page is the SOI.
    //  - TGP: FPS-style pod aiming (TgpControl).
    //  - Radar / ARAD / map / TSD / anything else: mouse movement is turned into the page's own thumbstick input.
    //    Those pages all move their cursor at "speed * frame time * axis" with no clamp on axis, so
    //    axis = pixels * k / frameTime moves the cursor in proportion to the mouse, whatever the frame rate.
    //    When the mouse goes still the page gets its "thumbstick released", which snaps the cursor to contacts.
    // The scroll wheel presses the page's own zoom buttons. LMB is the page's thumbstick press (TSD: click; drag pans),
    // except on the NAV map, where it sends a GPS point.
    internal class SoiCursor
    {
        public enum Kind { None, Tgp, Radar, Arad, Map, Tsd, Other }

        // At cursor sensitivity 1: 50 px of mouse = one second of full thumbstick on that page.
        private const float AxisSecondsPerPixel = 1f / 50f;
        private const float ReleaseAfterSeconds = 0.15f;

        private readonly TgpControl _tgp = new TgpControl();

        private MFDPage[] _pages = new MFDPage[0];
        private MFDPortalPage[] _portalPages = new MFDPortalPage[0];
        private TargetingMFDPage[] _tgps = new TargetingMFDPage[0];
        private MFDRadarUI[] _radars = new MFDRadarUI[0];
        private DashMapDisplay[] _maps = new DashMapDisplay[0];
        private MFDAntiRadarAttackDisplay[] _arads = new MFDAntiRadarAttackDisplay[0];

        private MFDPage _soiPage;
        private MFDPortalPage _soiPortal;
        private Component _target;   // the page's behaviour component (TGP, radar, map...)
        private bool _axisActive;
        private float _lastMoveTime;
        private bool _buttonHeld;

        public Kind Current { get; private set; }

        // TSD: LMB click = thumbstick press, LMB drag = pan the view (the same touch events FlatScreen 3 / a VR finger use).
        private const float DragThresholdPx = 4f;
        // At cursor sensitivity 1, 400 px of mouse drags the view by one screen width.
        private const float DragPixelsPerScreenWidth = 400f;
        private static readonly FieldInfo TouchBeginField = AccessTools.Field(typeof(VRTouchScreenInteractable), "OnBeginTouch");
        private static readonly FieldInfo TouchingField = AccessTools.Field(typeof(VRTouchScreenInteractable), "OnTouching");
        // Both properties have private setters (FlatScreen 3 builds against a publicized assembly; we use reflection).
        private static readonly MethodInfo SetIsTouching = AccessTools.PropertySetter(typeof(VRTouchScreenInteractable), "isTouching");
        private static readonly MethodInfo SetTouchingLocalPoint = AccessTools.PropertySetter(typeof(VRTouchScreenInteractable), "touchingLocalPoint");
        private static bool CanDrag => TouchBeginField != null && TouchingField != null && SetIsTouching != null && SetTouchingLocalPoint != null;
        private bool _lmbDown;
        private bool _mapLmb; // edge state for the NAV map's GPS send
        private float _lmbTravel;
        private VRTouchScreenInteractable _dragTouch;

        public string Label
        {
            get
            {
                switch (Current)
                {
                    case Kind.Tgp: return "TGP";
                    case Kind.Radar: return "RADAR";
                    case Kind.Arad: return "ARAD";
                    case Kind.Map: return "MAP";
                    case Kind.Tsd: return "TSD";
                    case Kind.Other: return "SOI";
                    default: return "NO SOI";
                }
            }
        }

        public void Begin(GameObject vehicle)
        {
            Current = Kind.None;
            _soiPage = null;
            _soiPortal = null;
            _target = null;
            _axisActive = false;
            _buttonHeld = false;
            if (vehicle == null)
                return;
            _pages = vehicle.GetComponentsInChildren<MFDPage>(true);
            _portalPages = vehicle.GetComponentsInChildren<MFDPortalPage>(true);
            _tgps = vehicle.GetComponentsInChildren<TargetingMFDPage>(true);
            _radars = vehicle.GetComponentsInChildren<MFDRadarUI>(true);
            _maps = vehicle.GetComponentsInChildren<DashMapDisplay>(true);
            _arads = vehicle.GetComponentsInChildren<MFDAntiRadarAttackDisplay>(true);
            RefreshSoi();
        }

        public void End()
        {
            FinishCurrent();
            Current = Kind.None;
        }

        // After a middle-mouse re-centre, pressing the wheel usually nudges the mouse. Movement is ignored while the
        // wheel is held and then until the mouse has clearly moved (RecenterDeadzonePx in total) or the window ends.
        private const float RecenterDeadzonePx = 25f;
        private const float RecenterDeadzoneSeconds = 0.6f;
        private bool _recenterSettling;
        private float _recenterAt;
        private Vector2 _recenterTravel;

        // Every frame while G is held. px = this frame's mouse movement (+x right, +y up); lmb = left button held.
        public void Update(Vector2 px, bool lmb, float tgpSensitivity, float cursorSensitivity)
        {
            RefreshSoi();
            if (Current == Kind.None)
                return;

            if (_recenterSettling)
            {
                if (Input.GetMouseButton(2))
                {
                    _recenterAt = Time.unscaledTime; // window starts when the wheel is let go
                    _recenterTravel = Vector2.zero;
                    px = Vector2.zero;
                }
                else
                {
                    _recenterTravel += px;
                    if (_recenterTravel.magnitude < RecenterDeadzonePx && Time.unscaledTime - _recenterAt < RecenterDeadzoneSeconds)
                        px = Vector2.zero;
                    else
                        _recenterSettling = false; // a deliberate move (or the window ran out): resume from this frame
                }
            }

            if (Current == Kind.Tgp)
            {
                SetButton(lmb);
                _tgp.Update(px, tgpSensitivity);
                return;
            }

            if (Current == Kind.Tsd)
            {
                // Click vs drag: below the threshold the cursor holds still so the click lands where you aimed.
                if (lmb && !_lmbDown)
                {
                    _lmbDown = true;
                    _lmbTravel = 0f;
                }
                if (_lmbDown && lmb)
                {
                    _lmbTravel += px.magnitude;
                    if (_dragTouch == null && _lmbTravel > DragThresholdPx)
                        BeginDrag();
                    if (_dragTouch != null)
                        Drag(px, cursorSensitivity);
                    return;
                }
                if (_lmbDown && !lmb)
                {
                    _lmbDown = false;
                    if (_dragTouch != null)
                        EndDrag();
                    else
                        Press();
                    return;
                }
            }
            else if (Current == Kind.Map)
            {
                // GPS SEND: one GPS point at the map cursor per click.
                if (lmb && !_mapLmb && _target is DashMapDisplay map)
                {
                    try { map.SendGPSTarget(); }
                    catch (Exception e) { Log.Warn("Map GPS send failed: " + e.Message); }
                }
                _mapLmb = lmb;
            }
            else
            {
                SetButton(lmb);
            }

            MoveCursor(px, cursorSensitivity);
        }

        private void MoveCursor(Vector2 px, float cursorSensitivity)
        {
            if (px != Vector2.zero)
            {
                float dt = Current == Kind.Radar ? Time.unscaledDeltaTime : Time.deltaTime;
                if (dt <= 0f)
                    return;
                Vector2 axis = px * (cursorSensitivity * AxisSecondsPerPixel / dt);
                InvokeAxis(new Vector3(axis.x, axis.y, 0f));
                _axisActive = true;
                _lastMoveTime = Time.unscaledTime;
            }
            else if (_axisActive && Time.unscaledTime - _lastMoveTime > ReleaseAfterSeconds)
            {
                ReleaseAxis();
            }
        }

        // A single thumbstick click (TSD): settle the cursor first so it snaps to a contact, then press and release.
        private void Press()
        {
            if (_axisActive)
                ReleaseAxis();
            InvokeButton(Phase.Down);
            InvokeButton(Phase.Held);
            InvokeButton(Phase.Up);
        }

        private void BeginDrag()
        {
            var tsd = _soiPortal as MFDPTacticalSituationDisplay;
            VRTouchScreenInteractable touch = tsd != null ? tsd.dragInteractable : null;
            if (touch == null || touch.screenRect == null)
                return;
            if (!CanDrag)
            {
                Log.Warn("VRTouchScreenInteractable members not found (game update?); TSD drag disabled");
                return;
            }
            if (_axisActive)
                ReleaseAxis();
            _dragTouch = touch;
            SetIsTouching.Invoke(_dragTouch, new object[] { true });
            InvokeTouch(TouchBeginField, _dragTouch);
        }

        // Moves the virtual finger across the screen: px scaled so DragPixelsPerScreenWidth = one screen width,
        // in the screen's own orientation, converted into the touch interactable's local space.
        private void Drag(Vector2 px, float cursorSensitivity)
        {
            if (px == Vector2.zero || _dragTouch == null)
                return;
            if (!_dragTouch.isActiveAndEnabled)
            {
                EndDrag();
                return;
            }
            RectTransform screen = _dragTouch.screenRect;
            float unitsPerPx = screen.rect.width / DragPixelsPerScreenWidth * cursorSensitivity;
            Vector3 world = screen.TransformVector(new Vector3(px.x, px.y, 0f) * unitsPerPx);
            Vector3 local = _dragTouch.transform.InverseTransformVector(world);
            SetTouchingLocalPoint.Invoke(_dragTouch, new object[] { _dragTouch.touchingLocalPoint + local });
            InvokeTouch(TouchingField, _dragTouch);
        }

        private void EndDrag()
        {
            if (_dragTouch != null)
                SetIsTouching?.Invoke(_dragTouch, new object[] { false });
            _dragTouch = null;
        }

        private static void InvokeTouch(FieldInfo field, VRTouchScreenInteractable touch)
        {
            if (field != null && field.GetValue(touch) is Action action)
                action();
        }

        // LMB on every page but the TSD and NAV map: held thumbstick button.
        private void SetButton(bool down)
        {
            if (down == _buttonHeld)
            {
                if (down)
                    InvokeButton(Phase.Held);
                return;
            }
            _buttonHeld = down;
            if (down)
            {
                // Make sure the page has settled its cursor (snapped to a contact) before the press.
                if (_axisActive)
                    ReleaseAxis();
                InvokeButton(Phase.Down);
                InvokeButton(Phase.Held);
            }
            else
            {
                InvokeButton(Phase.Up);
            }
        }

        private static readonly MethodInfo AradDeselect = AccessTools.Method(typeof(MFDAntiRadarAttackDisplay), "DeselectTarget");

        // Middle mouse: the page's own "back to default" action.
        //  TGP -> FWD mode; map -> reset offset onto the aircraft; TSD -> re-centre view on the aircraft;
        //  radar -> drop the lock; ARAD -> deselect the target.
        public void Recenter()
        {
            _recenterSettling = true;
            _recenterAt = Time.unscaledTime;
            _recenterTravel = Vector2.zero;
            try
            {
                switch (Current)
                {
                    case Kind.Tgp:
                        _tgp.End(); // stop any slew / pending re-lock first
                        ((TargetingMFDPage)_target).PointForward();
                        _tgp.Begin((TargetingMFDPage)_target);
                        break;
                    case Kind.Map:
                        ((DashMapDisplay)_target).ResetOffset();
                        break;
                    case Kind.Tsd:
                        EndDrag();
                        ((MFDPTacticalSituationDisplay)_soiPortal).RecenterView();
                        break;
                    case Kind.Radar:
                        ((MFDRadarUI)_target).Unlock();
                        break;
                    case Kind.Arad:
                        AradDeselect?.Invoke(_target, new object[] { true });
                        break;
                }
            }
            catch (Exception e)
            {
                Exception inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                Log.Warn($"SOI recenter on {Label} failed: {inner.Message}");
            }
        }

        // Scroll wheel: +1 = zoom in, -1 = zoom out, using the page's own zoom/range buttons.
        public void Zoom(int dir)
        {
            switch (Current)
            {
                case Kind.Tgp:
                    var tgp = (TargetingMFDPage)_target;
                    if (dir > 0) tgp.ZoomIn(); else tgp.ZoomOut();
                    break;
                case Kind.Radar:
                    var radar = (MFDRadarUI)_target;
                    if (dir > 0) radar.RangeDown(); else radar.RangeUp();
                    break;
                case Kind.Map:
                    var map = (DashMapDisplay)_target;
                    if (dir > 0) map.ZoomIn(); else map.ZoomOut();
                    break;
                case Kind.Tsd:
                    var tsd = (MFDPTacticalSituationDisplay)_soiPortal;
                    if (dir > 0) tsd.PrevViewScale(); else tsd.NextViewScale();
                    break;
            }
        }

        // ------------------------------------------------------------------

        private void RefreshSoi()
        {
            MFDPage page = null;
            MFDPortalPage portal = null;
            foreach (var p in _pages)
            {
                if (p != null && p.isSOI)
                {
                    page = p;
                    break;
                }
            }
            if (page == null)
            {
                foreach (var p in _portalPages)
                {
                    if (p != null && p.isSOI)
                    {
                        portal = p;
                        break;
                    }
                }
            }

            if (page == _soiPage && portal == _soiPortal && (Current != Kind.None || (page == null && portal == null)))
                return;

            // SOI changed (or first look): finish whatever the previous page was doing.
            FinishCurrent();
            _soiPage = page;
            _soiPortal = portal;
            Classify();

            if (Current == Kind.Tgp)
            {
                string problem = _tgp.Begin((TargetingMFDPage)_target);
                if (problem != null)
                    VirtualJoystickBehaviour.ShowToastStatic(problem);
            }
        }

        private void Classify()
        {
            _target = null;
            if (_soiPage == null && _soiPortal == null)
            {
                Current = Kind.None;
                return;
            }
            foreach (var t in _tgps)
                if (t != null && IsOnSoi(t.mfdPage, t.portalPage)) { _target = t; Current = Kind.Tgp; return; }
            foreach (var r in _radars)
                if (r != null && IsOnSoi(r.mfdPage, r.portalPage)) { _target = r; Current = Kind.Radar; return; }
            foreach (var a in _arads)
                if (a != null && IsOnSoi(a.mfdPage, null)) { _target = a; Current = Kind.Arad; return; }
            foreach (var m in _maps)
                if (m != null && IsOnSoi(m.mfdPage, m.portalPage)) { _target = m; Current = Kind.Map; return; }
            if (_soiPortal is MFDPTacticalSituationDisplay)
            {
                Current = Kind.Tsd;
                return;
            }
            Current = Kind.Other;
        }

        private bool IsOnSoi(MFDPage page, MFDPortalPage portal) =>
            (page != null && page == _soiPage) || (portal != null && portal == _soiPortal);

        private void FinishCurrent()
        {
            if (Current == Kind.Tgp)
                _tgp.End();
            EndDrag();
            _lmbDown = false;
            if (_buttonHeld)
            {
                _buttonHeld = false;
                InvokeButton(Phase.Up);
            }
            if (_axisActive)
                ReleaseAxis();
        }

        private void ReleaseAxis()
        {
            _axisActive = false;
            InvokeAxis(Vector3.zero);
            if (_soiPage != null) _soiPage.OnInputAxisReleased?.Invoke();
            else if (_soiPortal != null) _soiPortal.OnInputAxisReleased?.Invoke();
        }

        private void InvokeAxis(Vector3 axis)
        {
            if (_soiPage != null) _soiPage.OnInputAxis?.Invoke(axis);
            else if (_soiPortal != null) _soiPortal.OnInputAxis?.Invoke(axis);
        }

        private enum Phase { Down, Held, Up }

        private void InvokeButton(Phase phase)
        {
            if (_soiPage != null)
            {
                if (phase == Phase.Down) _soiPage.OnInputButtonDown?.Invoke();
                else if (phase == Phase.Held) _soiPage.OnInputButton?.Invoke();
                else _soiPage.OnInputButtonUp?.Invoke();
            }
            else if (_soiPortal != null)
            {
                if (phase == Phase.Down) _soiPortal.OnInputButtonDown?.Invoke();
                else if (phase == Phase.Held) _soiPortal.OnInputButton?.Invoke();
                else _soiPortal.OnInputButtonUp?.Invoke();
            }
        }
    }
}
