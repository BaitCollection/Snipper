// Snipper — a lightweight screenshot tool for Windows.
// Built with the C# compiler that ships with .NET Framework 4.x (see build.cmd).
// The code deliberately sticks to C# 5, the version the built-in csc.exe supports.
//
// MIT License — see LICENSE.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Snipper")]
[assembly: System.Reflection.AssemblyProduct("Snipper")]
[assembly: System.Reflection.AssemblyVersion("2.0.0.0")]

namespace Snipper
{
    // UI language: Ukrainian or English.
    static class L
    {
        public static bool En;
        public static string T(string uk, string en) { return En ? en : uk; }
    }

    static class Native
    {
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [DllImport("kernel32.dll")] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
        public const int WM_HOTKEY = 0x0312;
        public const int WM_DPICHANGED = 0x02E0;
        public const uint MOD_NOREPEAT = 0x4000;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint VK_SNAPSHOT = 0x2C;
        public const uint VK_S = 0x53;

        public static void EnableDpiAwareness()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch { }
            try { SetProcessDPIAware(); } catch { }
        }
    }

    static class Settings
    {
        const string Key = @"Software\Snipper";
        const string OldKey = @"Software\Screenshoter";   // the app's previous name
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "Snipper";
        const string OldRunName = "Screenshoter";

        // One-time move of settings and autostart from the old "Screenshoter" name.
        public static void MigrateFromOldName()
        {
            try
            {
                using (RegistryKey old = Registry.CurrentUser.OpenSubKey(OldKey))
                {
                    if (old == null) return;
                    using (RegistryKey cur = Registry.CurrentUser.OpenSubKey(Key))
                    {
                        if (cur == null)
                            using (RegistryKey nk = Registry.CurrentUser.CreateSubKey(Key))
                                foreach (string n in old.GetValueNames())
                                    nk.SetValue(n, old.GetValue(n), old.GetValueKind(n));
                    }
                }
                bool hadAutostart = false;
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (run != null && run.GetValue(OldRunName) != null)
                    {
                        hadAutostart = true;
                        run.DeleteValue(OldRunName);
                    }
                }
                if (hadAutostart) Autostart = true;
                Registry.CurrentUser.DeleteSubKeyTree(OldKey, false);
                // Close a still-running old copy so the two don't fight over the hotkey.
                foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcessesByName("Screenshoter"))
                {
                    try { p.Kill(); } catch { }
                    p.Dispose();
                }
            }
            catch { }
        }

        // "uk" or "en". By default follows the Windows display language.
        public static string Language
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
                {
                    string v = k == null ? null : k.GetValue("Language") as string;
                    if (v == "uk" || v == "en") return v;
                }
                string sys = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                return sys == "uk" || sys == "ru" ? "uk" : "en";
            }
            set
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key)) k.SetValue("Language", value);
            }
        }

        public static string SaveFolder
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
                {
                    string v = k == null ? null : k.GetValue("SaveFolder") as string;
                    if (!string.IsNullOrEmpty(v)) return v;
                }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
            }
            set
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key)) k.SetValue("SaveFolder", value);
            }
        }

        public static bool FirstRunDone
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
                    return k != null && k.GetValue("FirstRunDone") != null;
            }
            set
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key)) k.SetValue("FirstRunDone", 1);
            }
        }

        public static int ColorArgb
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
                {
                    object v = k == null ? null : k.GetValue("Color");
                    return v is int ? (int)v : Color.FromArgb(255, 59, 48).ToArgb();
                }
            }
            set
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key)) k.SetValue("Color", value, RegistryValueKind.DWord);
            }
        }

        static int GetInt(string name, int def)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
            {
                object v = k == null ? null : k.GetValue(name);
                return v is int ? (int)v : def;
            }
        }
        static void SetInt(string name, int v)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key)) k.SetValue(name, v, RegistryValueKind.DWord);
        }
        // Hotkey: virtual-key code + modifiers (1 Alt, 2 Ctrl, 4 Shift, 8 Win)
        public static int HotkeyVk { get { return GetInt("HotkeyVk", 0x2C); } set { SetInt("HotkeyVk", value); } }
        public static int HotkeyMods { get { return GetInt("HotkeyMods", 0); } set { SetInt("HotkeyMods", value); } }

        public static string HotkeyName(int vk, int mods)
        {
            string r = "";
            if ((mods & 2) != 0) r += "Ctrl+";
            if ((mods & 8) != 0) r += "Win+";
            if ((mods & 1) != 0) r += "Alt+";
            if ((mods & 4) != 0) r += "Shift+";
            Keys k = (Keys)vk;
            string name;
            if (k == Keys.PrintScreen) name = "Print Screen";
            else if (k >= Keys.D0 && k <= Keys.D9) name = ((int)(k - Keys.D0)).ToString(CultureInfo.InvariantCulture);
            else if (k >= Keys.NumPad0 && k <= Keys.NumPad9) name = "Num " + ((int)(k - Keys.NumPad0)).ToString(CultureInfo.InvariantCulture);
            else if (k == Keys.Pause) name = "Pause";
            else if (k == Keys.Scroll) name = "Scroll Lock";
            else if (k == Keys.Insert) name = "Insert";
            else if (k == Keys.Oemtilde) name = "`";
            else name = k.ToString();
            return r + name;
        }

        public static int PenWidth { get { return GetInt("PenWidth", 4); } set { SetInt("PenWidth", value); } }
        public static int BlurStrength { get { return GetInt("BlurStrength", 4); } set { SetInt("BlurStrength", value); } }

        public static bool Autostart
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(RunName) != null;
            }
            set
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                    else if (k.GetValue(RunName) != null) k.DeleteValue(RunName);
                }
            }
        }

        // Screen_01.png, Screen_02.png ... — first free name in the folder
        public static string NextFreeName(string folder)
        {
            for (int i = 1; ; i++)
            {
                string name = "Screen_" + i.ToString("00", CultureInfo.InvariantCulture) + ".png";
                if (!File.Exists(Path.Combine(folder, name))) return name;
            }
        }
    }

    static class Icons
    {
        public static readonly Dictionary<string, string> Data = new Dictionary<string, string>
        {
            { "Select", "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAFkElEQVR42u2bTYgdRRDHf93vvTVZzfqBCoJGEaISIrkIiUHFD4LgTYWcPISIikajrAb8OuhFT+ZiZCWCeknQGBAET0GIegh+gHoQ1KAE9iboJmvysu57b8bDVEMxds9XJuybt9PQ7Jve6Z6q6urqqup/mz0HY1ZzsazysuoF0K3Qx+T8P67Yr65x4gshAAN0gEhqnlal3+kAowraGRVo8/Wz8r24DgG4jw7leRq4KPDuqQCBo5x+6XIOWBLBayYioXldoN+/QF/RkCuwPAG4mbsY2Ak8BNwkzJiU2sXAPPAo8IN8PBYCngX2igBshhq7cf4BXgUOKjsVAfcB7wBXyhhpGvrAb8CnwIfAmTztMxnboOt4O/A+cEvB2fsSuFuEOwRuBH6vYGsWgRuABWHUAj8DNxfs/wuwCzieJQSbofYjYAtwVJgfAMsF1tVKOhax0DgQmo8KD6MQr92AwYuBS4BDov5DedeIKp4KqO48MKvGsMAfwHPAC8CaEkvgFZl9q+zIbmA/cJVnCQBcCkzJGEOh/RCwGTjrsSneJeBU131soJh/G5gD/lTE6rKQMTtljGBfDJrxfMNnBJ0wrgaeBJ5RQugBT4vt6CpjHtQAZzV3KCNmZIA9FbevjjDVL6HOvnVrhYGQoP8SGq1MYCQ87BD6ozwb4FR8Gtggz1OiBW/J+10lcV8NbYOmZB0FJierT1do3Cc0T0n7BuEpSi+bkBFcI9UJ5Yza40dK/X2VnPVdtFYZZyQ0LgjNxsNPoV1gEkPEuA2GWgGUE0Dc4CVhPPSbMrGASW1ntkI4u5LF+S6R4nNQVAOc1V9QoeXfKas6zobOiid5QNF/QNpsWpu7gQGWJfrbJ+2z0lYkHl/p4vb6vcDn0nYs5KNkeYJfAbcF/teELc8I4wRc6tx8gG0o83opTymbEFdJiEQN3i4jCagqb4MRcI3UqEG7gOPpWuBdqdeF+LWBKAzgceBXqY+JCnUaIoQO8DHwhNSPQrT7okGXwHxD4u518ntaRXXjvO4jYAa4VULnIbBJ2kpFg1ZFWZ1QNNUAZ+h/SZAiRjBOuY95Ieq4agN5hyn2fAdoo8FWAK0AWgG0AmgFMHkCKJxSmtSUmDtScocTvSxvaoy9wLhsSsxlhBaBw+ICu8Bi0ZdSGuOU2Jw6MZqTtk6Rw1F9svuwPB9RgUSTXOI75e/XZWKBWOUDPmnwiZFRjFdOibmU0nJDU2I9ZdNKCyAiASo1tUQycaW3QbddXAG8Drwmv5sSFToa1wPvSb0+xG8IImNIoCX3S9tW4IEG2QG3c22V503AHT7abUZKaZtKKW0LpZTG1AGaATYq+jeWTYmZVEpp0MCkyLBISmySM0LnnRJro8HVHg1OWikVDS4pJ8ihRi8DTisY7UrdF8gay0H6LxeaXRi8FHLqQtFgHzihsLc94HkFm8+CqoWO28rC5EyAUZvRZyg0zgrNDtt8QnjKBUhopOdh4C7VySEvs6Cydd0XiD14ZO3izqTO+tJQ2d1qMozw4kWxhsJhSIDGP5HA3YfyQeconc4AS+8CfkzdF3gKeAlYmzGz6SP574FHSOCvRmnSfuBBBX8hBZa2CjTZJYHqb1Yw3bjIfQEnqS3AFyIMBzLo5TBwDLhHOSDrgZMlbYBbAm8CL5OcSy5JfuJITr+BigTPAvcC34bgPTZjFjrAN8B2kssHPYW9rcuYUfMu5FAhPaF5exbzeeHwSIRwnAQrtJNiV2bS9wXmSeDqL6p+RZbAdyQgLaMAWp+RIL58SyB9ZeYD0YDKV2ZC8PcsY5Z1X2BtiSP2LCOIxwiSujRFXZemNOzM7bF5uP/QfYFzUssug9gz/mLBa3NRXfcGYxVRmYLAKt99gTpsR1RgCUUX8uZo1YRInYmUuA2GWgHUU/4DioXO5dbpBxcAAAAASUVORK5CYII=" },
            { "Pen", "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAE5ElEQVR42uXbSYgcVRjA8X+9mnbDIIKQgyIIiqCezEGRGCUIIiKoGB1BzDqTuKNGBEVRL2JcEqMmGeNkGRXBBTUHBQPRoBLjRSQQDy4gCCJIUDw4OLV4qO8xH2+qerp7ql5V9RQMPfQy837f++ot1V8FXx49TouPEIhznkuAtJc/MNJSeCCPMXA2sEws3wM/dwnOnMO0FB9IDz8D/AB8DHwAHAcmgDMFPzJsAQikzQmwE3gCWCLYGDgJGAe+Ba4EovmCYFqIjwW/CZiRTAjlJxX0+cAhCUYkrwVtDoDG71L4jgMLpMcTQU8AT8vnTF4QTMvwE8BGhZ8vsyPgSWCLfD5sWwBc/HgPeP3ZUN7/iGTCnDHBDCnePSVsJoy7QTBDjHczIQZ2AFeogbGRAdD41xeId9cOIfAWcIbMGIFpOH6sBLzO9gg4D3hOZgpjFgne3SeMARcAsWkgfndFePt/EvlflzdlDHDxGyrCN3Ip7Bufqo3UkboDoPFvCD6quOft9LcV+AkITUPw63vZuZWA7wBTwGY7IJpFgp+Rvz8FrFaDYWoWUc/vF7yRMSD1PQbUhR8R/BoX7zMAGj/pGb+vCO8rAC5+nUf8XmBtER4PV4U1fo80xid+XTd81Rlgd2B14PcIPuyGrzIAgZpq6sCv7/ULEjNk+Ml+8FUEQOP3esbb5XRfX42ZCvFrPOPH+sWXGYA68HZ5u3tQfFkB0Ph9HvEddc1wIHwZAXDxqz3jNy4Ev9AA1ImfKAO/kJWgxu8H7vSM31QGftAMyMPPeMLvKhM/SACK8B0P+J3AXWXi+w1A3fi7y8b3E4A68TuqwvcaAI2f8ox/DbinKnwvs4DGvwnc4Rl/b5X4+TLAlpPUgX/VB76XAIQ14F8B7vOB7xYAW2i0QfD/ecTf7wvfLQCJPK5V36ZG8pNWdN1+u2980SBoCxEvIitBDXLel5S0k7TL5+3AA77xRRlgn1ulGjQtvfMUszV3cUn4l+vCF2WArae7hdmqivfk/AT4gqzO5pyi2rs+8NuAB+vC52WAvYy8DLhE9fI70uBTgcPAcrLK7FAwrcTnBcDO/bep1/8APpeGT0vjfwVWkhUlj/RxOlj81ibg3QAE0sCTgZtUNcUnwD/S8FQVGZwArgOO9Vibb/EvAQ81Ae8GwP6+nKyUzILeZba8xB0nTgA3SJYYNX12wz/cFHxeBgCMqvHgNznnycHF6nQYVZi0AP9i0/A6ADb9l0iP6vT/V6V/Xs+eIjPDsznTo8ZvbhpeB8DeUHANsFQQCdmVV5PTYKOm0Gl5/KWg519oKl4HwH6DOqp6PwIuVKlv0XZ7bIErZI2wTb3XXiN8nqxUvZF4FCgGzgKuVdcAOsDbwMXA4844cClwM3CjvK5r8GxQtwCPqmmycXgbADuFXU9WRR079988RnY31tfArQK/jLnFh4GcDl/JFnpKjQmNxOttbwrc7jQ0UKk7KZDTHLQ9vgHeBw4APzqbqkYfNgDnAlc5KYwqK+3IAkk/fwz4CPgQ+M4ZV4ISNkteN0NXCzBSU16iZgjUSH9AevuIeo/OloQWHTYAfzq3o4YK/jvwqewID8u6wM2gZIBNUWMCYIDPBLhKnv8bOCjL4IPAX85n0jajycFEMsKvBE4Hjsr6Huc0GAp0XgDscchBB+q+3KE8/gcMLN2RCb10fwAAAABJRU5ErkJggg==" },
            { "Line", "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAABoklEQVR42uXbu0oDQRiG4dfN3oUiVppKsBCsPRTegoXgpWnhLVhoqjQeCq0EU4noTYgGmx2QsKtJdg7/YZpppnlepv1WxnfPODg18AUMgXNgG3gCzmpH+E3gCtgApsAecFE7wW8BI2AV+AYGzb1bOcHftOAHwGPlBL/Wgn8DTmqn+A/gCJhUDvHvwAEwAerKIf4QeAnvK894gMoz3kKAXnjtAXrjNQeIgtcaIBpeY4CoeG0BouM1BUiC1xIgGV5DgKR46QGS4yUHyIKXGiAbXmKArHhpAbLjJQUogpcSoBheQoCi+NIBiuNLBhCBLxVADL5EAFH43AHE4XMGEInPFUAsPkcA0fjUAcTjUwZQgU8VQA0+RQBV+NgB1OFjBlCJjxVALT5GANX4vgHU4/sEMIFfNoAZ/DIBTOEXDWAOv0gAk/h5A5jFzxPANP6/AObxfwVwge8K4AbfFsAVfjaAO/zvAC7xIYBb/OyocNQxLTOLDz9gCFx7xIcAl8C6R3wIsNOxqzOPDwEeGvS0uV+BfQ/4EOAUuAU+gXvgmGZUaB0P8AOM2Kr/cshSYwAAAABJRU5ErkJggg==" },
            { "Arrow", "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAACtUlEQVR42u2bO4sTURSAv5lMLBRxBSuLrWy28IGVhShuIYii4KOxEPRv6F+x2k4t1GYLEc2KwoIIy4ILay1oZ0BY2U1mLOZcOAwJ5E4e3HP3XhgyGTIh33fOPXMmN8k+bX7nMI+cQz6SgCQgCUgCkoAkIJ6R+Z5QRACcy2MpW9QZ4IALoANUwBAYCPyJGDPARTgT0EpF+ihwAbgq20WgBzwUMaVVAR0BbkLkwApwGVgFLgHLjXNviJi+vEdlUcBQ7S8L6KqArzSmbqVEdYHPAp9bzAAXsWvAdeAKcB441njdQE0LVxPcsQ8qW0wJcBBPgOcjgHU9KMZMG4CPKjNMFsGDFp+zFDk/gS11zJQAF+U1YEcq+jngLHAGOD2BgC/AnmTD0PJl8KtsbiwBJ4FnwGOR1R1x3nvfjjDURsgVtgI4AvyRLLgtcN0R838IbPikf8gCSlXV94E7wDpwCviriqRrijJgV7YsBgFIlA8k6i8lygPgLvBUgbu5viH7nRjuBTT8K/X8AfAO+Af8GjP/zd8MjYO/D7yWmtAHfqhCvidXAHzvCHND8G/luQPcVhV/S3qAzLKASeB1k7QtsEO5A8R3/ofUB/jAl6pXcAFc92l/QxPgG3lX/b8Bj4Sh17gimBHgC08j0msjjpm5CrSFb3aAUwWxMAxPm5QPIQNmBW9yXSAo+EULCA5+kQKChF+UgGDhFyEgaPh5Cwgefp4CTMDPS4AZ+HkIMAU/awHm4GcpwCT8rASYhZ+FANPw0wowDz+NgCjg2wqIBr6NgKjgfQV0YoP3EZBTf/92i3qhMgr4SQW45aYl4AX12tw+9UKlaXifDMioV2R7wG/gHvDGOjxM9rV4pQTcBI5Tr852rMP7ZECllqX6qiaYH0XLn6OXRDJ8BVSQ/jCRBCQBSUASkAQkAZGM/wuDCZoi5JdFAAAAAElFTkSuQmCC" },
            { "Rect", "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAACTElEQVR42u2bvW4TQRSFv9ldpwFRQMErxPwICZ6AhleIQ5DiBtHnHaipaSBI/IWWDgqKVBGCgh/nGdKkQErj7K4pfEZMFmNBx47PSCPLM2vJ57t3Z+9K94T9gwmrPApWfFRL9oIAhZ5rnAGtPv8aQAk0mrmMcpGeakHU0YUlcBMYAhd6KvoHcAh8lqaQZMVvAEKyMQZ2gOuZRP8r8Ah42tVaJQsBGAC7wKbWTzuZ0cf7PyiQT4A7wDZQx/0UQAs8A0bAVHAGmWRAK9Ejfd+MZ0IloTWwlYgfCMpjYA84+tMp+h+PAFwGNoAH0jSVxrfAS6AK+weTIBpfgPXksNiS+BzGBvBCQSx1MN4AmkKL13TaN8qIXYlf0w+Kns5SGvakqZLGK8BVYBYrwWFyryBaRVILtD2d8f8X0pRqHKal8LlOaXy8rHrqaSV43NF43u8CBmAABmAABmAABmAABmAABmAABmAABmAABmAABmAABmAABmAABmAABmAABmAABmAABmAABmAABrAiAE4420R4Mem2zEFjAVzqaDyBX+3yhx0g94AP2i/pb8NktP3EZvBU4yQCCMA3QVjXxWPgPfA6gwxomLfHj6UtNkt/B0KMcA08BJ4rRUrgFXAbeEP/2+XvJy2zlbTWsV0+pkUr0SPmTpEyo0MyGibWlNVnDBOITsHcTjJLLDN1Yjuhx5aZKhG/nQScquOimgJ3gXesiGkqdKyzoZMRt8jDNvdJEV9qm0s3osnwoyarYpxMHx25WWebf/UOzzKzzvpdYNH4CbRuvpFe1QT7AAAAAElFTkSuQmCC" },
            { "Marker", "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" },
            { "Text", "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAENElEQVR42u2aPYjUQBTHf7MZz1ULv/CrURAsxMZvBe0sbAQ7QRBtBDvtLCzUSrATLEQtRBQFsRQbbURRRFQEEUUFP0BQ/Ly7wvN2E4u8geeQ3cS92Zj1EhhyySU7k/+89583//fM7fvPmMxHg0l+1ABMdgDsAIKRSAsKgAHiAZo4EwoEq1Bd4llEFY8YeCvnICBYoAmcB7aLG5gKmz7AIxnrpxAgWGADsGNATD+W8W4Hzsj4WxMFYJryf1NxANpyboYkwURMPxkAAIwaax0H1AAEDoS6EU9R8zQBgxhThkvakq2klI8KCcA48Dpn1txaPAtY1IFM3b2vwMeC6/diYMa/AiCWmf8ArATGugzarcV7gbOyVNmM5csCF4EDOet3Q/q/CWyRd6OaBPsIQLeNkDZnU5FdXxzaBcaV2SWeCwwpU00oL9hBzN6ICxg1Lhs6EHoA3JW/XXM+fE4AiEoEAK/vITWuIeA9cD3UFt4Cw0I2axW6EfANeOzF4GXG+xdkchZ529+nwJeQABjgJ3Cnn8JDQdOPvOvn0vxjSoBxJUDbKqJrZDwQl0x6Rbe246HjgKRkM8+ysjnANvHzfgNtSJWlG1WQwCKZ+WPAvpL7Xm8rJHXNEyBaJWiTifQxr0oiqP5wW5IbtGzF1J4sUIqsGBTgskYG0Rv7H+gVPjC2nx2UyQkjwGGJUYwy2xhYABxV952u+QQ4JdYReyS7GdilgqrKARB7bQQ40eHZ+cAR9SEOtBfA6Q7vDAM7/e11lQCYJrM4Va5nisgyqmbabdrmdPiNqfJNkeICpz3MlvtRVTngArBMEdw14IcX8zsA2jnRpCZDZ1E3gPsCnhGl66GtULLjqrTIC8OTAK4F8BLYqCygXTUX8Ge3EXgvYjzATRVJ0PRL+VGW9Adx2oougaX1UYuiNQA1ADUANQA1ADUANQDUFSK9Fzo0/qK+0HgpuOClr2UA4CdSf1E8WdGSd391+M2BACAW8WK+kpdbcp13zCKt/HDvGNnzfx8EC3D1eeuAy6RJS6PUmm6Stru3m1SW0lreMLAfuOIpOWQkUCKVIu8mikZKSIlDkaAD4ACwlFR+aqqzLZgF0u80SQXOQwW2wG1xnRbwucMzY/L/MeVuwSzA+fyQEi5Mjq6ftx2NPQJNuggZe4AV0u9cr3LFTeQq4LhcjwOXSNPpHTmmVw5oZAgYvSRATE4K3vWznLRgIu83lwEH1f01wNZu47Q9fkAccPkqUqM8XZm0KZAZctK3zRNabA8z1wpYXJ1kWEQygYyPc4dIzs2QHOB86CSwyStdYQI64Ij4rTP5dgZA70hV3aUF+3RjvZXHT70AcE/IaMEErcAlOr4rVm93sJBPwGpg4V+A3gbe5NU49RoJjkoLLYl3c5NR4FVVIsGQRc9FapGSHvuM+7UZ+hcbmL70WesBNQCT/PgNp7kjdyFChskAAAAASUVORK5CYII=" },
            { "Blur", "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" },
            { "Undo", "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" },
            { "Copy", "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAC3klEQVR42u2bv2sUQRTHPzu7rGhpr4IYFY0INlYGrCOK2Ah21hZaWFjaW1jkbxALf4CQyk4rizSiQUiaRHsb8XTJ7VrcW31ucqd39zY3szcPBnbvipn5zJvZ9515k7x9t05L5qT4YKWUXZa1UFkiZWilMzIHVFJaA+Ck0xVwEjgNHGxWuo+WAN+BT8BGo43mAOpRPwo8BpaB3JPRL4BV4B6wJW2tLAHU7nUEeAMck9/7ngDIgevABeAS8KX2BEsPqIAV6XwhlaYerQGFtG0FuCZeYLJK13PqBHBFnnP8s1zatixtLQHnjOY+wKKaCr5aJV65WA+eJYCccCzX7mtJNzizBJCqGCAYy4zCTCTQqIHuqKlh7WGmIbYzAuCANeCRPGfiEZYlk+Isw+zMUGwkwH3gKXC8sUBaDNRh4BxwGTilAq3UBwC1i9aesNbyCn4VeAicmRaCa0F2OuWubZQCeAZcBJ5I5/tteIBT0nbWwUul5nwinf4G3JJ23pzUE7J/6HkfP9mlfGXq99sichaUB04MQOv5BaXnZ2m9IXq+lPb3gAfA80kGLQtMz98FtpWe78vzK2BTi5xxAQSr56UPO8DraQAEq+eVvZ9m5zZYPa/+/zpJ8KXj6pD0/Nk9OltNGwiFpOcPWIXajjm3CCACiAAigAggAogAIoAIIAKIACKACGCOLJl3AD/mFUDCYKP3o7yXrmPTeVciJH9vqjoG2+ubbWSJMcM0WBgcnCTSsWZ+Qspgy28LuNO1NeB/8hMK4CWwBHzWR39ZR9x/VH5Cb79SZfFgy3xUfoJrnDJ3DoCeDm7E8TpdBsC4+UMxFI4AIoAIIAKIACKA32opFPtplaKv4+IPKusqFD1vBsCJRl6VZx+9YU89j2G2eCJa+Tz+pckN1fOW6aeIVl4CXgjx1JPS1PO0cV+glpPbwA3+pMoe8vnqq/WFiVIlS2+oSn3a8yvbvjNUKW9wIUrccewXuf/E23dhnIUAAAAASUVORK5CYII=" },
            { "Folder", "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAACcElEQVR42u2bvWsVQRDAf7N3Bi0EwUIhWkWwiLWdNuIfYGHnR2Ul2EaeFqbxo9U/QNFS7awCNtbB4qmNjYW+wipJF3Lvbi1uFjYPnoJ5e3dkZ+BY3j24ufnt7M3sMiN+PCJncWQuBiB3AOUcKA7wQKPjQUX0mQLUC3pmEgCFvmCzYD1enzurZzAARMcaOAtcBy4By8CSGiD/abgAu8AP4CPwHthSj2j6BiB+PJLoZdeA+8CJhDonwEPg1RA8oYxm4g1wQ+9PoxmXBS2BMC4DL4FzCqJXCKUqf6TG7wFH5nwcFyWN6nwAfAde9wlB/Hh0Afgcff2lA71h7W8B54Ed1ZsqOsyNaA64p7NOR8YTLbuTwDVdcpWOKa4QeotZG0vgqv7ZdVIUco07+pJFovBbAT+BL+px+8JwqR8lob8s9KJeqeU38A54opGoAGoXuX9f0iR0/WmU2J0C7gKbwBW9X5QwiP1IF8vPK5DTwAdN9DZz2gyJevsUOKqJ2DGX6QawBlaBW7luh0POcbsk73OQ1dwPRI7nDkDsTNAAGAADYAAMgAEwAAbAABgAA2AADIABMAAGwAAYAANgAAyAATAABsAAGIB8AFQZ21852oopz4BK2OmmXsgDEwds0FZMNBkBaNTmDQe8iJaBz2T2UZufO+Ar8Ji2cLA65BBC5WihNn8r9cc6sEK6cvkhzLinrRBbom0NWA+FkmHt31RvSN0w0adsA0+BZ6FgW7RxMu4aOUPbMnOZg7fM9D3zQtsDMQE+AW+BX7G9MtM5OphmpoSyz8bZOsE6UdscPRdFuijU1//qG2wOWU7g/+bVthcwAJnLH6HzwSIFHQ25AAAAAElFTkSuQmCC" },
            { "Save", "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAECklEQVR42u2byWsUQRSHv14yBpNoEhDEgzeFiJ5U0OhFyUlBcIuak0EEDyIBQfwHBI85CiKCkLhEzcmTwVtUxGMgGNCTiMdoXLJMT3uY16RoZqaXqRprSD949Ez18qp+9eottTg3JkJSkCtsE4VARa65yU+47whXhG0kt5m6+QmND4X3AbtSANbK3v8KvJPG5wbBb9B4gG7gPnBBKbOJ3gOXgU/SlnIe9WnU+w+Ai4Ju0ICzUKXOO0GNXgwS+BDwBhiQxvs6NCBSp73AsHzYq6MBkfqNA2/luUaq2CXPbpHnx6V8DBgEfgI3gR/yf0yRUYvKwA5gBhgC5rNqQiMABkQLnAbqH1ngGeBVSo27KwB8Aaak/JQ0eBl4BKwCfwWAMKH+QTMguCk8QBrqEaGdco1zh1z7lW+WlPslRWa/lPWklO3FQNiTZTjo8u0VEZrEQUx7ovIwNu7LGa26CsLrLCDoAsDJwEla5mTUvqZA0AXAivTimhI7qFxR7qtao96PKPrGSo56xEGIvIObNxJMSyPABzFetcLTyK32Kj3bCfQpv6MO6ZP/IznrErcJx4CFesFSswB4cj0PnBAAkobKVvl9EvisuEfEAH4Uo9kdk5G1XpGLnAAO17MpvkYj2KU0JA2VFOuvAtQXizGaCfMD4ABwUMJmLx6E6bIBbp2xX4vjcUS9Mldj3rAzFuJr1wBy5gqOpu+QIqkz6gXalgoACgAKAAoACgAKAAoACgAKAAoACgA2Jvmal6taSY5NAFT+gzZpkelrHEq/SZ4S00Ulss0+GQMgoDrNNEV1FWdVx5p9gtq7AsA41bnIIOe8oVYNmAS+tXgITAoAVgyBTdI7uZaoc9S5LDKtMYKhgcnMNLKscoOqXWgX160dgAA4A5zLub6X1OvPgWkbAXCUZfLHNRY8dNFZYBvVjRSObTYA4A/wEDit2R5UpMHTIgNbbUAIXANuo3+BIwQWm/H5rQAgClQWDSZvju3ZoMmkKLTapSjWutfgEAhtHwL3DBvB6za7wc3AqEE3OArcstkNLgGXDAdCSza7QQ94KWw6GcLWXMDX7atN5hkmjOCa4ZS4w2YAysB24KghGzALfLfZC3Syvn3dBM0D+6lupHY24rqAtZFgVLFl4DhwRHPcHm2nnRUZ1rpBX8boC8NGcM3mXMAx7AatzAVc5fCDSfIUGa5pALLMvC4phx9MU1mRmcWFpgagorgc9exgo22tQ5IIeS04YOnKUBhMWCRV6z0fa9t6A+ocnY321j+lenIsSLltvtWUpl7PqJ57zHReIELuirw0bGnM4CXMITwBrjbaMJ0EwC9Jb+8Au7Hn6GwaO7EAzCXtGPdTuDRHPjQHbbkDJrMRrHd4od3C5lQn3v8BekctSR9e9FwAAAAASUVORK5CYII=" },
        };

        static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();

        // Icon at the requested size (each has its own pastel color), scaled cleanly for DPI
        public static Bitmap Get(string name, int size)
        {
            string key = name + "@" + size;
            Bitmap b;
            if (cache.TryGetValue(key, out b)) return b;
            using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(Data[name])))
            using (Bitmap src = new Bitmap(ms))
            {
                b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(src, new Rectangle(0, 0, size, size));
                }
            }
            cache[key] = b;
            return b;
        }
    }

    // ---------------- Annotations ----------------

    abstract class Shape
    {
        public Color Color;
        public float Width;
        public abstract void Draw(Graphics g, Bitmap source);
        public virtual void Update(Point p) { }
        public virtual bool IsEmpty { get { return false; } }
    }

    class PenShape : Shape
    {
        public List<Point> Points = new List<Point>();
        public bool Marker;
        public float MinStep = 3f;
        public override void Update(Point p)
        {
            Point last = Points[Points.Count - 1];
            float dx = p.X - last.X, dy = p.Y - last.Y;
            if (dx * dx + dy * dy < MinStep * MinStep) return; // ignore tiny mouse jitter
            Points.Add(p);
        }
        public override bool IsEmpty { get { return Points.Count < 2; } }
        public override void Draw(Graphics g, Bitmap source)
        {
            if (Points.Count < 2) return;
            Color c = Marker ? Color.FromArgb(110, Color) : Color;
            using (Pen pen = new Pen(c, Marker ? Width * 4 : Width))
            {
                pen.StartCap = Marker ? LineCap.Square : LineCap.Round;
                pen.EndCap = Marker ? LineCap.Square : LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                if (Points.Count < 3) g.DrawLines(pen, Points.ToArray());
                else g.DrawCurve(pen, Points.ToArray(), 0.5f); // smooth curve instead of a polyline
            }
        }
    }

    abstract class TwoPointShape : Shape
    {
        public Point A, B;
        public override void Update(Point p) { B = p; }
        public override bool IsEmpty { get { return Math.Abs(A.X - B.X) < 3 && Math.Abs(A.Y - B.Y) < 3; } }
        protected Rectangle Rect
        {
            get { return Rectangle.FromLTRB(Math.Min(A.X, B.X), Math.Min(A.Y, B.Y), Math.Max(A.X, B.X), Math.Max(A.Y, B.Y)); }
        }
    }

    class RectShape : TwoPointShape
    {
        public override void Draw(Graphics g, Bitmap source)
        {
            using (Pen pen = new Pen(Color, Width)) { pen.LineJoin = LineJoin.Round; g.DrawRectangle(pen, Rect); }
        }
    }

    class LineShape : TwoPointShape
    {
        public override void Draw(Graphics g, Bitmap source)
        {
            using (Pen pen = new Pen(Color, Width)) { pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; g.DrawLine(pen, A, B); }
        }
    }

    class ArrowShape : TwoPointShape
    {
        public override void Draw(Graphics g, Bitmap source)
        {
            using (Pen pen = new Pen(Color, Width))
            {
                pen.StartCap = LineCap.Round;
                float head = Width < 4 ? 5f : Width < 10 ? 4f : 3f; // arrowhead scales with line width
                pen.CustomEndCap = new AdjustableArrowCap(head, head, true);
                g.DrawLine(pen, A, B);
            }
        }
    }

    class BlurShape : TwoPointShape
    {
        public override void Draw(Graphics g, Bitmap source)
        {
            Rectangle r = Rectangle.Intersect(Rect, new Rectangle(Point.Empty, source.Size));
            if (r.Width < 2 || r.Height < 2) return;
            int block = Math.Max(3, (int)(Width * 4)); // here Width = blur strength 1..10
            int sw = Math.Max(1, r.Width / block), sh = Math.Max(1, r.Height / block);
            using (Bitmap small = new Bitmap(sw, sh))
            {
                using (Graphics sg = Graphics.FromImage(small))
                {
                    sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    sg.DrawImage(source, new Rectangle(0, 0, sw, sh), r, GraphicsUnit.Pixel);
                }
                InterpolationMode old = g.InterpolationMode;
                PixelOffsetMode oldp = g.PixelOffsetMode;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(small, r, new Rectangle(0, 0, sw, sh), GraphicsUnit.Pixel);
                g.InterpolationMode = old;
                g.PixelOffsetMode = oldp;
            }
        }
    }

    class TextShape : Shape
    {
        public Point At;
        public string Text = "";
        public override bool IsEmpty { get { return string.IsNullOrEmpty(Text); } }
        public float FontSize { get { return 12f + Width * 2.5f; } }
        public override void Draw(Graphics g, Bitmap source)
        {
            if (string.IsNullOrEmpty(Text)) return;
            using (Font f = new Font("Segoe UI", FontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Brush b = new SolidBrush(Color))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.DrawString(Text, f, b, At);
            }
        }
    }

    enum Tool { Select, Pen, Line, Arrow, Rect, Marker, Text, Blur }

    // ---------------- Capture overlay ----------------

    class CaptureForm : Form
    {
        readonly Bitmap shot;      // capture of all monitors
        readonly Bitmap dimmed;    // the same capture, dimmed
        readonly Rectangle virt;
        readonly TrayApp app;
        readonly float scale;

        Rectangle sel = Rectangle.Empty;
        bool selecting;
        bool spaceMove;
        int resizeEdges;       // edges being dragged: 1 left, 2 top, 4 right, 8 bottom (0 = none); Space held while selecting moves the frame instead of resizing it
        Point dragEnd;         // current selection corner (under the cursor)
        Point dragStart;
        Tool tool = Tool.Select;
        Color color;
        int penWidth = 4;      // 1..30 px
        int blurStrength = 4;  // 1..10
        readonly List<Shape> shapes = new List<Shape>();
        Shape current;
        TextBox textBox;
        TextShape textShape;

        FlowLayoutPanel bar;
        readonly Dictionary<Tool, Button> toolButtons = new Dictionary<Tool, Button>();
        Button colorButton;
        TextBox sizeBox;
        Label sizeUnit;
        readonly ToolTip tips = new ToolTip();

        public CaptureForm(TrayApp app)
        {
            this.app = app;
            virt = SystemInformation.VirtualScreen;
            shot = new Bitmap(virt.Width, virt.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(shot))
                g.CopyFromScreen(virt.Location, Point.Empty, virt.Size, CopyPixelOperation.SourceCopy);
            dimmed = new Bitmap(shot);
            using (Graphics g = Graphics.FromImage(dimmed))
            using (Brush b = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
                g.FillRectangle(b, 0, 0, dimmed.Width, dimmed.Height);

            color = Color.FromArgb(Settings.ColorArgb);
            penWidth = Math.Max(1, Math.Min(30, Settings.PenWidth));
            blurStrength = Math.Max(1, Math.Min(10, Settings.BlurStrength));
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Bounds = virt;
            TopMost = true;
            ShowInTaskbar = false;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            using (Graphics g = CreateGraphics()) scale = Math.Max(1f, g.DpiX / 96f);
            BuildToolbar();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_DPICHANGED) { m.Result = IntPtr.Zero; return; } // don't let Windows rescale the window
            base.WndProc(ref m);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bounds = virt;
            Activate();
            Native.SetForegroundWindow(Handle);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { shot.Dispose(); dimmed.Dispose(); tips.Dispose(); }
            base.Dispose(disposing);
        }

        // ---------- toolbar ----------
        static readonly Color IconColor = Color.FromArgb(0x6E, 0xA5, 0xFF);
        static readonly Color BtnBack = Color.FromArgb(44, 44, 48);
        static readonly Color BtnActive = Color.FromArgb(36, 56, 92);

        Button MakeIconButton(string icon, string tip, EventHandler click)
        {
            Button b = MakeButton("", tip, click);
            b.Image = Icons.Get(icon, (int)(20 * scale));
            b.ImageAlign = ContentAlignment.MiddleCenter;
            return b;
        }

        Button MakeButton(string text, string tip, EventHandler click)
        {
            int s = (int)(34 * scale);
            Button b = new Button();
            b.Text = text;
            b.Width = s; b.Height = s;
            b.Margin = new Padding((int)(2 * scale));
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(70, 70, 76);
            b.BackColor = BtnBack;
            b.ForeColor = IconColor;
            b.Font = new Font("Segoe UI Symbol", 14f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            b.Cursor = Cursors.Hand;
            b.TabStop = false;
            b.Click += click;
            tips.SetToolTip(b, tip);
            return b;
        }

        void AddTool(Tool t, string icon, string tip)
        {
            Button b = MakeIconButton(icon, tip, delegate { SetTool(t); });
            toolButtons[t] = b;
            bar.Controls.Add(b);
        }

        void BuildToolbar()
        {
            bar = new FlowLayoutPanel();
            bar.AutoSize = true;
            bar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            bar.WrapContents = false;
            bar.BackColor = Color.FromArgb(32, 32, 35);
            bar.Padding = new Padding((int)(3 * scale));
            bar.Visible = false;
            bar.Cursor = Cursors.Default;

            AddTool(Tool.Select, "Select", L.T("Змінити виділення", "Reselect area"));
            AddTool(Tool.Pen, "Pen", L.T("Олівець", "Pen"));
            AddTool(Tool.Line, "Line", L.T("Лінія", "Line"));
            AddTool(Tool.Arrow, "Arrow", L.T("Стрілка", "Arrow"));
            AddTool(Tool.Rect, "Rect", L.T("Рамка", "Rectangle"));
            AddTool(Tool.Marker, "Marker", L.T("Маркер", "Highlighter"));
            AddTool(Tool.Text, "Text", L.T("Текст", "Text"));
            AddTool(Tool.Blur, "Blur", L.T("Розмиття (пікселізація)", "Blur (pixelate)"));

            colorButton = MakeButton("", L.T("Колір", "Color"), delegate { PickColor(); });
            colorButton.BackColor = color;
            bar.Controls.Add(colorButton);
            bar.Controls.Add(MakeButton("−", L.T("Менше (або колесо миші вниз)", "Smaller (or scroll down)"), delegate { ChangeSize(-1); }));
            sizeBox = new TextBox();
            sizeBox.Width = (int)(40 * scale);
            sizeBox.Margin = new Padding((int)(2 * scale), (int)(7 * scale), 0, 0);
            sizeBox.TextAlign = HorizontalAlignment.Center;
            sizeBox.BorderStyle = BorderStyle.FixedSingle;
            sizeBox.BackColor = Color.FromArgb(20, 20, 22);
            sizeBox.ForeColor = Color.White;
            sizeBox.MaxLength = 2;
            sizeBox.Font = new Font("Segoe UI", 14f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            sizeBox.KeyPress += delegate (object o, KeyPressEventArgs ke) { if (!char.IsDigit(ke.KeyChar) && ke.KeyChar != '\b') ke.Handled = true; };
            sizeBox.Leave += delegate { ApplySizeBox(); };
            tips.SetToolTip(sizeBox, L.T("Впишіть число й натисніть Enter. Або крутіть колесо миші над скріншотом.", "Type a number and press Enter, or scroll the mouse wheel over the screenshot."));
            bar.Controls.Add(sizeBox);
            sizeUnit = new Label();
            sizeUnit.AutoSize = false;
            sizeUnit.Width = (int)(40 * scale);
            sizeUnit.Height = (int)(34 * scale);
            sizeUnit.Margin = new Padding(0, (int)(2 * scale), 0, 0);
            sizeUnit.TextAlign = ContentAlignment.MiddleLeft;
            sizeUnit.ForeColor = Color.FromArgb(170, 170, 176);
            sizeUnit.Font = new Font("Segoe UI", 12f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            bar.Controls.Add(sizeUnit);
            bar.Controls.Add(MakeButton("+", L.T("Більше (або колесо миші вгору)", "Bigger (or scroll up)"), delegate { ChangeSize(1); }));

            bar.Controls.Add(MakeIconButton("Undo", L.T("Скасувати (Ctrl+Z)", "Undo (Ctrl+Z)"), delegate { Undo(); }));
            bar.Controls.Add(MakeIconButton("Copy", L.T("Копіювати в буфер (Ctrl+C)", "Copy to clipboard (Ctrl+C)"), delegate { CopyToClipboard(); }));
            bar.Controls.Add(MakeIconButton("Save", L.T("Зберегти в папку (Ctrl+S)", "Save to folder (Ctrl+S)"), delegate { SaveToFolder(); }));
            bar.Controls.Add(MakeIconButton("Folder", L.T("Зберегти як… (Ctrl+Shift+S)", "Save as… (Ctrl+Shift+S)"), delegate { SaveAs(); }));
            Button closeBtn = MakeButton("✕", L.T("Закрити (Esc)", "Close (Esc)"), delegate { Close(); });
            closeBtn.ForeColor = Color.FromArgb(0xFF, 0x8A, 0x8A);
            bar.Controls.Add(closeBtn);
            Controls.Add(bar);
            SetTool(Tool.Pen);
        }

        void SetTool(Tool t)
        {
            CommitText();
            tool = t;
            foreach (KeyValuePair<Tool, Button> kv in toolButtons)
                kv.Value.BackColor = kv.Key == t ? BtnActive : BtnBack;
            Cursor = t == Tool.Text ? Cursors.IBeam : Cursors.Cross;
            UpdateSizeLabel();
        }

        void PickColor()
        {
            CommitText();
            using (ColorDialog d = new ColorDialog())
            {
                d.Color = color;
                d.FullOpen = true;
                TopMost = false;
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    color = d.Color;
                    colorButton.BackColor = color;
                    Settings.ColorArgb = color.ToArgb();
                }
                TopMost = true;
            }
        }

        void ChangeSize(int delta)
        {
            if (tool == Tool.Blur)
            {
                blurStrength = Math.Max(1, Math.Min(10, blurStrength + delta));
                Settings.BlurStrength = blurStrength;
            }
            else
            {
                int step = penWidth >= 10 ? 2 : 1;
                penWidth = Math.Max(1, Math.Min(30, penWidth + delta * step));
                Settings.PenWidth = penWidth;
            }
            UpdateSizeLabel();
        }

        void UpdateSizeLabel()
        {
            if (sizeBox == null) return;
            sizeBox.Text = (tool == Tool.Blur ? blurStrength : penWidth).ToString(CultureInfo.InvariantCulture);
            sizeUnit.Text = tool == Tool.Blur ? L.T("сила", "level") : "px";
            tips.SetToolTip(sizeBox, tool == Tool.Blur
                ? L.T("Сила розмиття 1–10. Впишіть число й натисніть Enter або крутіть колесо миші.", "Blur strength 1–10. Type a number and press Enter, or scroll the mouse wheel.")
                : L.T("Товщина 1–30 px. Впишіть число й натисніть Enter або крутіть колесо миші.", "Width 1–30 px. Type a number and press Enter, or scroll the mouse wheel."));
        }

        void ApplySizeBox()
        {
            int v;
            if (int.TryParse(sizeBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
            {
                if (tool == Tool.Blur) { blurStrength = Math.Max(1, Math.Min(10, v)); Settings.BlurStrength = blurStrength; }
                else { penWidth = Math.Max(1, Math.Min(30, v)); Settings.PenWidth = penWidth; }
            }
            UpdateSizeLabel(); // show the accepted (clamped) value
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (sel.IsEmpty || textBox != null || selecting) return;
            ChangeSize(e.Delta > 0 ? 1 : -1);
            if (current != null) // shape still being drawn (left button held) — change its width/strength right away
            {
                current.Width = current is BlurShape ? blurStrength : penWidth * scale;
                Invalidate(sel);
            }
        }

        void PlaceToolbar()
        {
            if (sel.IsEmpty) { bar.Visible = false; return; }
            bar.Visible = true;
            Size s = bar.PreferredSize;
            int gap = (int)(6 * scale);
            int x = sel.Right - s.Width;
            int y = sel.Bottom + gap;
            if (y + s.Height > ClientSize.Height) y = sel.Top - s.Height - gap;
            if (y < 0) y = sel.Bottom - s.Height - gap;
            x = Math.Max(0, Math.Min(x, ClientSize.Width - s.Width));
            bar.Location = new Point(x, y);
            bar.BringToFront();
        }

        // ---------- painting ----------
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.DrawImageUnscaled(dimmed, 0, 0);
            if (!sel.IsEmpty)
            {
                g.DrawImage(shot, sel, sel, GraphicsUnit.Pixel);
                GraphicsState st = g.Save();
                g.SetClip(sel);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                foreach (Shape s in shapes) s.Draw(g, shot);
                if (current != null) current.Draw(g, shot);
                g.Restore(st);

                using (Pen p = new Pen(Color.FromArgb(0, 120, 215), Math.Max(1f, scale)))
                {
                    p.DashStyle = DashStyle.Dash;
                    g.DrawRectangle(p, sel.X, sel.Y, sel.Width - 1, sel.Height - 1);
                }
                if (!selecting) // resize handles: corners and side midpoints
                {
                    int hs = (int)Math.Max(6, 7 * scale);
                    int cx = sel.X + sel.Width / 2, cy = sel.Y + sel.Height / 2;
                    Point[] hp = {
                        new Point(sel.Left, sel.Top), new Point(cx, sel.Top), new Point(sel.Right - 1, sel.Top),
                        new Point(sel.Left, cy), new Point(sel.Right - 1, cy),
                        new Point(sel.Left, sel.Bottom - 1), new Point(cx, sel.Bottom - 1), new Point(sel.Right - 1, sel.Bottom - 1) };
                    using (Brush hb = new SolidBrush(Color.FromArgb(0x6E, 0xA5, 0xFF)))
                    using (Pen hpen = new Pen(Color.FromArgb(20, 20, 22), 1f))
                        foreach (Point q in hp)
                        {
                            Rectangle hr = new Rectangle(q.X - hs / 2, q.Y - hs / 2, hs, hs);
                            g.FillRectangle(hb, hr);
                            g.DrawRectangle(hpen, hr);
                        }
                }
                string label = sel.Width + " × " + sel.Height;
                using (Font f = new Font("Segoe UI", 12f * scale, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    SizeF ts = g.MeasureString(label, f);
                    float lx = sel.X, ly = sel.Y - ts.Height - 4;
                    if (ly < 0) ly = sel.Y + 4;
                    using (Brush bg = new SolidBrush(Color.FromArgb(200, 20, 20, 22)))
                        g.FillRectangle(bg, lx, ly, ts.Width + 6, ts.Height);
                    g.DrawString(label, f, Brushes.White, lx + 3, ly);
                }
            }
            else
            {
                string hint = L.T("Виділіть область мишею.  Esc — скасувати", "Drag to select an area.  Esc to cancel");
                using (Font f = new Font("Segoe UI", 16f * scale, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    Screen scr = Screen.FromPoint(Cursor.Position);
                    Rectangle b = scr.Bounds;
                    b.Offset(-virt.X, -virt.Y);
                    SizeF ts = g.MeasureString(hint, f);
                    float x = b.X + (b.Width - ts.Width) / 2, y = b.Y + b.Height * 0.08f;
                    using (Brush bg = new SolidBrush(Color.FromArgb(200, 20, 20, 22)))
                        g.FillRectangle(bg, x - 10, y - 6, ts.Width + 20, ts.Height + 12);
                    g.DrawString(hint, f, Brushes.White, x, y);
                }
            }
        }

        // ---------- mouse ----------
        const int EL = 1, ET = 2, ER = 4, EB = 8;

        int HitEdges(Point p)
        {
            if (sel.IsEmpty || selecting) return 0;
            int m = (int)Math.Max(5, 7 * scale);
            Rectangle outer = Rectangle.Inflate(sel, m, m);
            if (!outer.Contains(p)) return 0;
            int r = 0;
            if (Math.Abs(p.X - sel.Left) <= m) r |= EL;
            else if (Math.Abs(p.X - sel.Right) <= m) r |= ER;
            if (Math.Abs(p.Y - sel.Top) <= m) r |= ET;
            else if (Math.Abs(p.Y - sel.Bottom) <= m) r |= EB;
            return r;
        }

        static Cursor EdgeCursor(int e)
        {
            if (e == (EL | ET) || e == (ER | EB)) return Cursors.SizeNWSE;
            if (e == (ER | ET) || e == (EL | EB)) return Cursors.SizeNESW;
            if ((e & (EL | ER)) != 0) return Cursors.SizeWE;
            if ((e & (ET | EB)) != 0) return Cursors.SizeNS;
            return null;
        }

        Cursor ToolCursor { get { return tool == Tool.Text ? Cursors.IBeam : Cursors.Cross; } }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right) { Close(); return; }
            if (e.Button != MouseButtons.Left) return;
            if (sizeBox.Focused) { ApplySizeBox(); Focus(); }
            CommitText();

            int edges = HitEdges(e.Location);
            if (edges != 0) // dragging an edge or corner of the finished selection
            {
                resizeEdges = edges;
                bar.Visible = false;
                return;
            }

            bool inside = !sel.IsEmpty && sel.Contains(e.Location);
            if (sel.IsEmpty || tool == Tool.Select || !inside)
            {
                if (!sel.IsEmpty && !inside && shapes.Count > 0 && tool != Tool.Select) return; // don't lose drawings to a stray click
                selecting = true;
                spaceMove = false;
                dragStart = e.Location;
                dragEnd = e.Location;
                sel = Rectangle.Empty;
                bar.Visible = false;
                Invalidate();
                return;
            }

            switch (tool)
            {
                case Tool.Pen:
                case Tool.Marker:
                    PenShape ps = new PenShape();
                    ps.Marker = tool == Tool.Marker;
                    ps.Points.Add(e.Location);
                    current = ps;
                    break;
                case Tool.Line: current = new LineShape(); break;
                case Tool.Arrow: current = new ArrowShape(); break;
                case Tool.Rect: current = new RectShape(); break;
                case Tool.Blur: current = new BlurShape(); break;
                case Tool.Text: StartText(e.Location); return;
            }
            if (current == null) return;
            current.Color = color;
            current.Width = tool == Tool.Blur ? blurStrength : penWidth * scale;
            PenShape pss = current as PenShape;
            if (pss != null) pss.MinStep = Math.Max(2f, 2.5f * scale);
            TwoPointShape tp = current as TwoPointShape;
            if (tp != null) { tp.A = e.Location; tp.B = e.Location; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (resizeEdges != 0)
            {
                int l = sel.Left, t = sel.Top, r = sel.Right, b = sel.Bottom;
                int x = Math.Max(0, Math.Min(ClientSize.Width, e.X)), y = Math.Max(0, Math.Min(ClientSize.Height, e.Y));
                if ((resizeEdges & EL) != 0) l = x;
                if ((resizeEdges & ER) != 0) r = x;
                if ((resizeEdges & ET) != 0) t = y;
                if ((resizeEdges & EB) != 0) b = y;
                if (l > r) { int tmp = l; l = r; r = tmp; if ((resizeEdges & (EL | ER)) != 0) resizeEdges ^= EL | ER; }
                if (t > b) { int tmp = t; t = b; b = tmp; if ((resizeEdges & (ET | EB)) != 0) resizeEdges ^= ET | EB; }
                sel = Rectangle.FromLTRB(l, t, Math.Max(r, l + 1), Math.Max(b, t + 1));
                Cursor = EdgeCursor(resizeEdges) ?? Cursors.SizeAll;
                Invalidate();
                return;
            }
            if (!selecting && current == null && !spaceMove)
            {
                Cursor ec = EdgeCursor(HitEdges(e.Location));
                Cursor want = ec ?? ToolCursor;
                if (Cursor != want) Cursor = want;
            }
            if (selecting)
            {
                if (spaceMove)
                {
                    // shift both corners by the same amount so the frame keeps its size
                    int dx = e.X - dragEnd.X, dy = e.Y - dragEnd.Y;
                    int left = Math.Min(dragStart.X, dragEnd.X), top = Math.Min(dragStart.Y, dragEnd.Y);
                    int right = Math.Max(dragStart.X, dragEnd.X), bottom = Math.Max(dragStart.Y, dragEnd.Y);
                    dx = Math.Max(-left, Math.Min(ClientSize.Width - right, dx));
                    dy = Math.Max(-top, Math.Min(ClientSize.Height - bottom, dy));
                    dragStart.Offset(dx, dy);
                    dragEnd.Offset(dx, dy);
                }
                else dragEnd = e.Location;
                sel = Rectangle.FromLTRB(Math.Min(dragStart.X, dragEnd.X), Math.Min(dragStart.Y, dragEnd.Y),
                                         Math.Max(dragStart.X, dragEnd.X), Math.Max(dragStart.Y, dragEnd.Y));
                sel.Intersect(ClientRectangle);
                Invalidate();
            }
            else if (current != null)
            {
                Point p = new Point(Math.Max(sel.Left, Math.Min(sel.Right, e.X)), Math.Max(sel.Top, Math.Min(sel.Bottom, e.Y)));
                current.Update(p);
                Invalidate(sel);
            }
            else if (sel.IsEmpty)
            {
                Invalidate(); // the hint follows the cursor to its monitor
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (resizeEdges != 0)
            {
                resizeEdges = 0;
                if (sel.Width < 4 || sel.Height < 4) sel = Rectangle.Inflate(new Rectangle(sel.Location, new Size(1, 1)), 2, 2);
                Cursor = EdgeCursor(HitEdges(e.Location)) ?? ToolCursor;
                PlaceToolbar();
                Invalidate();
                return;
            }
            if (selecting)
            {
                selecting = false;
                spaceMove = false;
                Cursor = tool == Tool.Text ? Cursors.IBeam : Cursors.Cross;
                if (sel.Width < 4 || sel.Height < 4) sel = Rectangle.Empty;
                else if (tool == Tool.Select) SetTool(Tool.Pen);
                PlaceToolbar();
                Invalidate();
            }
            else if (current != null)
            {
                if (!current.IsEmpty) shapes.Add(current);
                current = null;
                Invalidate(sel);
            }
        }

        // ---------- text ----------
        void StartText(Point at)
        {
            textShape = new TextShape();
            textShape.Color = color;
            textShape.Width = penWidth * scale;
            textShape.At = at;
            textBox = new TextBox();
            textBox.BorderStyle = BorderStyle.FixedSingle;
            textBox.Multiline = true;
            textBox.Font = new Font("Segoe UI", textShape.FontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            textBox.ForeColor = color;
            textBox.BackColor = Color.White;
            textBox.Location = at;
            textBox.Width = Math.Max(120, sel.Right - at.X - 4);
            textBox.Height = (int)(textShape.FontSize * 1.6f);
            textBox.KeyDown += delegate (object s, KeyEventArgs ke)
            {
                if (ke.KeyCode == Keys.Enter && !ke.Shift) { ke.SuppressKeyPress = true; CommitText(); }
                else if (ke.KeyCode == Keys.Escape) { ke.SuppressKeyPress = true; CancelText(); }
            };
            textBox.TextChanged += delegate
            {
                int lines = Math.Max(1, textBox.Lines.Length);
                textBox.Height = (int)(textShape.FontSize * 1.4f * lines + textShape.FontSize * 0.4f);
            };
            Controls.Add(textBox);
            textBox.BringToFront();
            textBox.Focus();
        }

        void CommitText()
        {
            if (textBox == null) return;
            textShape.Text = textBox.Text;
            if (!textShape.IsEmpty) shapes.Add(textShape);
            RemoveTextBox();
        }

        void CancelText() { RemoveTextBox(); }

        void RemoveTextBox()
        {
            if (textBox == null) return;
            TextBox tb = textBox;
            textBox = null;
            textShape = null;
            Controls.Remove(tb);
            tb.Dispose();
            Focus();
            Invalidate();
        }

        void Undo()
        {
            if (textBox != null) { CancelText(); return; }
            if (shapes.Count > 0) { shapes.RemoveAt(shapes.Count - 1); Invalidate(); }
        }

        // ---------- keyboard ----------
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && selecting)
            {
                if (!spaceMove)
                {
                    spaceMove = true;
                    dragEnd = PointToClient(Cursor.Position);
                    // the cursor may have drifted — keep the frame corner where it was
                    dragEnd = new Point(
                        sel.IsEmpty ? dragEnd.X : (dragStart.X <= sel.Left ? sel.Right : sel.Left),
                        sel.IsEmpty ? dragEnd.Y : (dragStart.Y <= sel.Top ? sel.Bottom : sel.Top));
                    Cursor = Cursors.SizeAll;
                }
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && spaceMove)
            {
                spaceMove = false;
                Cursor = tool == Tool.Text ? Cursors.IBeam : Cursors.Cross;
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }
            base.OnKeyUp(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (sizeBox != null && sizeBox.Focused)
            {
                if (keyData == Keys.Enter) { ApplySizeBox(); Focus(); return true; }
                if (keyData == Keys.Escape) { UpdateSizeLabel(); Focus(); return true; }
                return base.ProcessCmdKey(ref msg, keyData);
            }
            if (textBox != null && textBox.Focused)
            {
                if (keyData == Keys.Escape) { CancelText(); return true; }
                return base.ProcessCmdKey(ref msg, keyData);
            }
            switch (keyData)
            {
                case Keys.Escape: Close(); return true;
                case Keys.Control | Keys.C: CopyToClipboard(); return true;
                case Keys.Control | Keys.S: SaveToFolder(); return true;
                case Keys.Control | Keys.Shift | Keys.S: SaveAs(); return true;
                case Keys.Control | Keys.Z: Undo(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------- output ----------
        Bitmap Render()
        {
            CommitText();
            if (sel.IsEmpty) return null;
            Bitmap result = new Bitmap(sel.Width, sel.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.DrawImage(shot, new Rectangle(0, 0, sel.Width, sel.Height), sel, GraphicsUnit.Pixel);
                g.TranslateTransform(-sel.X, -sel.Y);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                foreach (Shape s in shapes) s.Draw(g, shot);
            }
            return result;
        }

        void CopyToClipboard()
        {
            using (Bitmap b = Render())
            {
                if (b == null) return;
                try { Clipboard.SetImage(b); }
                catch (Exception ex) { app.Notify(L.T("Не вдалося скопіювати: ", "Couldn't copy: ") + ex.Message, null); return; }
            }
            app.Notify(L.T("Скріншот скопійовано в буфер обміну", "Screenshot copied to clipboard"), null);
            Close();
        }

        void SaveToFolder()
        {
            using (Bitmap b = Render())
            {
                if (b == null) return;
                string folder = Settings.SaveFolder;
                try
                {
                    Directory.CreateDirectory(folder);
                    string path = Path.Combine(folder, Settings.NextFreeName(folder));
                    b.Save(path, ImageFormat.Png);
                    app.Notify(L.T("Збережено: ", "Saved: ") + Path.GetFileName(path), path);
                }
                catch (Exception ex) { app.Notify(L.T("Не вдалося зберегти: ", "Couldn't save: ") + ex.Message, null); return; }
            }
            Close();
        }

        void SaveAs()
        {
            if (sel.IsEmpty) return;
            CommitText();
            using (SaveFileDialog d = new SaveFileDialog())
            {
                string folder = Settings.SaveFolder;
                try { Directory.CreateDirectory(folder); } catch { }
                d.InitialDirectory = folder;
                d.FileName = Settings.NextFreeName(folder);
                d.Filter = "PNG|*.png|JPEG|*.jpg";
                d.Title = L.T("Зберегти скріншот", "Save screenshot");
                TopMost = false;
                DialogResult r = d.ShowDialog(this);
                TopMost = true;
                if (r != DialogResult.OK) return;
                using (Bitmap b = Render())
                {
                    try
                    {
                        if (d.FilterIndex == 2) b.Save(d.FileName, ImageFormat.Jpeg);
                        else b.Save(d.FileName, ImageFormat.Png);
                        app.Notify(L.T("Збережено: ", "Saved: ") + Path.GetFileName(d.FileName), d.FileName);
                    }
                    catch (Exception ex) { app.Notify(L.T("Не вдалося зберегти: ", "Couldn't save: ") + ex.Message, null); return; }
                }
            }
            Close();
        }
    }

    // ---------------- Tray + hotkey ----------------

    class HotkeyWindow : NativeWindow, IDisposable
    {
        public event EventHandler Pressed;
        public HotkeyWindow() { CreateHandle(new CreateParams()); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && Pressed != null) Pressed(this, EventArgs.Empty);
            base.WndProc(ref m);
        }
        public void Dispose() { DestroyHandle(); }
    }

    class HotkeyDialog : Form
    {
        public int Vk, Mods;
        readonly TrayApp app;
        readonly Label combo;
        readonly Label warn;
        readonly Button ok;

        public HotkeyDialog(TrayApp app, int vk, int mods)
        {
            this.app = app;
            Vk = vk; Mods = mods;
            Text = L.T("Гаряча клавіша для скріншота", "Screenshot hotkey");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10f);
            ClientSize = new Size(420, 214);

            Label hint = new Label();
            hint.Text = L.T("Натисніть клавішу або поєднання клавіш (наприклад F1, Print Screen, Ctrl+Shift+A). Esc — скасувати, Enter — зберегти.", "Press a key or key combination (e.g. F1, Print Screen, Ctrl+Shift+A). Esc to cancel, Enter to save.");
            hint.SetBounds(16, 12, 388, 56);
            Controls.Add(hint);

            combo = new Label();
            combo.Text = Settings.HotkeyName(vk, mods);
            combo.Font = new Font("Segoe UI", 16f, FontStyle.Bold);
            combo.TextAlign = ContentAlignment.MiddleCenter;
            combo.BorderStyle = BorderStyle.FixedSingle;
            combo.SetBounds(16, 72, 388, 48);
            Controls.Add(combo);

            warn = new Label();
            warn.Text = L.T("Увага: ця клавіша більше не працюватиме в інших програмах, лише для скріншота.", "Note: this key will no longer work in other apps, only for screenshots.");
            warn.ForeColor = Color.FromArgb(200, 90, 0);
            warn.Font = new Font("Segoe UI", 8.5f);
            warn.SetBounds(16, 122, 388, 34);
            warn.Visible = false;
            Controls.Add(warn);

            Button def = new Button();
            def.Text = "Print Screen";
            def.SetBounds(16, 162, 120, 36);
            def.TabStop = false;
            def.Click += delegate { Vk = 0x2C; Mods = 0; combo.Text = Settings.HotkeyName(Vk, Mods); };
            Controls.Add(def);

            ok = new Button();
            ok.Text = L.T("Зберегти", "Save");
            ok.SetBounds(196, 162, 100, 36);
            ok.TabStop = false;
            ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = L.T("Скасувати", "Cancel");
            cancel.SetBounds(304, 162, 100, 36);
            cancel.TabStop = false;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            app.Recorder = delegate (int vk, int mods)
            {
                if (IsDisposed) return;
                if (vk == 0x1B && mods == 0) { DialogResult = DialogResult.Cancel; Close(); return; } // Esc = cancel
                if (vk == 0x0D && mods == 0) { DialogResult = DialogResult.OK; Close(); return; }     // Enter = save
                bool special = (vk >= 0x70 && vk <= 0x87) || vk == 0x2C || vk == 0x13 || vk == 0x91 || vk == 0x2D;
                warn.Visible = mods == 0 && !special; // a single ordinary key will stop working in other apps
                Vk = vk; Mods = mods;
                combo.Text = Settings.HotkeyName(vk, mods);
            };
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            app.Recorder = null;
            base.OnFormClosed(e);
        }
    }

    class TrayApp : ApplicationContext
    {
        readonly NotifyIcon tray = new NotifyIcon();
        readonly HotkeyWindow hk = new HotkeyWindow();
        readonly ToolStripMenuItem autostartItem;
        CaptureForm active;
        string lastPath;
        IntPtr hook = IntPtr.Zero;
        Native.LowLevelKeyboardProc hookProc; // keep a reference so the GC doesn't collect the delegate
        readonly SynchronizationContext ui;
        int hotVk, hotMods;
        bool mCtrl, mShift, mAlt, mWin;      // modifier state tracked by the hook itself
        bool swallowUp;                      // also swallow the hotkey's key-up
        public Action<int, int> Recorder;    // when set, the hook records a new combo instead of taking a screenshot
        ToolStripMenuItem hotkeyItem;

        public TrayApp()
        {
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            tray.Icon = MakeIcon();
            tray.Text = "Snipper";
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripItem captureItem = menu.Items.Add("", null, delegate { Capture(); });
            ToolStripItem openItem = menu.Items.Add("", null, delegate { OpenFolder(); });
            ToolStripItem chooseItem = menu.Items.Add("", null, delegate { ChooseFolder(); });
            hotkeyItem = new ToolStripMenuItem("");
            hotkeyItem.Click += delegate { ChangeHotkey(); };
            menu.Items.Add(hotkeyItem);
            autostartItem = new ToolStripMenuItem("");
            autostartItem.Click += delegate { Settings.Autostart = !Settings.Autostart; autostartItem.Checked = Settings.Autostart; };
            menu.Items.Add(autostartItem);
            ToolStripMenuItem langItem = new ToolStripMenuItem("Мова / Language");
            ToolStripMenuItem ukItem = new ToolStripMenuItem("Українська");
            ToolStripMenuItem enItem = new ToolStripMenuItem("English");
            ukItem.Click += delegate { Settings.Language = "uk"; L.En = false; };
            enItem.Click += delegate { Settings.Language = "en"; L.En = true; };
            langItem.DropDownItems.Add(ukItem);
            langItem.DropDownItems.Add(enItem);
            menu.Items.Add(langItem);
            menu.Items.Add(new ToolStripSeparator());
            ToolStripItem exitItem = menu.Items.Add("", null, delegate { ExitThread(); });
            menu.Opening += delegate
            {
                captureItem.Text = L.T("Зробити скріншот", "Take screenshot");
                openItem.Text = L.T("Відкрити папку зі скріншотами", "Open screenshots folder");
                chooseItem.Text = L.T("Вибрати папку для збереження…", "Choose save folder…");
                autostartItem.Text = L.T("Запускати разом із Windows", "Start with Windows");
                exitItem.Text = L.T("Вихід", "Exit");
                ukItem.Checked = !L.En;
                enItem.Checked = L.En;
                autostartItem.Checked = Settings.Autostart;
                hotkeyItem.Text = L.T("Гаряча клавіша: ", "Hotkey: ") + Settings.HotkeyName(hotVk, hotMods) + "…";
            };
            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Capture(); };
            tray.BalloonTipClicked += delegate
            {
                if (lastPath != null && File.Exists(lastPath))
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + lastPath + "\"");
            };
            tray.Visible = true;

            hk.Pressed += delegate { Capture(); };
            hotVk = Settings.HotkeyVk;
            hotMods = Settings.HotkeyMods;

            // All hotkeys go through a low-level keyboard hook, so Print Screen and system combos
            // (e.g. Win+Shift+S) that Windows would otherwise keep for itself work too.
            hookProc = HookCallback;
            using (System.Diagnostics.Process cur = System.Diagnostics.Process.GetCurrentProcess())
            using (System.Diagnostics.ProcessModule mod = cur.MainModule)
                hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, hookProc, Native.GetModuleHandle(mod.ModuleName), 0);

            // Fallback if the hook could not be installed
            bool fallback = hook == IntPtr.Zero &&
                Native.RegisterHotKey(hk.Handle, 2, Native.MOD_NOREPEAT | Native.MOD_CONTROL | Native.MOD_SHIFT, Native.VK_S);

            if (!Settings.FirstRunDone)
            {
                Settings.FirstRunDone = true;
                Settings.Autostart = true;
            }

            if (hook != IntPtr.Zero)
                Notify(L.T("Snipper працює. Скріншот — ", "Snipper is running. Screenshot: ") + Settings.HotkeyName(hotVk, hotMods) + L.T(". Змінити: ПКМ по значку → «Гаряча клавіша».", ". To change it: right-click the tray icon → Hotkey."), null);
            else if (fallback)
                Notify(L.T("Не вдалося перехопити клавіатуру. Скріншот — Ctrl+Shift+S або клік по значку в треї.", "Couldn't hook the keyboard. Take a screenshot with Ctrl+Shift+S or by clicking the tray icon."), null);
            else
                Notify(L.T("Не вдалося призначити гарячі клавіші. Робіть скріншот клацанням по значку в треї.", "Couldn't register hotkeys. Take a screenshot by clicking the tray icon."), null);
        }

        static bool IsModifier(int vk)
        {
            return vk == 0x10 || vk == 0x11 || vk == 0x12 || (vk >= 0xA0 && vk <= 0xA5) || vk == 0x5B || vk == 0x5C;
        }

        static bool Down(int vk) { return (Native.GetAsyncKeyState(vk) & 0x8000) != 0; }

        public void ApplyHotkey(int vk, int mods)
        {
            hotVk = vk; hotMods = mods;
            Settings.HotkeyVk = vk; Settings.HotkeyMods = mods;
        }

        void ChangeHotkey()
        {
            using (HotkeyDialog d = new HotkeyDialog(this, hotVk, hotMods))
            {
                if (d.ShowDialog() == DialogResult.OK)
                {
                    ApplyHotkey(d.Vk, d.Mods);
                    Notify(L.T("Нова гаряча клавіша: ", "New hotkey: ") + Settings.HotkeyName(hotVk, hotMods), null);
                }
            }
        }

        IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vk = Marshal.ReadInt32(lParam);
                int msg = wParam.ToInt32();
                bool isDown = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
                bool isUp = msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP;

                if (IsModifier(vk))
                {
                    if (vk == 0x11 || vk == 0xA2 || vk == 0xA3) mCtrl = isDown;
                    else if (vk == 0x10 || vk == 0xA0 || vk == 0xA1) mShift = isDown;
                    else if (vk == 0x12 || vk == 0xA4 || vk == 0xA5) mAlt = isDown;
                    else mWin = isDown;
                    return Native.CallNextHookEx(hook, nCode, wParam, lParam);
                }

                int mods = 0;
                if (mAlt || Down(0x12)) mods |= 1;
                if (mCtrl || Down(0x11)) mods |= 2;
                if (mShift || Down(0x10)) mods |= 4;
                if (mWin || Down(0x5B) || Down(0x5C)) mods |= 8;

                if (Recorder != null)
                {
                    if (isDown)
                    {
                        Action<int, int> rec = Recorder;
                        int v = vk, m = mods;
                        ui.Post(delegate { rec(v, m); }, null);
                        if ((mods & 8) != 0) SuppressStartMenu();
                    }
                    return new IntPtr(1); // while recording a key, pass nothing on
                }

                if (vk == hotVk)
                {
                    if (isDown && mods == hotMods)
                    {
                        swallowUp = true;
                        if ((mods & 8) != 0) SuppressStartMenu();
                        ui.Post(delegate { Capture(); }, null);
                        return new IntPtr(1);
                    }
                    if (isUp && swallowUp) { swallowUp = false; return new IntPtr(1); }
                }
            }
            return Native.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        // After a swallowed combo with Win, Windows thinks Win was pressed alone and opens Start.
        // Sending an unassigned key prevents that.
        static void SuppressStartMenu()
        {
            Native.keybd_event(0xE8, 0, 0, UIntPtr.Zero);
            Native.keybd_event(0xE8, 0, 2, UIntPtr.Zero);
        }

        static Icon MakeIcon()
        {
            using (Bitmap b = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (Brush bg = new SolidBrush(Color.FromArgb(0, 120, 215)))
                        g.FillEllipse(bg, 1, 1, 30, 30);
                    using (Pen p = new Pen(Color.White, 3f))
                    {
                        g.DrawLines(p, new Point[] { new Point(9, 14), new Point(9, 9), new Point(14, 9) });
                        g.DrawLines(p, new Point[] { new Point(18, 23), new Point(23, 23), new Point(23, 18) });
                    }
                }
                IntPtr h = b.GetHicon();
                Icon tmp = Icon.FromHandle(h);
                Icon copy = (Icon)tmp.Clone();
                Native.DestroyIcon(h);
                return copy;
            }
        }

        public void Notify(string text, string path)
        {
            lastPath = path;
            tray.BalloonTipTitle = "Snipper";
            tray.BalloonTipText = text;
            tray.ShowBalloonTip(3000);
        }

        void Capture()
        {
            if (active != null && !active.IsDisposed) { active.Activate(); return; }
            Thread.Sleep(150); // let menus and tooltips disappear first
            active = new CaptureForm(this);
            active.FormClosed += delegate { active.Dispose(); active = null; };
            active.Show();
        }

        void OpenFolder()
        {
            string f = Settings.SaveFolder;
            try { Directory.CreateDirectory(f); System.Diagnostics.Process.Start("explorer.exe", "\"" + f + "\""); } catch { }
        }

        void ChooseFolder()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = L.T("Куди зберігати скріншоти (Screen_01.png, Screen_02.png …)", "Where to save screenshots (Screen_01.png, Screen_02.png …)");
                d.SelectedPath = Settings.SaveFolder;
                d.ShowNewFolderButton = true;
                if (d.ShowDialog() == DialogResult.OK)
                {
                    Settings.SaveFolder = d.SelectedPath;
                    Notify(L.T("Скріншоти зберігатимуться в: ", "Screenshots will be saved to: ") + d.SelectedPath, null);
                }
            }
        }

        protected override void ExitThreadCore()
        {
            if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
            Native.UnregisterHotKey(hk.Handle, 1);
            Native.UnregisterHotKey(hk.Handle, 2);
            hk.Dispose();
            tray.Visible = false;
            tray.Dispose();
            base.ExitThreadCore();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool created;
            using (Mutex m = new Mutex(true, "Snipper_SingleInstance_7F3A", out created))
            {
                if (!created) return; // already running
                Settings.MigrateFromOldName();
                L.En = Settings.Language == "en";
                Native.EnableDpiAwareness();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }
    }
}
