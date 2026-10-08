# KBM SLOP (VTOL VR)

A virtual joystick for flat-screen VTOL VR. Tap **C** to switch it on (or hold **Left Alt**
for as long as you need it): the mouse then drives the aircraft's stick, and a box in the middle
of the screen shows where the stick is. Tap C again to get your normal FlatScreen 3 cursor back.

Built for the new VTOL VR Mod Loader (Steam app "VTOL VR Mod Loader"). Works alongside
**FlatScreen 3** and **BYOJoystick / BYOJoystick Extended**. It doesn't need VTOLAPI.

## Controls (while active)

| Input | Action |
|---|---|
| Tap C | Toggle stick control on/off (only a clean tap counts: pressing another key while C is down cancels it). A second toggle key can be set in F8 → Keybinds |
| Hold Left Alt | Flips stick control while held. **Stick off:** the virtual joystick is on until you let go. **Stick on:** clickable mode — the mouse stops flying, the cursor comes back and clicks cockpit controls (FlatScreen 3); LMB clicks instead of pressing the thumbstick; WASD and Q/E still fly; the overlay shrinks to a small dot on a faint line (opacity: `clickModeOpacity`). Key: `clickModeKey`. |
| Mouse move | Moves the stick. It stays where you leave it, like a real stick with no spring. |
| Middle mouse | Re-centre the stick (on release); hold (1 s by default, `middleHoldSeconds`) to re-centre the view (`middleHoldRecentersView`) |
| Hold RMB | Free look (FlatScreen 3 camera). The stick is held still while RMB is down. |
| LMB | Thumbstick press on the SOI page (radar lock, TGP lock, select). NAV map in SOI cursor mode: GPS send. Radar in head boresight: BORE (leaves head mode). Fire weapons with Space (`triggerKey`); the overlay dot turns red while it's held. Clickable mode / stick off: clicks the cockpit |
| W / S | Stick forward (nose down) / back (nose up), springs back when released |
| A / D | Roll left / right, springs back when released |
| Q / E | Rudder left / right |
| T / hold Mouse button 4 | T toggles SOI cursor mode; holding Mouse button 4 (side "back" button) flips it while held. Works with the virtual joystick on or off (turning the stick on/off doesn't cancel it; Esc does). SOI cursor mode: the mouse controls whichever MFD page is the SOI. **TGP:** FPS-style pod aiming, scaled by zoom; the pod locks into TGT mode when you start moving and re-locks when the mouse stops or SOI mode ends. **Radar / ARAD / map / TSD / other pages:** the mouse moves the page's cursor (or pans the map); when the mouse stops, the page gets "thumbstick released" so the cursor snaps to contacts. **Middle mouse:** the page's re-centre: TGP → FWD, map → reset onto the aircraft, TSD → centre on the aircraft, radar → drop lock, ARAD → deselect. **Scroll:** the page's own zoom/range buttons (TGP zoom, radar range, map zoom, TSD scale). **LMB:** thumbstick press (lock/select) on every page except the NAV map, where it sends a GPS point at the map cursor. **Head modes** (also outside SOI cursor mode): TGP in HEAD mode → LMB is the thumbstick press (lock where you look), and the mouse doesn't move the pod; radar in head boresight → LMB is the head button (leaves head mode). **TSD:** LMB click = thumbstick press (select/deselect); LMB held + mouse move = drag the view around like a finger on the touchscreen. The stick holds its position and WASD / Q/E still fly. Keys: `tgpModeKey` (toggle), `soiHoldKey` (hold); sensitivities: `tgpSensitivity`, `cursorSensitivity` (F8). |
| Hold R | Weapon wheel (`weaponWheel`, hold time `weaponWheelHoldSeconds`, 0.4 s by default, 0 to 1 s; works with the stick on or off): lists the aircraft's weapons and their counts right of the stick overlay, named as on the HUD; the selected one is framed, empty ones are struck through. The mouse wheel selects (empty weapons too, as the weapon cycle does) and does nothing else while the list is open. EF-24: weapons of the other arming mode are listed too and picking one switches AA / AG; in EW master mode R keeps its normal job. A tap of R still cycles weapons, sent when the key is released; with a hold time of 0 the list opens at once and R no longer cycles. Setting: F8 → "Hold "Cycle weapons" to open Weapon Manager". Key: `weaponCycleKey`. |
| F8 | Settings window (works with the mode on or off) |
| Esc | Also switches the mode off, so FlatScreen 3's menus get a free cursor |

While it's active the cursor is hidden and held inside the game window, and FlatScreen 3's
hover/click on cockpit controls is paused, so flying doesn't flip switches. Mouse-wheel zoom
still works. Mouse movement is read through Windows (cursor position, re-centred every frame),
because the first version (Unity cursor lock + Unity mouse axes) didn't move the stick in game.

**Pitch direction:** by default, mouse up = stick forward = nose down, the same as a real
stick (the dot goes up in the box). If you want mouse up = nose up, set `"invertPitch": true`.

## Cockpit screens

Whenever you have a normal cursor (stick off, or holding Left Alt), this mod handles the sensor
(touchscreen) displays instead of FlatScreen 3: the portal MFDs of the F-45, EF-24 and similar, with
their on-screen buttons, layout presets and touch drag (TSD, map). Everything else stays with
FlatScreen 3: physical knobs, switches, levers, and the bezel buttons of classic MFDs (F/A-26B,
T-55, ...). Jets without touchscreen displays are left entirely to FlatScreen 3.

FlatScreen 3 tests a sphere around each element's pivot point, and UI pivots are often at a corner
or edge, so its hitboxes miss the drawn button. Here the mouse ray is tested against the element's
real rectangle (the same `useRect` / `useRectTransform` hitbox the game uses for a VR finger), and
the hitbox under the cursor is outlined in HUD green, with its labels turned the same green. Clicking
presses the element the way a finger does; holding LMB on a touchscreen and moving drags it. Screen
handling can be switched off under F8 → Cockpit screens (`handleScreens`).

## Key bindings (F8 → Bindings)

A short, fixed set of keyboard bindings that apply to every aircraft:

- **STICK (right controller)**: stick movement (W/S/A/D, Q/E), trigger, A (menu / weapon cycle),
  B (second button), thumbstick left/right/up/down/press.
- **THROTTLE (left controller)**: throttle up/down (Left Shift / Left Ctrl, speed slider), full
  throttle, MIL power (just below the afterburner detent), zero throttle, trigger (`B`, with a 0–0.8 s ramp-up, default 0.2 s),
  menu button, thumbstick left/right/up/down/press.
- **ENGINE**: engine 1 (left / only), engine 2 (right), APU, main battery (on/off/toggle each). Switches
  under a cover get the cover lifted first.
- **AIRCRAFT**: canopy (open/close/toggle), parking brake / brake lock (on/off/toggle; toggle on `H`),
  wheel brakes (hold), airbrake / speed brake (hold, toggle), flaps (down/up one step, cycle), landing gear
  (up/down/toggle), wing sweep, launch bar and arrestor hook (extend/retract/toggle).
- **COMBAT**: countermeasures (hold, `X`; no helicopter combo needed), radar power (on/off/toggle), RWR (on/mute/off/cycle), master arm (on/off/toggle; on
  lifts the switch cover), arming mode AA / AG / toggle (EF-24, the only aircraft with one), TGP zoom
  cycle (default `~`; works in every TGP mode including HEAD, and when the TGP isn't the SOI).
- **PILOT**: helmet visor and night vision (on/off/toggle).

Aircraft controls are found by the game's own control names (e.g. "Landing Gear"); a card says
"not in this aircraft" when the current jet doesn't have one. Opening an aircraft card marks that
control in the cockpit with a green ring, joined to its card by a line. Keys used in more than one place show in yellow.

## Settings

Press **F8** in game for a settings window with sliders for control-area size, deadzone,
sensitivity, centre curve, auto-centre, WASD/rudder speed and overlay opacity. While it's open the control
area is shown as a live preview, and the virtual joystick is paused so you can use the cursor.

All keys can be rebound in the window's **Keybinds** section: click a binding, press the new key
(Esc cancels, Delete/Backspace clears it).

Changes apply immediately and are saved (settings and key bindings) to
`%USERPROFILE%\AppData\LocalLow\Boundless Dynamics, LLC\VTOLVR\KBMSlop\settings.json`, while the window
is open and when it closes. That folder is outside the mod's own, so updating the mod keeps them. To
start over, use **Reset settings to defaults** or delete the file with the game closed. The file can be
edited by hand with the game closed; missing entries take their defaults. If it can't be read, the mod
starts from the defaults and keeps a copy as `settings.json.bad`.

The defaults are the field initializers in `src/VirtualJoystickSettings.cs` (slider ranges are in
`src/SettingsWindow.cs`).

| Setting | Default | Meaning |
|---|---|---|
| `enableOnSpawn` | `false` | Switch stick control on automatically when you enter the cockpit (once per spawn; tapping C off keeps it off until the next spawn) |
| `toggleKey` / `toggleKey2` | `"C"` / `"None"` | Keys that toggle stick control (either works) |
| `clickModeKey` | `"LeftAlt"` | Flips stick control while held (stick on while held, or clickable mode if it's already on) |
| `tgpModeKey` / `soiHoldKey` | `"T"` / `"Mouse3"` | SOI cursor mode: toggle / flip while held (`Mouse3` = mouse button 4) |
| `toggleMode` | `"Tap"` | `"Tap"` toggles; `"Hold"` = active only while a toggle key is held |
| `tapMaxSeconds` | `0.4` | A toggle-key press held longer than this doesn't toggle |
| `sensitivity` | `1.0` | At 1.0 the dot follows the mouse pixel for pixel: half the control area of travel = full deflection |
| `invertPitch` | `false` | Mouse up = nose up when `true` |
| `deadzone` | `0.05` | Radius of the round centre deadzone (fraction of full deflection), shown as a shaded circle |
| `curve` | `0` | Inverse cubic centre curve, 0–1: blends linear with `1-(1-x)³`, so sensitivity is strongest near centre (3× at 1) and flattens toward the edge. Full deflection is still reached at the edge |
| `autoCenterRate` | `0` | Spring back to centre when the mouse is still (deflections per second); 0 = off |
| `circularLimit` | `false` | Round travel limit instead of square |
| `rudderLeftKey` / `rudderRightKey` | `"Q"` / `"E"` | Unity `KeyCode` names; `""` disables |
| `rudderRate` | `4` | How fast rudder ramps in/out |
| `pitchDownKey` / `pitchUpKey` / `rollLeftKey` / `rollRightKey` | `W` / `S` / `A` / `D` | Keyboard stick, added on top of the mouse. Also flies with the virtual joystick off (only while a key is held or springing back, so a real joystick still works otherwise) |
| `keyboardRatePitch` / `keyboardRateRoll` | `3` / `3` | How fast W/S (pitch) and A/D (roll) ramp in while held (per second) |
| `keyboardReturnRatePitch` / `keyboardReturnRateRoll` | `4.8` / `4.8` | How fast W/S (pitch) and A/D (roll) input return to centre after release (per second) |
| `keyboardReturnPitch` / `keyboardReturnRoll` | `true` / `true` | Off: that axis stays where the keys left it instead of returning to centre |
| `menuKey` | `"F8"` | Opens the settings window |
| `middleMouseRecenters` | `true` | |
| `middleHoldRecentersView` | `true` | Holding middle mouse for `middleHoldSeconds` re-centres the view (FlatScreen 3's camera reset, or the game's VR re-centre without it) |
| `middleHoldSeconds` | `1` | How long middle mouse must be held to re-centre the view (0.2 - 2 s) |
| `recenterOnEnable` | `true` | Stick starts centred each time you switch on |
| `releaseKeys` | `["Escape"]` | Keys that also switch the mode off |
| `overlaySize` | `460` | Side of the square control area in px. Bigger = more mouse travel for full deflection |
| `overlayOpacity` | `0.85` | Overlay opacity |

## How it works

- The flight-control value is injected with a Harmony prefix on `VehicleInputManager.Update`, right
  before the game sends it to the control surfaces. Every active `VRJoystick` and BYOJoystick also
  write that value every frame, in no fixed order (the F/A-26B has a side and a centre stick, both
  active). Writing at the point where it's used is the only place that reliably wins.
  When you switch the mode off, the patch stops overriding and BYOJ / your HOTAS take over again.
- The cockpit stick models are moved to match (`RemoteSetStick`).
- If the game's **hardware rudder** option is on, the game takes yaw from your rudder axis and the
  Q/E rudder keys have no effect.
- The FlatScreen 3 hookup is a runtime Harmony prefix on `FlatScreen3MonoBehaviour.GetHoveredObject`,
  found by name. If FlatScreen 3 isn't loaded, nothing is patched.

## Build

Requires the .NET SDK (any recent version; it targets net472 through reference-assembly packages).

```
dotnet build -c Release
```

The build copies `VirtualJoystick.dll` and `item.json` to
`<VTOL VR>\@Mod Loader\Mods\VirtualJoystick\`, where the mod loader picks up local mods. If the
game isn't installed in the default Steam library, pass `-p:GameDir="X:\path\to\VTOL VR"`.
To build without copying, pass `-p:DeployToGame=false`.

## Known limitations

- Multicrew in multiplayer (two humans in one jet): this doesn't do BYOJ's stick-grab sync, so the
  other seat may not see your inputs. Single-seat multiplayer and singleplayer are fine.
- Throttle stays on whatever you use now (BYOJ keys / HOTAS / FlatScreen 3 scroll on the throttle).
