using ModLoader.Framework;
using ModLoader.Framework.Attributes;
using UnityEngine;

namespace MouseStick
{
    [ItemId("vtolmouse.mousestick")]
    public class Main : VtolMod
    {
        private GameObject _host;

        private void Awake()
        {
            Log.Info("Loading");
            MouseStickSettings.Load();

            _host = new GameObject("MouseStick");
            DontDestroyOnLoad(_host);
            _host.AddComponent<MouseStickBehaviour>();
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
        public static void Info(object msg) => Debug.Log("[MouseStick] " + msg);
        public static void Warn(object msg) => Debug.LogWarning("[MouseStick] " + msg);
        public static void Error(object msg) => Debug.LogError("[MouseStick] " + msg);
    }
}
