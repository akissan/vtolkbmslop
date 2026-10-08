using System.Collections.Generic;
using UnityEngine;

namespace VirtualJoystick.Cockpit
{
    // SOI key bindings that work the same in every aircraft, independently of the controller thumbstick bindings:
    // switch SOI left/right, slew the SOI cursor, select, and zoom. They drive the game's SOI machinery directly,
    // as BYOJoystick's SOI controls do:
    //  - switching: the pages of the aircraft's SOI switcher (touchscreen-portal one if there is one, else the classic
    //    one; the one for this seat on two-seaters), one step per key press, wrapping around.
    //  - slew / select: the MFD managers' input axis / button, which the SOI page receives like thumbstick input.
    //  - zoom: the SOI page's own zoom (TGP zoom, radar range, map zoom, TSD view scale).
    internal static class SoiKeys
    {
        private static ThrottleSOISwitcher _switcher;
        private static MultiPortalSOISwitcher _portalSwitcher;
        private static MFDManager[] _mfdManagers = new MFDManager[0];
        private static MFDPortalManager[] _portalManagers = new MFDPortalManager[0];
        private static TargetingMFDPage[] _tgps = new TargetingMFDPage[0];
        private static MFDRadarUI[] _radars = new MFDRadarUI[0];
        private static DashMapDisplay[] _maps = new DashMapDisplay[0];
        private static MFDPTacticalSituationDisplay[] _tsds = new MFDPTacticalSituationDisplay[0];

        private static bool _slewActive, _selectHeld;

        private static VirtualJoystickSettings S => VirtualJoystickSettings.Current;

        public static bool CanSwitch => _switcher != null || _portalSwitcher != null;
        public static bool HasMfds => _mfdManagers.Length > 0 || _portalManagers.Length > 0;

        public static void Find(GameObject vehicle, Vector3 headWorld)
        {
            ReleaseAll();
            _portalSwitcher = Nearest(vehicle.GetComponentsInChildren<MultiPortalSOISwitcher>(true), headWorld);
            _switcher = _portalSwitcher == null ? Nearest(vehicle.GetComponentsInChildren<ThrottleSOISwitcher>(true), headWorld) : null;
            _mfdManagers = vehicle.GetComponentsInChildren<MFDManager>(true);
            _portalManagers = vehicle.GetComponentsInChildren<MFDPortalManager>(true);
            _tgps = vehicle.GetComponentsInChildren<TargetingMFDPage>(true);
            _radars = vehicle.GetComponentsInChildren<MFDRadarUI>(true);
            _maps = vehicle.GetComponentsInChildren<DashMapDisplay>(true);
            _tsds = vehicle.GetComponentsInChildren<MFDPTacticalSituationDisplay>(true);
        }

        public static void Clear()
        {
            ReleaseAll();
            _switcher = null;
            _portalSwitcher = null;
            _mfdManagers = new MFDManager[0];
            _portalManagers = new MFDPortalManager[0];
            _tgps = new TargetingMFDPage[0];
            _radars = new MFDRadarUI[0];
            _maps = new DashMapDisplay[0];
            _tsds = new MFDPTacticalSituationDisplay[0];
        }

        public static void Update()
        {
            // Switch SOI: one step per key press, wrapping around at either end.
            if (KeyActions.Pressed(S.soiNextKey)) CycleSoi(+1);
            if (KeyActions.Pressed(S.soiPrevKey)) CycleSoi(-1);

            // Slew: full deflection while held.
            Vector3 slew = new Vector3((KeyActions.Held(S.soiSlewRightKey) ? 1f : 0f) - (KeyActions.Held(S.soiSlewLeftKey) ? 1f : 0f),
                                       (KeyActions.Held(S.soiSlewUpKey) ? 1f : 0f) - (KeyActions.Held(S.soiSlewDownKey) ? 1f : 0f), 0f);
            if (slew != Vector3.zero)
            {
                _slewActive = true;
                InputAxis(slew);
            }
            else if (_slewActive)
                ReleaseSlew();

            // Select: like pressing the thumbstick on the SOI.
            bool select = KeyActions.Held(S.soiSelectKey);
            if (select && !_selectHeld)
                InputButtonDown();
            if (select)
                InputButton();
            if (!select && _selectHeld)
                InputButtonUp();
            _selectHeld = select;

            if (KeyActions.Pressed(S.soiZoomInKey)) Zoom(+1);
            if (KeyActions.Pressed(S.soiZoomOutKey)) Zoom(-1);
            UpdateTgpZoomCycle();
        }

        // Tap the zoom cycle key: one step (on release, so a hold never zooms in first). Hold it for
        // tgpZoomResetHoldSeconds: straight back to the widest zoom.
        private static float _zoomCycleDownAt = -1f;
        private static bool _zoomCycleConsumed;

        private static void UpdateTgpZoomCycle()
        {
            float hold = S.tgpZoomResetHoldSeconds;
            if (hold <= 0f)
            {
                if (KeyActions.Pressed(S.tgpZoomCycleKey)) CycleTgpZoom();
                _zoomCycleDownAt = -1f;
                return;
            }
            bool held = KeyActions.Held(S.tgpZoomCycleKey);
            if (held && _zoomCycleDownAt < 0f)
            {
                _zoomCycleDownAt = Time.unscaledTime;
                _zoomCycleConsumed = false;
            }
            else if (held && !_zoomCycleConsumed && Time.unscaledTime - _zoomCycleDownAt >= hold)
            {
                _zoomCycleConsumed = true;
                ResetTgpZoom();
            }
            else if (!held && _zoomCycleDownAt >= 0f)
            {
                if (!_zoomCycleConsumed) CycleTgpZoom();
                _zoomCycleDownAt = -1f;
            }
        }

        public static bool HasTgp => _tgps.Length > 0;

        // LMB outside SOI cursor mode: the same thumbstick press as the select key, on whichever page is the SOI.
        private static bool _lmbSelectHeld;

        public static void LmbSelect(bool held)
        {
            if (held && !_lmbSelectHeld)
                InputButtonDown();
            if (held)
                InputButton();
            if (!held && _lmbSelectHeld)
                InputButtonUp();
            _lmbSelectHeld = held;
        }

        // LMB in a head mode: the SOI TGP in HEAD mode gets a thumbstick press (lock where you look); the SOI radar in
        // head boresight gets its head button (BORE: back out of head mode). A press that started in a head mode is
        // kept until LMB is released, so it never turns into a different action halfway through.
        // Returns true while LMB belongs to a head mode (callers then treat LMB as not pressed).
        private static bool _headLmb;
        private static TargetingMFDPage _headTgp;

        // An LMB press currently belongs to a head mode.
        public static bool HeadLmbActive => _headLmb;

        // The SOI is a TGP in HEAD mode or a radar in head boresight.
        public static bool InHeadMode
        {
            get
            {
                foreach (var t in _tgps)
                    if (t != null && t.isSOI && t.powered && t.tgpMode == TargetingMFDPage.TGPModes.HEAD)
                        return true;
                foreach (var r in _radars)
                    if (r != null && r.isSOI && r.radarCtrlr != null && r.radarCtrlr.boresightHead)
                        return true;
                return false;
            }
        }

        public static bool HeadModeLmb(bool lmb)
        {
            if (!lmb)
            {
                if (_headLmb && _headTgp != null)
                    Guard(() => _headTgp.OnThumbstickUp());
                _headLmb = false;
                _headTgp = null;
                return false;
            }
            if (_headLmb)
                return true;
            if (!KeyActions.Pressed("Mouse0"))
                return false; // held since before a head mode began: leave it as it was

            foreach (var t in _tgps)
                if (t != null && t.isSOI && t.powered && t.tgpMode == TargetingMFDPage.TGPModes.HEAD)
                {
                    _headLmb = true;
                    _headTgp = t;
                    Guard(t.OnThumbstickDown);
                    return true;
                }
            foreach (var r in _radars)
                if (r != null && r.isSOI && r.radarCtrlr != null && r.radarCtrlr.boresightHead)
                {
                    _headLmb = true;
                    Guard(r.ToggleHeadBoresight);
                    return true;
                }
            return false;
        }

        private static void Guard(System.Action a)
        {
            try { a(); }
            catch (System.Exception e) { Log.Warn("Head mode LMB threw: " + e.Message); }
        }

        // One step more TGP zoom, back to the widest after the narrowest. Works in any TGP mode (HEAD included) and
        // whether or not the TGP is the SOI: the SOI TGP if there is one, else the first powered one.
        private static void CycleTgpZoom()
        {
            var tgp = ZoomTgp();
            if (tgp == null)
                return;
            if (tgp.fovIdx < tgp.fovs.Length - 1)
                tgp.ZoomIn(); // also syncs to the other seat and plays the zoom sound
            else
                SetWidest(tgp);
        }

        // Straight back to the widest TGP zoom (1x), on the same TGP the cycle uses.
        private static void ResetTgpZoom()
        {
            var tgp = ZoomTgp();
            if (tgp != null && tgp.fovIdx != 0)
                SetWidest(tgp);
        }

        // The SOI TGP if there is one, else the first powered one; null if none has zoom levels.
        private static TargetingMFDPage ZoomTgp()
        {
            TargetingMFDPage tgp = null;
            foreach (var t in _tgps)
                if (t != null && t.isSOI) { tgp = t; break; }
            if (tgp == null)
                foreach (var t in _tgps)
                    if (t != null && t.powered) { tgp = t; break; }
            return tgp == null || tgp.fovs == null || tgp.fovs.Length == 0 ? null : tgp;
        }

        private static void SetWidest(TargetingMFDPage tgp)
        {
            if (!tgp.powered)
                return;
            tgp.RemoteSetFovIdx(0);
            tgp.OnSetFovIdx?.Invoke(0);
        }

        public static void ReleaseAll()
        {
            HeadModeLmb(false);
            LmbSelect(false);
            if (_slewActive)
                ReleaseSlew();
            if (_selectHeld)
            {
                _selectHeld = false;
                InputButtonUp();
            }
        }

        // ------------------------------------------------------------------

        // The game's classic switcher stops at the first / last screen instead of wrapping (and its multi-portal one
        // lands oddly when nothing is SOI yet), so the candidate pages are cycled here, in the switcher's own order:
        // MFDs in list order, portal quarters left to right.
        private static void CycleSoi(int dir)
        {
            if (_portalSwitcher != null && _portalSwitcher.portalManagers != null)
                CyclePortal(_portalSwitcher.portalManagers, dir);
            else if (_switcher != null && _switcher.mfdManager != null)
                CycleMfd(_switcher.mfdManager, dir);
            else if (_switcher != null && _switcher.mfdpManager != null)
                CyclePortal(new[] { _switcher.mfdpManager }, dir);
        }

        private static void CycleMfd(MFDManager manager, int dir)
        {
            var pages = new List<MFDPage>();
            int current = -1;
            foreach (var mfd in manager.mfds)
            {
                var p = mfd != null ? mfd.activePage : null;
                if (p == null || !p.canSOI)
                    continue;
                if (p.isSOI)
                    current = pages.Count;
                pages.Add(p);
            }
            int next = Step(current, pages.Count, dir);
            if (next < 0 || next == current)
                return;
            pages[next].ToggleInput(); // takes SOI off the others on this manager
            if (current >= 0 && pages[current].isSOI)
                pages[current].ToggleInput();
            if (_switcher.inputAudioSource != null && _switcher.switchedClip != null)
                _switcher.inputAudioSource.PlayOneShot(_switcher.switchedClip);
        }

        private static void CyclePortal(MFDPortalManager[] managers, int dir)
        {
            var pages = new List<MFDPortalPage>();
            int current = -1;
            foreach (var m in managers)
            {
                if (m == null)
                    continue;
                foreach (var q in new[] { m.halfLeft.quarterLeft, m.halfLeft.quarterRight, m.halfRight.quarterLeft, m.halfRight.quarterRight })
                {
                    var p = q != null ? q.displayedPage : null;
                    if (p == null || !p.canSOI || p.pageState == MFDPortalPage.PageStates.Minimized || p.pageState == MFDPortalPage.PageStates.SubSized)
                        continue;
                    if (p.isSOI)
                        current = pages.Count;
                    pages.Add(p);
                }
            }
            int next = Step(current, pages.Count, dir);
            if (next < 0 || next == current)
                return;
            pages[next].ToggleInput(); // clears SOI on its own manager and plays the input sound
            if (current >= 0 && pages[current].isSOI)
                pages[current].ToggleInput(); // the old one was on another portal manager
        }

        // Index of the next page in direction dir, wrapping; with no SOI yet, next starts at the first page and
        // previous at the last. -1 if there are no pages.
        private static int Step(int current, int count, int dir)
        {
            if (count == 0)
                return -1;
            if (current < 0)
                return dir > 0 ? 0 : count - 1;
            return ((current + dir) % count + count) % count;
        }

        private static void ReleaseSlew()
        {
            _slewActive = false;
            InputAxis(Vector3.zero);
            if (_portalManagers.Length > 0)
                foreach (var m in _portalManagers) m.OnInputAxisReleased();
            else
                foreach (var m in _mfdManagers) m.OnInputAxisReleased();
        }

        // Portal aircraft go through their portal managers, classic ones through the MFD manager (as BYOJ does);
        // each manager hands the input to whichever of its pages is the SOI.
        private static void InputAxis(Vector3 v)
        {
            if (_portalManagers.Length > 0)
                foreach (var m in _portalManagers) m.OnInputAxis(v);
            else
                foreach (var m in _mfdManagers) m.OnInputAxis(v);
        }

        private static void InputButtonDown()
        {
            if (_portalManagers.Length > 0)
                foreach (var m in _portalManagers) m.OnInputButtonDown();
            else
                foreach (var m in _mfdManagers) m.OnInputButtonDown();
        }

        private static void InputButton()
        {
            if (_portalManagers.Length > 0)
                foreach (var m in _portalManagers) m.OnInputButton();
            else
                foreach (var m in _mfdManagers) m.OnInputButton();
        }

        private static void InputButtonUp()
        {
            if (_portalManagers.Length > 0)
                foreach (var m in _portalManagers) m.OnInputButtonUp();
            else
                foreach (var m in _mfdManagers) m.OnInputButtonUp();
        }

        // +1 = zoom in, -1 = out, on whichever page is the SOI.
        private static void Zoom(int dir)
        {
            foreach (var t in _tgps)
                if (t != null && t.isSOI) { if (dir > 0) t.ZoomIn(); else t.ZoomOut(); return; }
            foreach (var r in _radars)
                if (r != null && r.isSOI) { if (dir > 0) r.RangeDown(); else r.RangeUp(); return; }
            foreach (var m in _maps)
                if (m != null && ((m.mfdPage != null && m.mfdPage.isSOI) || (m.portalPage != null && m.portalPage.isSOI)))
                { if (dir > 0) m.ZoomIn(); else m.ZoomOut(); return; }
            foreach (var t in _tsds)
                if (t != null && t.isSOI) { if (dir > 0) t.PrevViewScale(); else t.NextViewScale(); return; }
        }

        private static T Nearest<T>(T[] all, Vector3 head) where T : Component
        {
            T best = null;
            float bestD = float.MaxValue;
            foreach (var c in all)
            {
                if (c == null)
                    continue;
                float d = (c.transform.position - head).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = c;
                }
            }
            return best;
        }
    }
}
