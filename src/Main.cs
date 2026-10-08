using ModLoader.Framework;
using ModLoader.Framework.Attributes;
using UnityEngine;

namespace VirtualJoystick
{
    [ItemId("vtolmouse.kbmslop")]
    public class Main : VtolMod
    {
        private GameObject _host;

        private void Awake()
        {
            Log.Info("Loading");

            _host = new GameObject("VirtualJoystick");
            DontDestroyOnLoad(_host);
            _host.AddComponent<VirtualJoystickBehaviour>();
        }

        public override void UnLoad()
        {
            FlatScreenCompat.Unpatch();
            if (_host != null)
                Destroy(_host);
            Log.Info("Unloaded");
        }
    }

    internal static class Log
    {
        public static void Info(object msg) => Debug.Log("[VirtualJoystick] " + msg);
        public static void Warn(object msg) => Debug.LogWarning("[VirtualJoystick] " + msg);
        public static void Error(object msg) => Debug.LogError("[VirtualJoystick] " + msg);
    }
}
