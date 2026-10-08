using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VirtualJoystick
{
    // Replaces FlatScreen 3's handling of everything drawn on cockpit screens (MFD / portal on-screen buttons and
    // touchscreen drag surfaces, e.g. the F-45's displays). Physical knobs, switches and buttons stay with FlatScreen 3.
    //
    // Why: with its default "use sphere" setting FlatScreen 3 tests a sphere around each interactable's pivot. UI
    // elements usually have their pivot at a corner or edge, so the hitbox sits off the drawn button. In VR the game
    // uses the element's rect (useRect / useRectTransform), so here the mouse ray is tested against that exact
    // rectangle, the hovered hitbox is outlined, and presses / drags go to the right element.
    //
    // FlatScreen 3 never sees these elements: they're filtered out of its candidate list (FlatScreenCompat).
    internal static class ScreenPointer
    {
        private const float RefreshSeconds = 1f;
        // Hits within this distance of the nearest one count as the same screen layer; the smallest element wins.
        // Generous on purpose: canvases layer page backgrounds, frames and buttons a few millimetres apart.
        private const float SameSurfaceMeters = 0.02f;
        // Point-type screen buttons with a tiny or zero radius still get a usable hitbox.
        private const float MinSphereRadius = 0.006f;

        // FlatScreen 3 is looked up lazily and retried: this mod loads before it, so a one-time lookup at startup
        // finds nothing and would leave screen handling off for the whole session.
        private static PropertyInfo FsInstance, FsEnabled;
        private static FieldInfo FsCamera;
        private static float _nextResolve;

        private static bool ResolveFlatScreen()
        {
            if (FsCamera != null && FsInstance != null)
                return true;
            if (Time.unscaledTime < _nextResolve)
                return false;
            _nextResolve = Time.unscaledTime + 2f;
            Type type = AccessTools.TypeByName("Triquetra.FlatScreen3.FlatScreen3MonoBehaviour");
            if (type == null)
                return false;
            FsInstance = AccessTools.Property(type, "instance");
            FsEnabled = AccessTools.Property(type, "flatScreenEnabled");
            FsCamera = AccessTools.Field(type, "cameraEyeGameObject");
            if (FsInstance == null || FsCamera == null)
            {
                Log.Warn("FlatScreen 3 found but its camera/instance members are missing; cockpit screen handling disabled");
                _nextResolve = float.MaxValue;
                return false;
            }
            Log.Info("Cockpit screen handling ready (FlatScreen 3 found)");
            return true;
        }

        private static readonly FieldInfo TouchBeginField = AccessTools.Field(typeof(VRTouchScreenInteractable), "OnBeginTouch");
        private static readonly FieldInfo TouchingField = AccessTools.Field(typeof(VRTouchScreenInteractable), "OnTouching");
        private static readonly MethodInfo SetIsTouching = AccessTools.PropertySetter(typeof(VRTouchScreenInteractable), "isTouching");
        private static readonly MethodInfo SetTouchingLocalPoint = AccessTools.PropertySetter(typeof(VRTouchScreenInteractable), "touchingLocalPoint");

        private static readonly List<VRInteractable> Buttons = new List<VRInteractable>();
        private static readonly List<VRTouchScreenInteractable> Touches = new List<VRTouchScreenInteractable>();
        // Every interactable this class handles; FlatScreen 3's list is filtered against it.
        private static readonly HashSet<VRInteractable> Owned = new HashSet<VRInteractable>();

        private static GameObject _vehicle;
        private static float _nextRefresh;

        private static VRInteractable _hoverButton;
        private static VRTouchScreenInteractable _hoverTouch;
        private static readonly Vector3[] _hoverCorners = new Vector3[4]; // hovered hitbox outline, in _outlineTf's space

        private static VRInteractable _pressed;
        private static VRTouchScreenInteractable _touching;

        public static bool Available => ResolveFlatScreen();

        // FlatScreen 3's eye camera (what the player sees), or null.
        public static Camera Camera => ResolveFlatScreen() ? GetCamera() : null;

        // World point -> GUI coordinates (y down) through that camera; false if behind it.
        public static bool WorldToGui(Vector3 world, out Vector2 gui)
        {
            gui = default;
            Camera cam = Camera;
            if (cam == null)
                return false;
            Vector3 sp = WorldToScreen(cam, world);
            if (sp.z <= 0f)
                return false;
            gui = new Vector2(sp.x, Screen.height - sp.y);
            return true;
        }

        // The mouse is over a screen element this class handles (FlatScreen 3 should do nothing this frame).
        public static bool HasHover => _hoverButton != null || _hoverTouch != null || _pressed != null || _touching != null;

        // Screen handling switched on in settings. Off = FlatScreen 3 keeps everything.
        private static bool _handling = true;

        // Bumped whenever the owned set or _handling changes, so FlatScreen 3's filtered list gets rebuilt.
        public static int Version { get; private set; }

        public static bool IsOwned(VRInteractable v) => _handling && v != null && Owned.Contains(v);

        // Called every frame from the behaviour. handling = screen handling on in settings; freeCursor = the free
        // cursor is in use (stick not flying, no menu, no SOI mode).
        public static void Update(bool handling, bool freeCursor, GameObject vehicle)
        {
            if (!Available)
                return;
            if (handling != _handling)
            {
                _handling = handling;
                Version++;
            }
            bool enabled = handling && freeCursor;

            if (vehicle != _vehicle || Time.unscaledTime >= _nextRefresh)
                Refresh(vehicle);

            Camera cam = GetCamera();
            if (!enabled || cam == null || _vehicle == null)
            {
                ClearHover();
                EndPress();
                EndTouch();
                return;
            }

            Ray ray = MouseRay(cam);

            if (_touching != null)
            {
                UpdateTouch(ray);
                return;
            }
            if (_pressed != null)
            {
                if (!Input.GetMouseButton(0))
                    EndPress();
                return;
            }

            FindHover(ray);

            if (Input.GetMouseButtonDown(0))
            {
                if (_hoverButton != null)
                    BeginPress(_hoverButton);
                else if (_hoverTouch != null)
                    BeginTouch(_hoverTouch, ray);
            }
        }

        public static void Reset()
        {
            ClearHover();
            EndPress();
            EndTouch();
            Buttons.Clear();
            Touches.Clear();
            Owned.Clear();
            _vehicle = null;
            Version++;
        }

        // Screen-space corners of the hovered hitbox (GUI coordinates, y down), for the outline.
        public static bool TryGetHoverOutline(Vector2[] corners)
        {
            if ((_hoverButton == null && _hoverTouch == null) || _outlineTf == null)
                return false;
            Camera cam = GetCamera();
            if (cam == null)
                return false;
            for (int i = 0; i < 4; i++)
            {
                Vector3 sp = WorldToScreen(cam, _outlineTf.TransformPoint(_hoverCorners[i]));
                if (sp.z <= 0f)
                    return false;
                corners[i] = new Vector2(sp.x, Screen.height - sp.y);
            }
            return true;
        }

        public static bool HoverIsTouchSurface => _hoverButton == null && _hoverTouch != null;

        // ------------------------------------------------------------------ candidates

        private static void Refresh(GameObject vehicle)
        {
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            if (vehicle != _vehicle)
            {
                EndPress();
                EndTouch();
                ClearHover();
            }
            int ownedBefore = Owned.Count;
            _vehicle = vehicle;
            Buttons.Clear();
            Touches.Clear();
            Owned.Clear();
            if (vehicle == null)
            {
                if (ownedBefore != 0)
                    Version++;
                return;
            }

            // Only sensor (touchscreen) displays are handled here: the portal MFDs of the F-45, EF-24 and similar.
            // Every other control, physical switches, knobs, levers and classic MFD bezel buttons included, stays with
            // FlatScreen 3. A jet without portal displays is left entirely to FlatScreen 3.
            // Strictly the portal hierarchy (not "same canvas": some cockpits put physical controls under canvases too).
            var portals = vehicle.GetComponentsInChildren<MFDPortalManager>(true);
            // Layout preset buttons belong to the portals even where they sit outside the portal hierarchy.
            var presets = new HashSet<VRInteractable>();
            foreach (var p in vehicle.GetComponentsInChildren<MFDPortalPresetButton>(true))
                if (p.interactable != null)
                    presets.Add(p.interactable);

            bool OnSensorScreen(Component c) => c.GetComponentInParent<MFDPortalManager>(true) != null;

            Visuals.Clear();
            if (portals.Length > 0)
            {
                foreach (var t in vehicle.GetComponentsInChildren<VRTouchScreenInteractable>(true))
                {
                    if (t.screenRect == null || !OnSensorScreen(t))
                        continue;
                    Touches.Add(t);
                    var vi = t.GetComponent<VRInteractable>();
                    if (vi != null)
                        Owned.Add(vi);
                }
                foreach (var v in vehicle.GetComponentsInChildren<VRInteractable>(true))
                {
                    if (Owned.Contains(v) || !(presets.Contains(v) || OnSensorScreen(v)))
                        continue;
                    Buttons.Add(v);
                    Owned.Add(v);
                    Visuals[v] = CollectVisuals(v);
                }
            }

            // Pages come and go (sub-pages, popups); a changed count means FlatScreen 3's filter needs redoing.
            if (Owned.Count != ownedBefore)
            {
                Version++;
                Log.Info($"Sensor screens: {portals.Length} portal display(s), {Buttons.Count} on-screen element(s), " +
                         $"{Touches.Count} touch surface(s) handled here; everything else is FlatScreen 3's");
            }
        }

        // One ray hit on a screen element: distance along the ray, world area (for "smaller wins"), world outline.
        private struct Hit
        {
            public VRInteractable Button;
            public float Dist;
            public float Area;
            public Vector3 C0, C1, C2, C3;
            public Transform Tf; // the hitbox's plane; the outline is stored relative to it
            public string Shape;
        }

        private static readonly List<Hit> Hits = new List<Hit>();

        // What each element draws: its own text labels and other graphics (not ones belonging to a nested element).
        private class ElementVisuals
        {
            public readonly List<UnityEngine.UI.Text> Texts = new List<UnityEngine.UI.Text>();
            public readonly List<TMPro.TMP_Text> Tmps = new List<TMPro.TMP_Text>();
            public readonly List<UnityEngine.UI.Graphic> Images = new List<UnityEngine.UI.Graphic>();

            // Cached drawn hitbox, in Plane's local space (so it follows the element if the page moves).
            public bool Cached;
            public string Kind;          // "text", "icon", or null (nothing drawn: fall back to rect/bounds/sphere)
            public Transform Plane;
            public Rect Rect;
            public float CachedAt;
            public string[] TextSnapshot; // label strings the cache was built from
        }

        // Recompute a cached hitbox at least this often (layout changes that don't change the text).
        private const float HitboxCacheSeconds = 0.5f;

        // The element's drawn hitbox (label, else icon), cached until a label's text changes or the cache ages out.
        private static string DrawnHitbox(ElementVisuals vis, out Transform plane, out Rect rect)
        {
            // A cached label that has since been hidden or destroyed means the cache is stale.
            bool planeGone = vis.Cached && vis.Kind != null && (vis.Plane == null || !vis.Plane.gameObject.activeInHierarchy);
            if (!vis.Cached || planeGone || Time.unscaledTime - vis.CachedAt > HitboxCacheSeconds || TextsChanged(vis))
            {
                vis.Cached = true;
                vis.CachedAt = Time.unscaledTime;
                vis.TextSnapshot = SnapshotTexts(vis);
                if (LabelHitbox(vis, out vis.Plane, out vis.Rect))
                    vis.Kind = "text";
                else if (IconHitbox(vis, out vis.Plane, out vis.Rect))
                    vis.Kind = "icon";
                else
                    vis.Kind = null;
            }
            plane = vis.Plane;
            rect = vis.Rect;
            return vis.Kind;
        }

        private static string[] SnapshotTexts(ElementVisuals vis)
        {
            var s = new string[vis.Texts.Count + vis.Tmps.Count];
            for (int i = 0; i < vis.Texts.Count; i++)
                s[i] = vis.Texts[i] != null && vis.Texts[i].isActiveAndEnabled ? vis.Texts[i].text : null;
            for (int i = 0; i < vis.Tmps.Count; i++)
                s[vis.Texts.Count + i] = vis.Tmps[i] != null && vis.Tmps[i].isActiveAndEnabled ? vis.Tmps[i].text : null;
            return s;
        }

        private static bool TextsChanged(ElementVisuals vis)
        {
            string[] old = vis.TextSnapshot;
            if (old == null || old.Length != vis.Texts.Count + vis.Tmps.Count)
                return true;
            for (int i = 0; i < vis.Texts.Count; i++)
            {
                var t = vis.Texts[i];
                string now = t != null && t.isActiveAndEnabled ? t.text : null;
                if (!ReferenceEquals(now, old[i]) && now != old[i])
                    return true;
            }
            for (int i = 0; i < vis.Tmps.Count; i++)
            {
                var t = vis.Tmps[i];
                string now = t != null && t.isActiveAndEnabled ? t.text : null;
                if (!ReferenceEquals(now, old[vis.Texts.Count + i]) && now != old[vis.Texts.Count + i])
                    return true;
            }
            return false;
        }

        private static readonly Dictionary<VRInteractable, ElementVisuals> Visuals = new Dictionary<VRInteractable, ElementVisuals>();

        // Label padding around the drawn glyphs: a quarter of the label height, at least this much.
        private const float MinLabelPadMeters = 0.003f;

        private static bool HasSize(RectTransform rt) => rt.rect.width > 1e-4f && rt.rect.height > 1e-4f;

        private static ElementVisuals CollectVisuals(VRInteractable v)
        {
            var vis = new ElementVisuals();
            foreach (var g in v.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
            {
                if (g.GetComponentInParent<VRInteractable>(true) != v)
                    continue; // drawn by a nested element
                if (g is UnityEngine.UI.Text txt)
                    vis.Texts.Add(txt);
                else if (g is TMPro.TMP_Text tmp)
                    vis.Tmps.Add(tmp);
                else
                    vis.Images.Add(g);
            }
            return vis;
        }

        // Exact extents of the glyphs a legacy Text actually drew, in its local space.
        private static bool DrawnTextRect(UnityEngine.UI.Text t, out Rect r)
        {
            r = default;
            if (!t.isActiveAndEnabled || string.IsNullOrEmpty(t.text) || t.color.a < 0.02f)
                return false;
            TextGenerator gen = t.cachedTextGenerator;
            if (gen == null || gen.vertexCount < 4)
                return false;
            IList<UIVertex> verts = gen.verts;
            float inv = 1f / Mathf.Max(t.pixelsPerUnit, 1e-4f);
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            bool any = false;
            for (int i = 0; i + 3 < verts.Count; i += 4)
            {
                // One quad per character; spaces come out as empty quads.
                Vector3 a = verts[i].position, c = verts[i + 2].position;
                if (Mathf.Abs(c.x - a.x) < 1e-3f || Mathf.Abs(c.y - a.y) < 1e-3f)
                    continue;
                for (int k = 0; k < 4; k++)
                {
                    Vector3 p = verts[i + k].position;
                    xMin = Mathf.Min(xMin, p.x); xMax = Mathf.Max(xMax, p.x);
                    yMin = Mathf.Min(yMin, p.y); yMax = Mathf.Max(yMax, p.y);
                }
                any = true;
            }
            if (!any)
                return false;
            r = Rect.MinMaxRect(xMin * inv, yMin * inv, xMax * inv, yMax * inv);
            return true;
        }

        private static bool DrawnTmpRect(TMPro.TMP_Text t, out Rect r)
        {
            r = default;
            if (!t.isActiveAndEnabled || string.IsNullOrEmpty(t.text) || t.color.a < 0.02f)
                return false;
            Bounds b = t.textBounds;
            if (b.size.x <= 1e-4f || b.size.y <= 1e-4f)
                return false;
            r = new Rect(b.min.x, b.min.y, b.size.x, b.size.y);
            return true;
        }

        // Union of an element's drawn text labels, in the first label's local plane, padded for comfortable clicking.
        private static bool LabelHitbox(ElementVisuals vis, out Transform plane, out Rect rect)
        {
            Transform basis = null;
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;

            void Add(Transform tf, Rect r)
            {
                if (basis == null)
                    basis = tf;
                for (int k = 0; k < 4; k++)
                {
                    var corner = new Vector3(k == 0 || k == 3 ? r.xMin : r.xMax, k < 2 ? r.yMin : r.yMax, 0f);
                    Vector3 p = tf == basis ? corner : basis.InverseTransformPoint(tf.TransformPoint(corner));
                    xMin = Mathf.Min(xMin, p.x); xMax = Mathf.Max(xMax, p.x);
                    yMin = Mathf.Min(yMin, p.y); yMax = Mathf.Max(yMax, p.y);
                }
            }

            foreach (var t in vis.Texts)
                if (t != null && DrawnTextRect(t, out Rect r))
                    Add(t.transform, r);
            foreach (var t in vis.Tmps)
                if (t != null && DrawnTmpRect(t, out Rect r))
                    Add(t.transform, r);

            plane = basis;
            rect = default;
            if (basis == null)
                return false;

            float worldPerLocal = Mathf.Max(basis.TransformVector(Vector3.right).magnitude, 1e-6f);
            float pad = Mathf.Max(0.25f * (yMax - yMin), MinLabelPadMeters / worldPerLocal);
            rect = Rect.MinMaxRect(xMin - pad, yMin - pad, xMax + pad, yMax + pad);
            return true;
        }

        // Smallest visible non-text graphic of the element (an icon), for buttons without a label.
        private static bool IconHitbox(ElementVisuals vis, out Transform plane, out Rect rect)
        {
            plane = null;
            rect = default;
            float best = float.MaxValue;
            foreach (var g in vis.Images)
            {
                if (g == null || !g.isActiveAndEnabled || g.color.a < 0.02f || !(g.transform is RectTransform rt) || !HasSize(rt))
                    continue;
                float area = WorldArea(rt, rt.rect);
                if (area < best)
                {
                    best = area;
                    plane = rt;
                    rect = rt.rect;
                }
            }
            return plane != null;
        }

        // Hitbox = what you see, as close to the label as possible, so neighbouring buttons don't overlap.
        // (The buttons' own RectTransforms are often far bigger than the button: 12 x 12 cm on the F-45 MFDs.)
        //  1. the drawn text label(s), padded a little;
        //  2. else the smallest drawn icon;
        //  3. else the element's own rect, 4. the game's rect bounds, 5. a sphere of `radius`.
        private static bool TryHit(VRInteractable v, Ray ray, out Hit hit)
        {
            hit = default;
            hit.Button = v;
            Transform t = v.transform;
            if (Visuals.TryGetValue(v, out ElementVisuals vis))
            {
                string kind = DrawnHitbox(vis, out Transform plane, out Rect drawn);
                if (kind != null)
                {
                    hit.Shape = kind;
                    return HitLocalRect(plane, drawn, 0f, ray, ref hit);
                }
            }
            if (t is RectTransform own && HasSize(own))
            {
                hit.Shape = "rect";
                return HitLocalRect(t, own.rect, 0f, ray, ref hit);
            }
            if (v.useRect)
            {
                hit.Shape = "bounds";
                var r = new Rect(v.rect.min.x, v.rect.min.y, v.rect.size.x, v.rect.size.y);
                return HitLocalRect(t, r, v.rect.center.z, ray, ref hit);
            }

            // Sphere around the element (world units, like the VR finger check).
            hit.Shape = "sphere";
            float radius = Mathf.Max(v.radius, MinSphereRadius);
            Vector3 toCenter = t.position - ray.origin;
            float along = Vector3.Dot(toCenter, ray.direction);
            if (along <= 0f)
                return false;
            float off = (toCenter - ray.direction * along).magnitude;
            if (off > radius)
                return false;
            hit.Dist = along;
            hit.Area = 4f * radius * radius;
            Camera cam = GetCamera();
            Vector3 right = cam != null ? cam.transform.right : t.right;
            Vector3 up = cam != null ? cam.transform.up : t.up;
            hit.C0 = t.position + (-right - up) * radius;
            hit.C1 = t.position + (right - up) * radius;
            hit.C2 = t.position + (right + up) * radius;
            hit.C3 = t.position + (-right + up) * radius;
            hit.Tf = t;
            return true;
        }

        private static bool HitLocalRect(Transform t, Rect r, float localZ, Ray ray, ref Hit hit)
        {
            if (!RayHitsRect(t, r, localZ, ray, out float dist, out _))
                return false;
            hit.Dist = dist;
            hit.Area = WorldArea(t, r);
            hit.C0 = t.TransformPoint(new Vector3(r.xMin, r.yMin, localZ));
            hit.C1 = t.TransformPoint(new Vector3(r.xMax, r.yMin, localZ));
            hit.C2 = t.TransformPoint(new Vector3(r.xMax, r.yMax, localZ));
            hit.C3 = t.TransformPoint(new Vector3(r.xMin, r.yMax, localZ));
            hit.Tf = t;
            return true;
        }

        private static void FindHover(Ray ray)
        {
            ClearHover();
            Hits.Clear();

            float nearest = float.MaxValue;
            foreach (var v in Buttons)
            {
                if (v == null || !v.enabled || !v.gameObject.activeInHierarchy)
                    continue;
                if (TryHit(v, ray, out Hit h))
                {
                    Hits.Add(h);
                    nearest = Mathf.Min(nearest, h.Dist);
                }
            }

            // Buttons always beat touch surfaces. Among buttons on the front-most screen layer (anything within
            // SameSurfaceMeters of the nearest hit), the smallest one is the specific button under the cursor;
            // big page-sized elements layered a little in front don't swallow it.
            int best = -1;
            for (int i = 0; i < Hits.Count; i++)
            {
                if (Hits[i].Dist > nearest + SameSurfaceMeters)
                    continue;
                if (best < 0 || Hits[i].Area < Hits[best].Area)
                    best = i;
            }
            if (best >= 0)
            {
                Hit h = Hits[best];
                _hoverButton = h.Button;
                SetOutline(h.Tf, h.C0, h.C1, h.C2, h.C3);
                HoverInfo = $"{ButtonName(h.Button)}\n{h.Button.name} · {h.Shape} {(h.C1 - h.C0).magnitude * 100f:0.0} × {(h.C3 - h.C0).magnitude * 100f:0.0} cm";
                if (Hits.Count > 1)
                    HoverInfo += $" · {Hits.Count - 1} more under cursor";
                return;
            }

            // No button: a touchscreen surface under the cursor can be dragged (lowest priority).
            float bestDist = float.MaxValue;
            foreach (var t in Touches)
            {
                if (t == null || !t.isActiveAndEnabled || t.screenRect == null || !t.screenRect.gameObject.activeInHierarchy)
                    continue;
                Rect r = t.screenRect.rect;
                if (!RayHitsRect(t.screenRect, r, 0f, ray, out float dist, out _) || dist >= bestDist)
                    continue;
                bestDist = dist;
                _hoverTouch = t;
                HoverInfo = $"Touch / drag area\n{t.name} · {r.width * t.screenRect.lossyScale.x * 100f:0.0} × {r.height * t.screenRect.lossyScale.y * 100f:0.0} cm";
                Transform s = t.screenRect;
                SetOutline(s, s.TransformPoint(new Vector3(r.xMin, r.yMin, 0f)), s.TransformPoint(new Vector3(r.xMax, r.yMin, 0f)),
                           s.TransformPoint(new Vector3(r.xMax, r.yMax, 0f)), s.TransformPoint(new Vector3(r.xMin, r.yMax, 0f)));
            }
        }

        // The outline is kept relative to the element's transform, not as world points: the cockpit flies at
        // hundreds of m/s, so world points captured on hover drift off the button while it's held.
        private static void SetOutline(Transform tf, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            _outlineTf = tf;
            _hoverCorners[0] = tf.InverseTransformPoint(a);
            _hoverCorners[1] = tf.InverseTransformPoint(b);
            _hoverCorners[2] = tf.InverseTransformPoint(c);
            _hoverCorners[3] = tf.InverseTransformPoint(d);
        }

        private static Transform _outlineTf;

        // While LMB holds a screen button: seconds held so far (for the hold timer), else 0.
        public static float PressHeldSeconds => _pressed != null ? Time.unscaledTime - _pressStart : 0f;

        private static void ClearHover()
        {
            _hoverButton = null;
            _hoverTouch = null;
            HoverInfo = null;
        }

        // Debug tooltip text for the hovered screen element: name, hitbox source and size.
        public static string HoverInfo { get; private set; }

        private static string ButtonName(VRInteractable v) =>
            string.IsNullOrEmpty(v.interactableName) ? v.name : v.interactableName;

        // One line per click, so a wrong press can be diagnosed from Player.log.
        private static void LogClick(VRInteractable pressed)
        {
            var sb = new System.Text.StringBuilder("[VirtualJoystick] Screen click: ");
            sb.Append(Describe(pressed));
            int others = 0;
            foreach (var h in Hits)
            {
                if (h.Button == pressed)
                    continue;
                sb.Append(others == 0 ? " | also under cursor: " : ", ").Append(Describe(h.Button));
                if (++others >= 5)
                    break;
            }
            Debug.Log(sb.ToString());
        }

        private static string Describe(VRInteractable v)
        {
            foreach (var h in Hits)
                if (h.Button == v)
                    return $"'{v.interactableName}' {v.name} [{h.Shape}, {h.Dist:0.000}m, {h.Area * 1e4f:0.0}cm2]";
            return $"'{v.interactableName}' {v.name}";
        }

        // ------------------------------------------------------------------ press

        // A press is held for as long as LMB is down, so hold-to-act elements (e.g. hold a layout preset for 2 s to
        // save it, tap to load) see a real press and release. Each stage of the game's StartInteraction /
        // StopInteraction is run separately and guarded: listeners written for a VR hand can throw with no hand,
        // and one throwing listener must not stop the rest, above all the release (OnStopInteract).
        private static readonly MethodInfo SetInteracting = AccessTools.PropertySetter(typeof(VRInteractable), "interacting");
        private static readonly FieldInfo InteractedOnFrame = AccessTools.Field(typeof(VRInteractable), "interactedOnFrame");
        private static readonly FieldInfo StartInteractionEvent = AccessTools.Field(typeof(VRInteractable), "OnStartInteraction");
        private static readonly FieldInfo StopInteractionEvent = AccessTools.Field(typeof(VRInteractable), "OnStopInteraction");
        private static readonly MethodInfo WhileInteractingRoutine = AccessTools.Method(typeof(VRInteractable), "WhileInteractingRoutine");

        private static float _pressStart;

        private static void BeginPress(VRInteractable v)
        {
            LogClick(v);
            _pressed = v;
            _pressStart = Time.unscaledTime;

            var key = v.GetComponent<VRKeyboard.VRKey>();
            if (key != null)
            {
                // On-screen keyboard keys (radio frequency entry etc.) press through OnPress, as in FlatScreen 3.
                Guard(key.OnPress, v, "key press");
                _pressed = null;
                return;
            }
            if (v.GetComponent<VRButton>() != null)
            {
                // VRButton listens to OnInteract / OnStopInteract only (a VR finger push).
                Guard(() => v.OnInteract?.Invoke(), v, "OnInteract");
                return;
            }

            SetInteracting?.Invoke(v, new object[] { true });
            InteractedOnFrame?.SetValue(v, Time.frameCount);
            InvokeHandEvent(StartInteractionEvent, v);
            Guard(() => v.OnInteract?.Invoke(), v, "OnInteract");
            // Continuous holds (sliders, hold-to-slew...) get OnInteracting every frame while interacting.
            if (v.OnInteracting != null && v.gameObject.activeInHierarchy && WhileInteractingRoutine != null)
                Guard(() => v.StartCoroutine((System.Collections.IEnumerator)WhileInteractingRoutine.Invoke(v, null)), v, "OnInteracting");
        }

        private static void EndPress()
        {
            if (_pressed == null)
                return;
            VRInteractable v = _pressed;
            _pressed = null;
            if (v == null)
                return;
            Debug.Log($"[VirtualJoystick] Screen release: '{ButtonName(v)}' held {Time.unscaledTime - _pressStart:0.00}s");

            if (v.GetComponent<VRButton>() != null)
            {
                Guard(() => v.OnStopInteract?.Invoke(), v, "OnStopInteract");
                return;
            }
            if (!v.interacting && SetInteracting != null)
                return; // the game already ended this interaction itself
            SetInteracting?.Invoke(v, new object[] { false });
            InvokeHandEvent(StopInteractionEvent, v);
            Guard(() => v.OnStopInteract?.Invoke(), v, "OnStopInteract");
        }

        // OnStartInteraction / OnStopInteraction carry the VR hand; there is none, so each listener gets null on
        // its own, and one that can't cope doesn't block the others.
        private static void InvokeHandEvent(FieldInfo field, VRInteractable v)
        {
            if (field == null || !(field.GetValue(v) is Delegate d))
                return;
            foreach (Delegate listener in d.GetInvocationList())
            {
                try
                {
                    listener.DynamicInvoke(new object[] { null });
                }
                catch (Exception)
                {
                    // Listener needs a real VR hand (haptics, glove animation); nothing to do with no hand.
                }
            }
        }

        private static void Guard(Action action, VRInteractable v, string what)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Exception inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                Log.Warn($"Screen {what} on '{ButtonName(v)}' threw {inner.GetType().Name}: {inner.Message}");
            }
        }

        // ------------------------------------------------------------------ touch / drag

        private static bool CanTouch => TouchBeginField != null && TouchingField != null && SetIsTouching != null && SetTouchingLocalPoint != null;

        private static void BeginTouch(VRTouchScreenInteractable t, Ray ray)
        {
            if (!CanTouch)
            {
                Log.Warn("VRTouchScreenInteractable members not found (game update?); touchscreen drag disabled");
                return;
            }
            if (!RayHitsRect(t.screenRect, t.screenRect.rect, 0f, ray, out _, out Vector3 point))
                return;
            Debug.Log($"[VirtualJoystick] Screen touch: {t.name}");
            _touching = t;
            SetTouchingLocalPoint.Invoke(t, new object[] { t.transform.InverseTransformPoint(point) });
            SetIsTouching.Invoke(t, new object[] { true });
            InvokeTouch(TouchBeginField, t);
        }

        private static void UpdateTouch(Ray ray)
        {
            VRTouchScreenInteractable t = _touching;
            if (t == null || !t.isActiveAndEnabled || !Input.GetMouseButton(0))
            {
                EndTouch();
                return;
            }
            // Like a finger: keep following the cursor across the screen plane; sliding off the screen lets go.
            if (!RayHitsRect(t.screenRect, t.screenRect.rect, 0f, ray, out _, out Vector3 point))
            {
                EndTouch();
                return;
            }
            SetTouchingLocalPoint.Invoke(t, new object[] { t.transform.InverseTransformPoint(point) });
            InvokeTouch(TouchingField, t);
        }

        private static void EndTouch()
        {
            if (_touching != null)
                SetIsTouching?.Invoke(_touching, new object[] { false });
            _touching = null;
        }

        private static void InvokeTouch(FieldInfo field, VRTouchScreenInteractable t)
        {
            if (field != null && field.GetValue(t) is Action action)
                action();
        }

        // ------------------------------------------------------------------ geometry

        private static Camera GetCamera()
        {
            if (FsInstance == null)
                return null;
            object fs = FsInstance.GetValue(null, null);
            if (fs == null)
                return null;
            if (FsEnabled != null && !(bool)FsEnabled.GetValue(fs, null))
                return null;
            var go = FsCamera.GetValue(fs) as GameObject;
            return go != null ? go.GetComponent<Camera>() : null;
        }

        // Mouse ray through the camera's actual projection (what's drawn), rather than FlatScreen 3's hand-built
        // FOV/aspect ray, which drifts off-target whenever the projection isn't a plain symmetric full-screen one.
        private static Ray MouseRay(Camera cam)
        {
            if (cam.targetTexture == null)
                return cam.ScreenPointToRay(Input.mousePosition);
            // Rendering to a texture: fall back to the FOV-built ray over the whole screen.
            float x = Input.mousePosition.x / Screen.width * 2f - 1f;
            float y = Input.mousePosition.y / Screen.height * 2f - 1f;
            float tan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float aspect = (float)Screen.width / Screen.height;
            Vector3 dir = new Vector3(x * tan * aspect, y * tan, 1f);
            return new Ray(cam.transform.position, cam.transform.TransformDirection(dir).normalized);
        }

        // Inverse of MouseRay, for drawing the hitbox outline.
        private static Vector3 WorldToScreen(Camera cam, Vector3 world)
        {
            if (cam.targetTexture == null)
                return cam.WorldToScreenPoint(world);
            Vector3 local = cam.transform.InverseTransformPoint(world);
            if (local.z <= 0f)
                return new Vector3(0f, 0f, -1f);
            float tan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float aspect = (float)Screen.width / Screen.height;
            float x = local.x / local.z / (tan * aspect);
            float y = local.y / local.z / tan;
            return new Vector3((x + 1f) * 0.5f * Screen.width, (y + 1f) * 0.5f * Screen.height, local.z);
        }

        // Ray vs a rectangle in t's local XY plane at local depth z.
        private static bool RayHitsRect(Transform t, Rect r, float localZ, Ray ray, out float dist, out Vector3 point)
        {
            point = Vector3.zero;
            var plane = new Plane(t.forward, t.TransformPoint(new Vector3(0f, 0f, localZ)));
            if (!plane.Raycast(ray, out dist))
                return false;
            point = ray.GetPoint(dist);
            Vector3 lp = t.InverseTransformPoint(point);
            return r.Contains(new Vector2(lp.x, lp.y));
        }

        private static float WorldArea(Transform t, Rect r) =>
            t.TransformVector(new Vector3(r.width, 0f, 0f)).magnitude * t.TransformVector(new Vector3(0f, r.height, 0f)).magnitude;
    }
}
