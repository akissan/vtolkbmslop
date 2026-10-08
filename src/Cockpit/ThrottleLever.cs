using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VirtualJoystick.Cockpit
{
    // Moves a VRThrottle the way a hand does, one step per frame: throttle click sounds, the afterburner detent
    // (the lever holds at the gate while being pushed into it, then snaps through to full), the AB / MIL gate
    // sounds, the lever animation, and the engine value. Ported from BYOJoystick's CThrottle.UpdateThrottle,
    // which reproduces VRThrottle's own hand-driven update through its private members.
    // If a game update renames those members, it falls back to the public RemoteSetThrottle (no gate sounds).
    internal static class ThrottleLever
    {
        private static readonly FieldInfo ThrottleField = AccessTools.Field(typeof(VRThrottle), "throttle");
        private static readonly FieldInfo GateAnimField = AccessTools.Field(typeof(VRThrottle), "gateAnimThrottle");
        private static readonly FieldInfo BelowGateField = AccessTools.Field(typeof(VRThrottle), "belowGate");
        private static readonly FieldInfo LastClickField = AccessTools.Field(typeof(VRThrottle), "lastClickT");
        private static readonly FieldInfo SmoothFlagField = AccessTools.Field(typeof(VRThrottle), "smoothThrottle");
        private static readonly FieldInfo SmoothValueField = AccessTools.Field(typeof(VRThrottle), "f_smoothThrottle");
        private static readonly MethodInfo UpdateThrottleMethod = AccessTools.Method(typeof(VRThrottle), "UpdateThrottle", new[] { typeof(float) });
        private static readonly MethodInfo UpdateAnimMethod = AccessTools.Method(typeof(VRThrottle), "UpdateThrottleAnim", new[] { typeof(float) });

        private static bool CanAnimate =>
            ThrottleField != null && GateAnimField != null && BelowGateField != null && LastClickField != null
            && UpdateThrottleMethod != null && UpdateAnimMethod != null;

        private static bool _warned;

        public static void Move(VRThrottle th, float t, float dt)
        {
            if (!CanAnimate)
            {
                if (!_warned)
                {
                    _warned = true;
                    Log.Warn("VRThrottle members not found (game update?); throttle keys move without detent/gate sounds");
                }
                th.RemoteSetThrottleForceEvents(t);
                return;
            }

            bool sendEvents = th.sendEvents;
            th.sendEvents = true; // the engine only listens when events are sent
            try
            {
                MoveFrame(th, t, dt);
            }
            catch (Exception e)
            {
                Exception inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                if (!_warned)
                {
                    _warned = true;
                    Log.Warn("Throttle lever update threw " + inner.Message + "; using the plain setter");
                }
                th.RemoteSetThrottleForceEvents(t);
            }
            finally
            {
                th.sendEvents = sendEvents;
            }
        }

        private static void MoveFrame(VRThrottle th, float t, float dt)
        {
            if (th.abGate)
            {
                float gateAnim = (float)GateAnimField.GetValue(th);
                bool atGate = false;
                if (t > th.abGateThreshold && t < 1f - th.abPostGateWidth)
                {
                    // Pushing into the detent: the output stays at the gate, the lever creeps forward a little.
                    gateAnim = Mathf.Lerp(gateAnim, Mathf.Lerp(th.abGateThreshold, t, 0.25f), 25f * dt);
                    GateAnimField.SetValue(th, gateAnim);
                    t = th.abGateThreshold;
                    Apply(th, t, gateAnim);
                    atGate = true;
                }
                else if (t >= 1f - th.abPostGateWidth)
                {
                    // Through the detent: full afterburner, lever swings to the stop.
                    t = 1f;
                    gateAnim = Mathf.Lerp(gateAnim, 1f, 25f * dt);
                    GateAnimField.SetValue(th, gateAnim);
                    Apply(th, t, gateAnim);
                    atGate = true;
                }
                else
                {
                    GateAnimField.SetValue(th, t);
                }

                // Gate sounds when crossing into / out of afterburner.
                bool below = (bool)BelowGateField.GetValue(th);
                if (t > th.abGateThreshold + 0.01f)
                {
                    if (below)
                    {
                        BelowGateField.SetValue(th, false);
                        if (th.gateAudioSource != null && th.gateABSound != null)
                            th.gateAudioSource.PlayOneShot(th.gateABSound);
                    }
                }
                else if (!below)
                {
                    BelowGateField.SetValue(th, true);
                    if (th.gateAudioSource != null && th.gateMilSound != null)
                        th.gateAudioSource.PlayOneShot(th.gateMilSound);
                }
                if (atGate)
                    return;
            }

            // Throttle clicks every throttleClickInterval of travel, louder at higher power (as the game does).
            float lastClick = (float)LastClickField.GetValue(th);
            if (th.audioSource != null && th.throttleClickSound != null && Mathf.Abs(t - lastClick) > th.throttleClickInterval)
            {
                LastClickField.SetValue(th, t);
                th.audioSource.volume = t;
                th.audioSource.PlayOneShot(th.throttleClickSound);
            }
            Apply(th, t, t);
        }

        // Engine value + lever animation + the stored throttle (and its smoothed copy, if the throttle smooths).
        private static void Apply(VRThrottle th, float value, float anim)
        {
            UpdateAnimMethod.Invoke(th, new object[] { anim });
            UpdateThrottleMethod.Invoke(th, new object[] { value });
            ThrottleField.SetValue(th, value);
            if (SmoothFlagField != null && SmoothValueField != null && (bool)SmoothFlagField.GetValue(th))
                SmoothValueField.SetValue(th, value);
        }
    }
}
