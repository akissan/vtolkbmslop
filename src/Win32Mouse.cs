using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

namespace VirtualJoystick
{
    // Reads mouse movement straight from Windows instead of Unity's "Mouse X/Y" axes, which gave no stick
    // movement in VTOL VR with Unity's cursor lock on. Each frame: measure how far the cursor moved from the centre
    // of the game window, then warp it back. The cursor is clipped to the window so a fast flick can't
    // land a click on another application.
    internal static class Win32Mouse
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern bool ClipCursor(ref RECT r);
        [DllImport("user32.dll", EntryPoint = "ClipCursor")] private static extern bool ClipCursorNone(IntPtr r);
        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT p);

        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        private const int VK_MBUTTON = 0x04;

        private static IntPtr _hwnd;
        private static bool _captured;

        public static bool IsCaptured => _captured;

        // Middle mouse button state straight from Windows (backs up Unity's, whatever the cursor capture is doing).
        public static bool MiddleHeld => (GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0;

        // Clip to the game window and centre the cursor. Returns false if the window can't be found.
        public static bool Capture()
        {
            if (!TryGetClientRect(out RECT rect))
                return false;
            ClipCursor(ref rect);
            SetCursorPos((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
            _captured = true;
            return true;
        }

        // Clip to the game window without moving the cursor or reading it (free look in clickable mode). Release undoes it.
        public static void Confine()
        {
            if (!TryGetClientRect(out RECT rect))
                return;
            ClipCursor(ref rect);
            _captured = true;
        }

        // Put the cursor at the centre of the game window.
        public static void Center()
        {
            if (TryGetClientRect(out RECT rect))
                SetCursorPos((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
        }

        public static void Release()
        {
            if (!_captured)
                return;
            _captured = false;
            ClipCursorNone(IntPtr.Zero);
        }

        // Movement since the last call in pixels (+x right, +y up), and re-centres the cursor.
        // With recenterOnly, the movement is thrown away (used after free look so the stick doesn't jump).
        public static Vector2 ReadDelta(bool recenterOnly = false)
        {
            if (!_captured || !TryGetClientRect(out RECT rect))
                return Vector2.zero;

            // Re-clip every frame: the window may have moved or been resized, and alt-tab drops the clip.
            ClipCursor(ref rect);
            int cx = (rect.Left + rect.Right) / 2;
            int cy = (rect.Top + rect.Bottom) / 2;
            GetCursorPos(out POINT p);
            SetCursorPos(cx, cy);
            if (recenterOnly)
                return Vector2.zero;
            return new Vector2(p.X - cx, cy - p.Y);
        }

        private static bool TryGetClientRect(out RECT rect)
        {
            rect = default;
            IntPtr hwnd = FindGameWindow();
            if (hwnd == IntPtr.Zero || !GetClientRect(hwnd, out RECT client))
                return false;
            var tl = new POINT { X = client.Left, Y = client.Top };
            var br = new POINT { X = client.Right, Y = client.Bottom };
            ClientToScreen(hwnd, ref tl);
            ClientToScreen(hwnd, ref br);
            rect = new RECT { Left = tl.X, Top = tl.Y, Right = br.X, Bottom = br.Y };
            return rect.Right - rect.Left > 0 && rect.Bottom - rect.Top > 0;
        }

        private static IntPtr FindGameWindow()
        {
            if (_hwnd != IntPtr.Zero)
                return _hwnd;
            IntPtr h = GetActiveWindow();
            if (h == IntPtr.Zero)
            {
                h = GetForegroundWindow();
                GetWindowThreadProcessId(h, out uint pid);
                if (pid != (uint)Process.GetCurrentProcess().Id)
                    h = IntPtr.Zero;
            }
            _hwnd = h;
            return h;
        }
    }
}
