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
[assembly: System.Reflection.AssemblyVersion("2.4.0.0")]

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

        static string GetStr(string name, string def)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
            {
                string v = k == null ? null : k.GetValue(name) as string;
                return string.IsNullOrEmpty(v) ? def : v;
            }
        }
        static void SetStr(string name, string v)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key)) k.SetValue(name, v);
        }
        // Font for the Text tool: family + style (1 bold, 2 italic, 4 underline). Size comes from the size control.
        public static string TextFontName { get { return GetStr("TextFont", "Segoe UI"); } set { SetStr("TextFont", value); } }
        public static int TextFontStyle { get { return GetInt("TextFontStyle", (int)FontStyle.Bold); } set { SetInt("TextFontStyle", value); } }

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
            { "Number", "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" },
            { "Pin", "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAFjUlEQVR42t2bS2gdZRTHfzPzpSpSH+3CQn2BcVUqbgRB3dRHI6JGRNQqFAxqsWAUiqIirhTtKhq1CrY+EC0o2hqt1EgXLmzBakVUKoiIShFxZaOmyZ07LuYcPAz35s57JnfgktzJZe73/3/n8f/OOfGi7dtp+eUDEbAG2AeMAo8AzwMjwGKRhzuWx+UBrwMXA11gGugALwmGThF223wFAngjcLUA9eTeDmCr3BsZVgI8+XmDuIHe84BQ3OA+cYORYSRAQa81wJUEX0h4oQgJy8UC/k0QUhoJbSdA48DHCUJKI2E5WEAI/CQ//T6fyU2C32LgToBcB+wZsNbcJPgtBe9LetsCzABn9nGBwiS0jQBVfSHwtOT6SPK+l4G81CS4lgW7EDgZeBW4TawgSAm+HwkAL/aTzW0hQOXsGuBd4LIi4iYLCa5F4C8C3gMuKCpvs5DgtwT8tcBnAj4scWMGxgS/wUgfCPh7gQ+B02WhQUVZpScJTRBgI/1TcqRFIn1QcWpVErYKCc41FOlPAnYBm4zC82rSF3qK7AI7XAP+fhbwDnCFvHcNiKxQAuKcK+mBdve68uoFfr1E+tEawXcTru6Ze1tcCUyGSyjMrgG/EXhbZG1d4KNEzNG1aRbI5QK+AAvFnC8FLgRWAL8Ah4Gjxuc7wN0S7PyS01yanf9RLM5+57fAw8A+l2Pnu8Bq4AngDmBV4jOLwCfA48AR4EngUaPpgxrAq4VNA9uAS4B18rejwEFZp+9lKIvrzq8D3pddR3Y0SuR3gOPAF8AGc5jxagQ/Q1xLXDIjuYz+FABvCvgF8aVeOxoCKw34uvSGutcR4PbE9/oGh7pwal+0xciVhkFvCXbrNHkrpI4BNwJ/G6ulR2bKJIUjAdsB7gHmTc1+kNylxurxPwL+1xTry3wWUFM+IL41n2CYBkvnqibvlCzk+qTnwhUhzeuzLSJB/f5BCc4jWVplfoEo2wYSdC3PAVOmkFp5X6ANJOga9gKTWcy+rKJokySo2X8pJ0o/oUdq6ww1QYKmu9+AcYn85AFfVkGkThJsuhsXEoIi3+WX7I9VkmDT3SYx/1x+X1VjJEnCgjmKlun3kxL4Ck2GVFUU1XL2LHAlMCcEFCVhUQBPScorBXxVRVEFe05JUlhJ3SNip7DZV0mA7swE8BZwSsFjsCrPw1J7yJ3u6iBAwd8FvGIW6iVqhVHGA9gPwE1F013VBFjwO3uUuvUgpfeilEHPA16WdDdSRWr1awDfkfdfAZuBE6a0xoCKTQQ8BpxrntMqAtKAd8RFyOuBN+S8nkYn6HjMaqnrRW0jIC3474iHHI8Rd4T2ZxBLqiPGiecGOmXXFf0awF8F/C4mfSKjYtRnrgXOTzEmUwsBecGHOWWzmv4ZbSCgKPg8ZwfNGnNNp0EFv7kg+CwkqG74A/i5SQK0znYz8JrJ63nBpyVBGyr7xQKCJgjQOtsYsLtHlycv+EEkLPB/Kf6ZDAKqVALU7C8nbmsHiUBUFPxSJKyQZ20Dvq+qyOIGKLEOcS9wrxxsbJtLF/0Nces7L/heJGwg7ih/LueKoMwTYBoC9NR1NvARcQfYDjDpkNEhUXh/lrRIlbsH5WUVIVUNLPVTX6cJ+PP6gD8AXFMi+GQHylUR9AYRYIPbbuLhxY4Br8WJD4inuI8ba6Hkym+nyp3vR4Du5DTx8KL6ZWR+3yXavC29wdII0Ig/iZmjM2LESTqaMJayrMHbIGgHmabMbtuo/wDwrKnDRwzB5Uy6G5U6XmRMOwD+Im47z5RZjW0TARFwqgidVaaW54gnrG4Bvh5G8HbwaSfxEOOiIWaWeG5/aMErAQ8Bt5r8HkgWGJNTWDCs4JWAMXPimyceX7/fxIKQIb4c8T8nrSeuu08An5ruS5chv/4DdFsAdBw7opAAAAAASUVORK5CYII=" },
            { "Timer", "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" },
            { "Picker", "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAFMUlEQVR42uWbS4jdVBjHfyfJaEsdEbU+qqAgFBdSfCCoW7FDxYVQEB8obupGEEVR0YUdUMGNgg9wK+rUFz6woLVudK12KOhCnW58UVsHaitVJ8lxke/gxyG5N8nk5tXA5d65N5Pk9//O43ucY+zBJziVj2gEDEZ9tqeKAAYIgVRe+gjlPS0jyBAFCIEEiOXvOWCTfHdc3v1zRyGAkVcCnAvcBtwMXA6cJYIcBr4BPgD2yrkTRYgG1s9T4H7gSeDCnPPOB7YB9wJfA48Dn08SIRgAfKBEeA14WeBjgXJ93cpn9/01wH7gUdUSBtcCNPweafZr8txRQUtx/+Ms/pyc+2xeSwgGAG89+Dlv6ps0WAbSIp4BduS1hGAA8G958HUGTgu8BGyQbmL6LEBT8LolpMBlwK1y3bAvAgTqZZRlmoLXHqIFdvoeY9SxldOc75uGRwm8zR8Iow49OYAtwAXASeAQ8O8M4LUfcTZwBnDMjQ1RR/DXA7uBG+SBLPADsApcJ+fMMbJoMBCwnWLlyLPQVtUtwobvbeUeq8AJPQ4ELcJb4ArgTYGP1WDkvLhkRs/kvMWDvi8QtejLp8DTwOkCH+XM17MOpN7zcwhBS/AJsBm40Z+HWzhcq/oR+Eg9T2sCuHtcIgMeM7Z23vxvgAeAv1V3bE0Ad7O45RnHiqUjCZ8/6SoYcgIcAv6om7tbB/xuiQSjvJxAWwKEwJ8yCJlpaaqG4RfVrNNZQiSVez0FfKcClLbgk2kDVFvHYekKRg1QbcDbLgUIFPDbwC0qJjcNtYRa8G0IkBfbx+q3E/KeNiBAZfhZC1CU2LDy2x7gSmBZpa7W4+YuVoWfpQBF8EiU9w5wN7ACbCfL5Uc1RHBe3pdi/UrwsxKgDPztYrkIOAIsAAdqiOA8yq3AmXVaUdARvBsAY5kSj0pLcCKkFQW4iP8LJaYrAarA63RYokRYkO5QdWBM6jpXQcfwGsB1hx06ZVUy2PkV+LmOmx30AF5ni0LgBWDeS5JMsrwB3pdIL6oqQNQTeHcsyTXSglq/vl8MnAb8IsFOrRgj6Am8fw0XO7wBvCKfQzV4BgK/QlYi/71kl2msBcwS3j2X8xUA9gG7xHHaIFbfK91ldT3eZNQgvJHrvQvc6RU/TMH0NU1AF0d8LK+NYvlj3vOkbaXFi+BdJXYJuKtCmnpa67EqTW7JCign1bMn640johpeVxH8p5J3O2dCszeqpbxYoutYr9ZvvEGQtgojOm1dVLq6GvjeTzpOuO+8EqnsuGG7qgy5frY0oW53Xo0oLqkAT1elMZdJfUgesqhoaWtEcp3ClxHAZWzmgcem1O2qBCFrfYAv4wi5368lW4LWhPvcG/gyMG762aKmpNHATxMgJFuwsBl4sAHr9w5+0hgQqiWp+4CrlH8+GvgiiwYK/jOBj8cInyeAm+99+GiM8L4A7uE2SuAxenhfAFeve4RsodJaTfh0KPD+YuRYYu1dNRYq6Z0bgcC/DtzhnUOfBYBsOenFavVmlcqvC4K+Be4D7lHucS/h86bBTSoOL+PaujHigHSdoyJAkhPSDkKA34B/yFZyTRNBwy+QpbRL79XpWxdwzfgn4CtVbp4Gv6zg51QKaxDwRdPgosraxN6WlETNDstk5awjYvG1slvV+iqAq7TuBx4G/hJQvZQ9FEt/Adyk4BMYx85R1xWeBz4k2321HbhULLtCVtd/VQk2WHgAU7B32LfqvLwf97qMhXHuHU7U3J4ocKPEGTz8tJSY9u4aT0cPbfe4ZaTHf/t7370Sj952AAAAAElFTkSuQmCC" },
            { "Fish", "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" },
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
        // Raw icon recolored to a single color (keeps its own aspect ratio)
        public static Bitmap Tinted(string name, Color c)
        {
            string key = name + "#" + c.ToArgb();
            Bitmap b;
            if (cache.TryGetValue(key, out b)) return b;
            using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(Data[name])))
            using (Bitmap src = new Bitmap(ms))
            using (ImageAttributes ia = new ImageAttributes())
            {
                ColorMatrix cm = new ColorMatrix(new float[][] {
                    new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 },
                    new float[] { 0, 0, 0, 1, 0 }, new float[] { c.R / 255f, c.G / 255f, c.B / 255f, 0, 1 } });
                ia.SetColorMatrix(cm);
                b = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(b))
                    g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
            }
            cache[key] = b;
            return b;
        }

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
        public bool Straight;   // Shift held: straight line from the start point
        public override void Update(Point p)
        {
            if (Straight)
            {
                Point start = Points[0];
                Points.Clear();
                Points.Add(start);
                Points.Add(p);
                return;
            }
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
        public string FontName = "Segoe UI";
        public FontStyle Style = FontStyle.Bold;
        public float FontSize { get { return 12f + Width * 2.5f; } }
        public Font MakeFont()
        {
            try { return new Font(FontName, FontSize, Style, GraphicsUnit.Pixel); }
            catch { return new Font("Segoe UI", FontSize, Style, GraphicsUnit.Pixel); }
        }
        public override void Draw(Graphics g, Bitmap source)
        {
            if (string.IsNullOrEmpty(Text)) return;
            using (Font f = MakeFont())
            using (Brush b = new SolidBrush(Color))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.DrawString(Text, f, b, At);
            }
        }
    }

    class NumberShape : Shape
    {
        public Point Center;
        public int Number;
        public float Radius { get { return 9f + Width * 1.6f; } }
        public override void Draw(Graphics g, Bitmap source)
        {
            float r = Radius;
            RectangleF rc = new RectangleF(Center.X - r, Center.Y - r, r * 2, r * 2);
            // light fill gets black digits, dark fill gets white
            double lum = 0.299 * Color.R + 0.587 * Color.G + 0.114 * Color.B;
            Color ink = lum > 150 ? Color.Black : Color.White;
            using (Brush fill = new SolidBrush(Color))
            using (Pen edge = new Pen(Color.FromArgb(90, 0, 0, 0), Math.Max(1f, r * 0.08f)))
            using (Brush txt = new SolidBrush(ink))
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
            {
                SmoothingMode old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(fill, rc);
                g.DrawEllipse(edge, rc);
                if (Number == 100)
                {
                    // easter egg: 100 becomes an ichthys
                    Bitmap fish = Icons.Tinted("Fish", ink);
                    float fw = r * 1.45f, fh = fw * fish.Height / fish.Width;
                    InterpolationMode im = g.InterpolationMode;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(fish, Center.X - fw / 2, Center.Y - fh / 2, fw, fh);
                    g.InterpolationMode = im;
                }
                else
                {
                    string label = Number.ToString(CultureInfo.InvariantCulture);
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    // shrink the font until the number fits inside the circle on a single line
                    float size = r * 1.15f, maxW = r * 1.55f;
                    for (int i = 0; i < 12; i++)
                    {
                        using (Font probe = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel))
                        {
                            float w = g.MeasureString(label, probe, PointF.Empty, StringFormat.GenericTypographic).Width;
                            if (w <= maxW) break;
                            size *= Math.Max(0.6f, maxW / w);
                        }
                    }
                    using (Font f = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel))
                    {
                        RectangleF tr = new RectangleF(rc.X - r, rc.Y, rc.Width + 2 * r, rc.Height); // wide box: never wraps
                        tr.Offset(0, r * 0.04f);
                        g.DrawString(label, f, txt, tr, sf);
                    }
                }
                g.SmoothingMode = old;
            }
        }
    }

    enum Tool { Select, Pen, Line, Arrow, Rect, Marker, Text, Number, Blur, Picker }

    // ---------------- Capture overlay ----------------

    class CaptureForm : Form
    {
        Bitmap shot;      // capture of all monitors
        Bitmap dimmed;    // the same capture, dimmed
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
        FlowLayoutPanel side;
        Button timerBtn;
        Tool toolBeforePicker = Tool.Pen;
        Point mousePos = new Point(-1, -1);
        TextBox sizeBox;
        Label sizeUnit;
        readonly ToolTip tips = new ToolTip();

        // Capture all monitors (also used again by the timer)
        void GrabScreen()
        {
            Bitmap ns = new Bitmap(virt.Width, virt.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(ns))
                g.CopyFromScreen(virt.Location, Point.Empty, virt.Size, CopyPixelOperation.SourceCopy);
            Bitmap nd = new Bitmap(ns);
            using (Graphics g = Graphics.FromImage(nd))
            using (Brush b = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
                g.FillRectangle(b, 0, 0, nd.Width, nd.Height);
            if (shot != null) shot.Dispose();
            if (dimmed != null) dimmed.Dispose();
            shot = ns; dimmed = nd;
        }

        public CaptureForm(TrayApp app)
        {
            this.app = app;
            virt = SystemInformation.VirtualScreen;
            GrabScreen();

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
            AddTool(Tool.Number, "Number", L.T("Нумерація (1, 2, 3…)", "Numbering (1, 2, 3…)"));
            AddTool(Tool.Blur, "Blur", L.T("Розмиття (пікселізація)", "Blur (pixelate)"));

            colorButton = MakeButton("", L.T("Колір", "Color"), delegate { PickColor(); });
            colorButton.BackColor = color;
            bar.Controls.Add(colorButton);
            Button fontBtn = MakeButton("Aa", L.T("Шрифт для тексту", "Text font"), delegate { PickFont(); });
            fontBtn.Font = new Font("Segoe UI", 13f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            fontBtn.ForeColor = Color.FromArgb(0xC3, 0xC9, 0xD6);
            bar.Controls.Add(fontBtn);
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

            // Separate block to the right of the selection: pin, timer, color picker
            side = new FlowLayoutPanel();
            side.AutoSize = true;
            side.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            side.FlowDirection = FlowDirection.TopDown;
            side.WrapContents = false;
            side.BackColor = bar.BackColor;
            side.Padding = bar.Padding;
            side.Visible = false;
            side.Cursor = Cursors.Default;
            side.Controls.Add(MakeIconButton("Pin", L.T("Закріпити поверх усіх вікон", "Pin on top of all windows"), delegate { PinToScreen(); }));
            timerBtn = MakeIconButton("Timer", L.T("Таймер: зняти цю зону ще раз через кілька секунд", "Timer: capture this area again after a few seconds"), delegate { ShowTimerMenu(); });
            side.Controls.Add(timerBtn);
#if OCR
            Button ocrBtn = MakeButton("OCR", L.T("Розпізнати текст: виділіть уривок мишею. Ctrl+T — одразу скопіювати весь текст",
                                                  "Recognize text and select a passage with the mouse. Ctrl+T copies all text at once"), delegate { StartTextSelect(); });
            ocrBtn.Font = new Font("Segoe UI", 10f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            ocrBtn.ForeColor = Color.FromArgb(0xA8, 0xE6, 0xA1);
            side.Controls.Add(ocrBtn);
#endif
            Button pick = MakeIconButton("Picker", L.T("Піпетка: клік — HEX, Shift+клік — RGB", "Color picker: click = HEX, Shift+click = RGB"), delegate { SetTool(Tool.Picker); });
            toolButtons[Tool.Picker] = pick;
            side.Controls.Add(pick);
            Controls.Add(side);
            SetTool(Tool.Pen);
        }

        void SetTool(Tool t)
        {
            CommitText();
#if OCR
            if (ocr != null) { ocr = null; ocrAnchor = ocrFocus = -1; }
#endif
            if (t == Tool.Picker && tool != Tool.Picker) toolBeforePicker = tool;
            tool = t;
            Invalidate();
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
#if OCR
            if (ocr != null) return;
#endif
            ChangeSize(e.Delta > 0 ? 1 : -1);
            if (current != null) // shape still being drawn (left button held) — change its width/strength right away
            {
                current.Width = current is BlurShape ? blurStrength : penWidth * scale;
                Invalidate();
            }
        }

        void PlaceToolbar()
        {
            if (sel.IsEmpty) { bar.Visible = false; side.Visible = false; return; }
            // limits = the monitor the selection is on (in form coordinates), not the whole virtual screen
            Rectangle selScreen = new Rectangle(sel.X + virt.X, sel.Y + virt.Y, sel.Width, sel.Height);
            Rectangle mon = Screen.FromRectangle(selScreen).Bounds;
            mon.Offset(-virt.X, -virt.Y);
            int gap = (int)(6 * scale);

            // main bar: below → above → inside, at the bottom of the selection
            bar.Visible = true;
            Size s = bar.PreferredSize;
            int x = sel.Right - s.Width;
            int y = sel.Bottom + gap;
            if (y + s.Height > mon.Bottom) y = sel.Top - s.Height - gap;
            if (y < mon.Top) y = sel.Bottom - s.Height - gap;
            y = Math.Max(mon.Top, Math.Min(y, mon.Bottom - s.Height));
            x = Math.Max(mon.Left, Math.Min(x, mon.Right - s.Width));
            bar.Location = new Point(x, y);
            bar.BringToFront();

            // side block: right → left → inside, at the right edge of the selection
            Size ss = side.PreferredSize;
            int sx = sel.Right + gap;
            if (sx + ss.Width > mon.Right) sx = sel.Left - ss.Width - gap;
            if (sx < mon.Left) sx = sel.Right - ss.Width - gap;
            sx = Math.Max(mon.Left, Math.Min(sx, mon.Right - ss.Width));
            int sy = Math.Max(mon.Top, Math.Min(sel.Top, mon.Bottom - ss.Height));
            // inside the selection: keep it clear of the main bar
            Rectangle sideRect = new Rectangle(sx, sy, ss.Width, ss.Height);
            if (sideRect.IntersectsWith(bar.Bounds))
            {
                int above = bar.Top - ss.Height - gap;
                if (above >= mon.Top) sy = Math.Min(sy, above);
                else sx = Math.Max(mon.Left, bar.Left - ss.Width - gap);
            }
            side.Location = new Point(sx, sy);
            side.Visible = true;
            side.BringToFront();
        }

        // ---------- new tools: color picker, pin, timer, font ----------
        void DrawMagnifier(Graphics g)
        {
            int cells = 11, cell = (int)(10 * scale), box = cells * cell;
            int mx = mousePos.X, my = mousePos.Y;
            if (mx < 0 || my < 0 || mx >= shot.Width || my >= shot.Height) return;
            Color c = shot.GetPixel(mx, my);
            string hex = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
            string rgb = c.R + ", " + c.G + ", " + c.B;
            using (Font f = new Font("Segoe UI", 12f * scale, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                int labelH = (int)(f.GetHeight(g) * 2 + 8 * scale);
                int x = mx + (int)(20 * scale), y = my + (int)(20 * scale);
                if (x + box > ClientSize.Width) x = mx - (int)(20 * scale) - box;
                if (y + box + labelH > ClientSize.Height) y = my - (int)(20 * scale) - box - labelH;
                Rectangle r = new Rectangle(x, y, box, box);
                InterpolationMode im = g.InterpolationMode; PixelOffsetMode pm = g.PixelOffsetMode;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.FillRectangle(Brushes.Black, r);
                g.DrawImage(shot, r, new Rectangle(mx - cells / 2, my - cells / 2, cells, cells), GraphicsUnit.Pixel);
                g.InterpolationMode = im; g.PixelOffsetMode = pm;
                using (Pen grid = new Pen(Color.FromArgb(40, 255, 255, 255)))
                    for (int i = 1; i < cells; i++)
                    {
                        g.DrawLine(grid, x + i * cell, y, x + i * cell, y + box);
                        g.DrawLine(grid, x, y + i * cell, x + box, y + i * cell);
                    }
                Rectangle center = new Rectangle(x + (cells / 2) * cell, y + (cells / 2) * cell, cell, cell);
                g.DrawRectangle(Pens.Black, Rectangle.Inflate(center, 1, 1));
                g.DrawRectangle(Pens.White, center);
                using (Pen border = new Pen(Color.FromArgb(0x6E, 0xA5, 0xFF), Math.Max(1f, scale)))
                    g.DrawRectangle(border, r);
                Rectangle lab = new Rectangle(x, y + box, box, labelH);
                using (Brush bg = new SolidBrush(Color.FromArgb(235, 20, 20, 22))) g.FillRectangle(bg, lab);
                using (Brush sw = new SolidBrush(c))
                    g.FillRectangle(sw, x + 4 * scale, y + box + 4 * scale, 14 * scale, 14 * scale);
                g.DrawString(hex, f, Brushes.White, x + 22 * scale, y + box + 2 * scale);
                g.DrawString(rgb, f, Brushes.Gainsboro, x + 4 * scale, y + box + 2 * scale + f.GetHeight(g));
            }
        }

        void PickPixel(Point at, bool asRgb)
        {
            if (at.X < 0 || at.Y < 0 || at.X >= shot.Width || at.Y >= shot.Height) return;
            Color c = shot.GetPixel(at.X, at.Y);
            string text = asRgb ? "rgb(" + c.R + ", " + c.G + ", " + c.B + ")"
                                : "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
            try { Clipboard.SetText(text); } catch { }
            // the picked color also becomes the drawing color
            color = Color.FromArgb(255, c.R, c.G, c.B);
            colorButton.BackColor = color;
            Settings.ColorArgb = color.ToArgb();
            app.Notify(L.T("Скопійовано колір: ", "Color copied: ") + text, null);
            SetTool(toolBeforePicker);
        }

#if OCR
        bool ocrBusy;
        OcrData ocr;                 // not null = text-selection mode
        int ocrAnchor = -1, ocrFocus = -1;
        bool ocrDragging;

        async System.Threading.Tasks.Task<OcrData> RunOcr()
        {
            Bitmap crop = new Bitmap(sel.Width, sel.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(crop))
                g.DrawImage(shot, new Rectangle(0, 0, sel.Width, sel.Height), sel, GraphicsUnit.Pixel);
            using (crop) return await OcrHelper.RecognizeAsync(crop, sel.Location);
        }

        // OCR button: recognize, then let the user select a passage with the mouse
        async void StartTextSelect()
        {
            if (sel.IsEmpty || ocrBusy) return;
            if (ocr != null) { CopyOcrSelection(); return; }
            CommitText();
            ocrBusy = true;
            Cursor = Cursors.WaitCursor;
            try
            {
                OcrData d = await RunOcr();
                if (IsDisposed) return;
                if (d.Words.Count == 0) { app.Notify(L.T("Текст не знайдено", "No text found"), null); return; }
                ocr = d;
                ocrAnchor = ocrFocus = -1;
                Invalidate();
            }
            catch (Exception ex) { app.Notify(L.T("OCR не спрацював: ", "OCR failed: ") + ex.Message, null); }
            finally
            {
                ocrBusy = false;
                if (!IsDisposed) Cursor = ocr != null ? Cursors.IBeam : ToolCursor;
            }
        }

        // Ctrl+T: copy all recognized text at once
        async void RecognizeText()
        {
            if (sel.IsEmpty || ocrBusy) return;
            if (ocr != null) { CopyText(ocr.All); return; }
            CommitText();
            ocrBusy = true;
            Cursor = Cursors.WaitCursor;
            try
            {
                OcrData d = await RunOcr();
                if (IsDisposed) return;
                if (d.Words.Count == 0) { app.Notify(L.T("Текст не знайдено", "No text found"), null); return; }
                CopyText(d.All);
            }
            catch (Exception ex) { app.Notify(L.T("OCR не спрацював: ", "OCR failed: ") + ex.Message, null); }
            finally
            {
                ocrBusy = false;
                if (!IsDisposed) Cursor = ToolCursor;
            }
        }

        void CopyText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetText(text); } catch { }
            string preview = text.Replace("\r", " ").Replace("\n", " ");
            if (preview.Length > 80) preview = preview.Substring(0, 80) + "…";
            app.Notify(L.T("Текст скопійовано: ", "Text copied: ") + preview, null);
            Close();
        }

        void CopyOcrSelection()
        {
            if (ocr == null) return;
            if (ocrAnchor < 0) { CopyText(ocr.All); return; } // nothing selected — copy everything
            CopyText(ocr.Join(Math.Min(ocrAnchor, ocrFocus), Math.Max(ocrAnchor, ocrFocus)));
        }

        void ExitTextSelect()
        {
            ocr = null;
            ocrAnchor = ocrFocus = -1;
            ocrDragging = false;
            Cursor = ToolCursor;
            Invalidate();
        }

        // Word under the point; if none, the closest word on the same line, else the closest overall
        int WordAt(Point p)
        {
            if (ocr == null || ocr.Words.Count == 0) return -1;
            int best = -1; double bestD = double.MaxValue;
            for (int i = 0; i < ocr.Words.Count; i++)
            {
                RectangleF r = ocr.Words[i].R;
                if (RectangleF.Inflate(r, 2, 2).Contains(p)) return i;
                double dy = p.Y < r.Top ? r.Top - p.Y : p.Y > r.Bottom ? p.Y - r.Bottom : 0;
                double dx = p.X < r.Left ? r.Left - p.X : p.X > r.Right ? p.X - r.Right : 0;
                double d = dy * 4 + dx;   // prefer words on the same line
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        void DrawOcr(Graphics g)
        {
            if (ocr == null) return;
            int a = Math.Min(ocrAnchor, ocrFocus), b = Math.Max(ocrAnchor, ocrFocus);
            using (Brush found = new SolidBrush(Color.FromArgb(38, 0x6E, 0xA5, 0xFF)))
            using (Brush picked = new SolidBrush(Color.FromArgb(120, 0x6E, 0xA5, 0xFF)))
                for (int i = 0; i < ocr.Words.Count; i++)
                {
                    RectangleF r = RectangleF.Inflate(ocr.Words[i].R, 2, 2);
                    bool on = ocrAnchor >= 0 && i >= a && i <= b;
                    g.FillRectangle(on ? picked : found, r);
                    // fill the gap to the next word on the same line so the selection looks continuous
                    if (on && i < b && i + 1 < ocr.Words.Count && ocr.Words[i + 1].Line == ocr.Words[i].Line)
                    {
                        RectangleF n = RectangleF.Inflate(ocr.Words[i + 1].R, 2, 2);
                        if (n.Left > r.Right)
                            g.FillRectangle(picked, r.Right, Math.Max(r.Top, n.Top), n.Left - r.Right, Math.Min(r.Bottom, n.Bottom) - Math.Max(r.Top, n.Top));
                    }
                }
            string hint = L.T("Виділіть текст мишею  ·  Ctrl+C — копіювати  ·  Ctrl+A — усе  ·  Esc — назад",
                              "Select text with the mouse  ·  Ctrl+C copy  ·  Ctrl+A all  ·  Esc back");
            using (Font f = new Font("Segoe UI", 13f * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                SizeF ts = g.MeasureString(hint, f);
                float x = sel.X + (sel.Width - ts.Width) / 2, y = sel.Y - ts.Height - 10 * scale;
                if (y < 0) y = sel.Y + 6 * scale;
                x = Math.Max(4, Math.Min(x, ClientSize.Width - ts.Width - 4));
                using (Brush bg = new SolidBrush(Color.FromArgb(225, 20, 20, 22)))
                    g.FillRectangle(bg, x - 8, y - 4, ts.Width + 16, ts.Height + 8);
                g.DrawString(hint, f, Brushes.White, x, y);
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (ocr == null || e.Button != MouseButtons.Left) return;
            int w = WordAt(e.Location);
            if (w < 0) return;
            int line = ocr.Words[w].Line, first = w, last = w;
            while (first > 0 && ocr.Words[first - 1].Line == line) first--;
            while (last < ocr.Words.Count - 1 && ocr.Words[last + 1].Line == line) last++;
            ocrAnchor = first; ocrFocus = last;
            ocrDragging = false;
            Invalidate();
        }
#endif

        void PickFont()
        {
            CommitText();
            using (FontDialog d = new FontDialog())
            {
                d.ShowEffects = true;
                d.ShowColor = false;
                d.FontMustExist = true;
                d.AllowVerticalFonts = false;
                try { d.Font = new Font(Settings.TextFontName, 14f, (FontStyle)(Settings.TextFontStyle & 7)); } catch { }
                TopMost = false;
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    Settings.TextFontName = d.Font.FontFamily.Name;
                    Settings.TextFontStyle = (int)(d.Font.Style & (FontStyle.Bold | FontStyle.Italic | FontStyle.Underline));
                    app.Notify(L.T("Шрифт тексту: ", "Text font: ") + d.Font.FontFamily.Name + L.T(" (розмір — полем −/+)", " (size via the −/+ control)"), null);
                    SetTool(Tool.Text);
                }
                TopMost = true;
            }
        }

        void PinToScreen()
        {
            Bitmap b = Render();
            if (b == null) return;
            Point screenPos = new Point(virt.X + sel.X, virt.Y + sel.Y);
            PinForm pf = new PinForm(b, screenPos, app);
            pf.Show();
            Close();
        }

        void ShowTimerMenu()
        {
            CommitText();
            ContextMenuStrip m = new ContextMenuStrip();
            foreach (int sec in new int[] { 3, 5, 10 })
            {
                int s2 = sec;
                m.Items.Add(sec + L.T(" с", " s"), null, delegate { StartTimer(s2); });
            }
            m.Show(timerBtn, new Point(timerBtn.Width, 0));
        }

        void StartTimer(int seconds)
        {
            CommitText();
            Rectangle area = sel;
            Hide();
            CountdownForm cd = new CountdownForm(seconds, new Rectangle(virt.X + area.X, virt.Y + area.Y, area.Width, area.Height));
            cd.Finished += delegate (object o, EventArgs ev)
            {
                bool cancelled = cd.Cancelled;
                cd.Close();
                cd.Dispose();
                if (!cancelled)
                {
                    Application.DoEvents();
                    Thread.Sleep(120); // let the countdown window fully disappear
                    GrabScreen();
                }
                Show();
                Bounds = virt;
                Activate();
                Native.SetForegroundWindow(Handle);
                PlaceToolbar();
                Invalidate();
            };
            cd.Show();
        }

        // ---------- painting ----------
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            PaintMain(e);
            if (tool == Tool.Picker && !sel.IsEmpty && mousePos.X >= 0) DrawMagnifier(e.Graphics);
        }

        void PaintMain(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.DrawImageUnscaled(dimmed, 0, 0);
            if (!sel.IsEmpty)
            {
                g.DrawImage(shot, sel, sel, GraphicsUnit.Pixel);
                GraphicsState st = g.Save();
                g.SmoothingMode = SmoothingMode.AntiAlias;
                foreach (Shape s in shapes) s.Draw(g, shot);
                if (current != null) current.Draw(g, shot);
                DrawTextEditing(g);
                g.Restore(st);
#if OCR
                DrawOcr(g);
#endif

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
#if OCR
            if (e.Button == MouseButtons.Right && ocr != null) { ExitTextSelect(); return; }
#endif
            if (e.Button == MouseButtons.Right) { Close(); return; }
            if (e.Button != MouseButtons.Left) return;
            if (sizeBox.Focused) { ApplySizeBox(); Focus(); }
            CommitText();
#if OCR
            if (ocr != null)
            {
                int w = WordAt(e.Location);
                if (w >= 0)
                {
                    if ((Control.ModifierKeys & Keys.Shift) != 0 && ocrAnchor >= 0) ocrFocus = w;
                    else { ocrAnchor = w; ocrFocus = w; }
                    ocrDragging = true;
                    Invalidate();
                }
                return;
            }
#endif

            if (tool == Tool.Picker)
            {
                PickPixel(e.Location, (Control.ModifierKeys & Keys.Shift) != 0);
                return;
            }

            int edges = HitEdges(e.Location);
            if (edges != 0) // dragging an edge or corner of the finished selection
            {
                resizeEdges = edges;
                bar.Visible = false; side.Visible = false;
                return;
            }

            if (sel.IsEmpty || tool == Tool.Select)
            {

                selecting = true;
                spaceMove = false;
                dragStart = e.Location;
                dragEnd = e.Location;
                sel = Rectangle.Empty;
                bar.Visible = false; side.Visible = false;
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
                case Tool.Number:
                    NumberShape ns = new NumberShape();
                    ns.Center = e.Location;
                    ns.Color = color;
                    ns.Width = penWidth * scale;
                    int max = 0;
                    foreach (Shape sh in shapes) { NumberShape n = sh as NumberShape; if (n != null && n.Number > max) max = n.Number; }
                    ns.Number = max + 1;
                    shapes.Add(ns);
                    Invalidate();
                    return;
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
#if OCR
            if (ocr != null)
            {
                if (Cursor != Cursors.IBeam) Cursor = Cursors.IBeam;
                if (ocrDragging)
                {
                    int w = WordAt(e.Location);
                    if (w >= 0 && w != ocrFocus) { ocrFocus = w; Invalidate(); }
                }
                return;
            }
#endif
            if (tool == Tool.Picker && !selecting && resizeEdges == 0)
            {
                mousePos = e.Location;
                if (Cursor != Cursors.Cross) Cursor = Cursors.Cross;
                Invalidate();
                return;
            }
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
                Point p = new Point(Math.Max(0, Math.Min(ClientSize.Width, e.X)), Math.Max(0, Math.Min(ClientSize.Height, e.Y)));
                PenShape pen = current as PenShape;
                if (pen != null && pen.Marker)
                {
                    bool shift = (Control.ModifierKeys & Keys.Shift) != 0;
                    if (shift && !pen.Straight) { pen.Straight = true; }
                    else if (!shift && pen.Straight)
                    {
                        // Shift released: carry on freehand from the end of the straight segment
                        pen.Straight = false;
                        Point a = pen.Points[0], b = pen.Points[pen.Points.Count - 1];
                        pen.Points.Clear();
                        int steps = Math.Max(2, (int)(Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)) / 12));
                        for (int i = 0; i <= steps; i++)
                            pen.Points.Add(new Point(a.X + (b.X - a.X) * i / steps, a.Y + (b.Y - a.Y) * i / steps));
                    }
                }
                current.Update(p);
                Invalidate();
            }
            else if (sel.IsEmpty)
            {
                Invalidate(); // the hint follows the cursor to its monitor
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
#if OCR
            if (ocr != null) { ocrDragging = false; return; }
#endif
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
                Invalidate();
            }
        }

        // ---------- text ----------
        System.Windows.Forms.Timer caretTimer;
        bool caretOn;

        void StartText(Point at)
        {
            textShape = new TextShape();
            textShape.Color = color;
            textShape.Width = penWidth * scale;
            textShape.At = at;
            textShape.FontName = Settings.TextFontName;
            textShape.Style = (FontStyle)(Settings.TextFontStyle & 7);
            // The input box is hidden off-screen; text is drawn straight onto the screenshot, no backdrop
            textBox = new TextBox();
            textBox.Multiline = true;
            textBox.AcceptsReturn = true;
            textBox.Location = new Point(-10000, -10000);
            textBox.Size = new Size(300, 40);
            textBox.KeyDown += delegate (object s, KeyEventArgs ke)
            {
                if (ke.KeyCode == Keys.Enter && !ke.Shift) { ke.SuppressKeyPress = true; CommitText(); }
                else if (ke.KeyCode == Keys.Escape) { ke.SuppressKeyPress = true; CancelText(); }
            };
            textBox.TextChanged += delegate
            {
                if (textShape == null) return;
                textShape.Text = textBox.Text;
                caretOn = true;
                Invalidate();
            };
            Controls.Add(textBox);
            textBox.Focus();
            if (caretTimer == null)
            {
                caretTimer = new System.Windows.Forms.Timer();
                caretTimer.Interval = 530;
                caretTimer.Tick += delegate { if (textShape != null) { caretOn = !caretOn; Invalidate(); } };
            }
            caretOn = true;
            caretTimer.Start();
            Invalidate();
        }

        void DrawTextEditing(Graphics g)
        {
            if (textShape == null) return;
            textShape.Draw(g, shot);
            using (Font f = textShape.MakeFont())
            {
                string[] lines = (textShape.Text ?? "").Replace("\r", "").Split('\n');
                string last = lines[lines.Length - 1];
                float lineH = f.GetHeight(g);
                float w = 0;
                if (last.Length > 0)
                {
                    using (StringFormat sf = StringFormat.GenericTypographic)
                    {
                        sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                        w = g.MeasureString(last, f, PointF.Empty, sf).Width;
                    }
                }
                float x = textShape.At.X + w + 3, y = textShape.At.Y + lineH * (lines.Length - 1) + lineH * 0.12f;
                if (caretOn)
                    using (Pen cp = new Pen(textShape.Color, Math.Max(1.5f, scale * 1.5f)))
                        g.DrawLine(cp, x, y, x, y + lineH * 0.8f);
            }
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
            if (caretTimer != null) caretTimer.Stop();
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
#if OCR
            if (ocr != null)
            {
                switch (keyData)
                {
                    case Keys.Escape: ExitTextSelect(); return true;
                    case Keys.Control | Keys.C: CopyOcrSelection(); return true;
                    case Keys.Control | Keys.A: ocrAnchor = 0; ocrFocus = ocr.Words.Count - 1; Invalidate(); return true;
                    case Keys.Control | Keys.T: CopyText(ocr.All); return true;
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }
#endif
            switch (keyData)
            {
                case Keys.Escape: Close(); return true;
                case Keys.Control | Keys.C: CopyToClipboard(); return true;
                case Keys.Control | Keys.S: SaveToFolder(); return true;
                case Keys.Control | Keys.Shift | Keys.S: SaveAs(); return true;
                case Keys.Control | Keys.Z: Undo(); return true;
#if OCR
                case Keys.Control | Keys.T: RecognizeText(); return true;
#endif
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
#if OCR
            if (ocr != null) { CopyOcrSelection(); return; }
#endif
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

#if OCR
    // Text recognition with the OCR engine built into Windows 10/11 (Windows.Media.Ocr).
    // Works offline; recognizes the languages whose OCR packs are installed in Windows.
    class OcrWordBox
    {
        public string Text;
        public RectangleF R;   // overlay coordinates
        public int Line;
    }

    class OcrData
    {
        public List<OcrWordBox> Words = new List<OcrWordBox>();

        // Words from..to (inclusive): spaces inside a line, line breaks between lines
        public string Join(int from, int to)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = Math.Max(0, from); i <= to && i < Words.Count; i++)
            {
                if (i > from) sb.Append(Words[i].Line != Words[i - 1].Line ? "\r\n" : " ");
                sb.Append(Words[i].Text);
            }
            return sb.ToString();
        }
        public string All { get { return Join(0, Words.Count - 1); } }
    }

    static class OcrHelper
    {
        // offset = where the bitmap sits on the overlay, so word boxes come back in overlay coordinates
        public static async System.Threading.Tasks.Task<OcrData> RecognizeAsync(Bitmap bmp, Point offset)
        {
            global::Windows.Media.Ocr.OcrEngine engine = global::Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null)
                throw new InvalidOperationException(L.T(
                    "у Windows немає мовного пакета для розпізнавання тексту (Параметри → Час і мова → Мова та регіон → додайте мову).",
                    "Windows has no OCR language pack installed (Settings → Time & language → Language & region → add a language)."));

            // small text is recognized better when enlarged
            int max = (int)global::Windows.Media.Ocr.OcrEngine.MaxImageDimension;
            float k = 1f;
            if (Math.Max(bmp.Width, bmp.Height) * 2 <= max && Math.Min(bmp.Width, bmp.Height) < 600) k = 2f;
            Bitmap work = bmp;
            if (k != 1f || Math.Max(bmp.Width, bmp.Height) > max)
            {
                if (Math.Max(bmp.Width, bmp.Height) * k > max) k = (float)max / Math.Max(bmp.Width, bmp.Height);
                work = new Bitmap(Math.Max(1, (int)(bmp.Width * k)), Math.Max(1, (int)(bmp.Height * k)), PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(work))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(bmp, new Rectangle(0, 0, work.Width, work.Height));
                }
            }
            try
            {
                // Pixels go to Windows directly (no streams/decoders) — avoids the System.Runtime.WindowsRuntime helpers
                Rectangle all = new Rectangle(0, 0, work.Width, work.Height);
                BitmapData bd = work.LockBits(all, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                byte[] px = new byte[work.Width * work.Height * 4];
                try
                {
                    for (int y = 0; y < work.Height; y++)
                        Marshal.Copy(new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), px, y * work.Width * 4, work.Width * 4);
                }
                finally { work.UnlockBits(bd); }
                global::Windows.Storage.Streams.IBuffer buf = global::Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(px);
                global::Windows.Graphics.Imaging.SoftwareBitmap sb = global::Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromBuffer(
                    buf, global::Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, work.Width, work.Height,
                    global::Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
                global::Windows.Media.Ocr.OcrResult res = await AsTask(engine.RecognizeAsync(sb));
                OcrData data = new OcrData();
                int li = 0;
                foreach (global::Windows.Media.Ocr.OcrLine line in res.Lines)
                {
                    foreach (global::Windows.Media.Ocr.OcrWord w in line.Words)
                    {
                        global::Windows.Foundation.Rect br = w.BoundingRect;
                        OcrWordBox wb = new OcrWordBox();
                        wb.Text = w.Text;
                        wb.Line = li;
                        wb.R = new RectangleF((float)(br.X / k) + offset.X, (float)(br.Y / k) + offset.Y,
                                              (float)(br.Width / k), (float)(br.Height / k));
                        data.Words.Add(wb);
                    }
                    li++;
                }
                return data;
            }
            finally
            {
                if (work != bmp) work.Dispose();
            }
        }

        // Our own await for WinRT async operations (no dependency on System.Runtime.WindowsRuntime.dll)
        static System.Threading.Tasks.Task<T> AsTask<T>(global::Windows.Foundation.IAsyncOperation<T> op)
        {
            System.Threading.Tasks.TaskCompletionSource<T> tcs = new System.Threading.Tasks.TaskCompletionSource<T>();
            op.Completed = new global::Windows.Foundation.AsyncOperationCompletedHandler<T>(delegate (global::Windows.Foundation.IAsyncOperation<T> o, global::Windows.Foundation.AsyncStatus st)
            {
                if (st == global::Windows.Foundation.AsyncStatus.Completed) tcs.TrySetResult(o.GetResults());
                else if (st == global::Windows.Foundation.AsyncStatus.Error)
                    tcs.TrySetException(((global::Windows.Foundation.IAsyncInfo)o).ErrorCode ?? new Exception("OCR error"));
                else tcs.TrySetCanceled();
            });
            return tcs.Task;
        }
    }
#endif

    // A screenshot pinned on top of all windows. Drag to move, wheel to zoom, Ctrl+wheel for opacity.
    class PinForm : Form
    {
        readonly Bitmap img;
        readonly TrayApp app;
        float zoom = 1f;
        Point dragOffset;
        bool dragging;

        public PinForm(Bitmap image, Point location, TrayApp app)
        {
            img = image;
            this.app = app;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;
            Cursor = Cursors.SizeAll;
            Location = location;
            ClientSize = img.Size;
            Text = "Snipper";

            ContextMenuStrip m = new ContextMenuStrip();
            m.Items.Add(L.T("Копіювати", "Copy"), null, delegate { try { Clipboard.SetImage(img); } catch { } });
            m.Items.Add(L.T("Зберегти в папку", "Save to folder"), null, delegate { Save(); });
            m.Items.Add(L.T("Справжній розмір", "Actual size"), null, delegate { SetZoom(1f, Point.Empty); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(L.T("Закрити", "Close"), null, delegate { Close(); });
            ContextMenuStrip = m;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_DPICHANGED) { m.Result = IntPtr.Zero; return; }
            base.WndProc(ref m);
        }

        void Save()
        {
            string folder = Settings.SaveFolder;
            try
            {
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, Settings.NextFreeName(folder));
                img.Save(path, ImageFormat.Png);
                app.Notify(L.T("Збережено: ", "Saved: ") + Path.GetFileName(path), path);
            }
            catch (Exception ex) { app.Notify(L.T("Не вдалося зберегти: ", "Couldn't save: ") + ex.Message, null); }
        }

        void SetZoom(float z, Point anchor)
        {
            z = Math.Max(0.1f, Math.Min(5f, z));
            Size old = ClientSize;
            Size ns = new Size(Math.Max(16, (int)(img.Width * z)), Math.Max(16, (int)(img.Height * z)));
            // keep the point under the cursor in place
            int nx = Left + anchor.X - anchor.X * ns.Width / Math.Max(1, old.Width);
            int ny = Top + anchor.Y - anchor.Y * ns.Height / Math.Max(1, old.Height);
            zoom = z;
            Bounds = new Rectangle(nx, ny, ns.Width, ns.Height);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.InterpolationMode = zoom == 1f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(img, ClientRectangle);
            using (Pen p = new Pen(Color.FromArgb(0x6E, 0xA5, 0xFF)))
                e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) { dragging = true; dragOffset = e.Location; }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) Location = new Point(Cursor.Position.X - dragOffset.X, Cursor.Position.Y - dragOffset.Y);
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; }
        protected override void OnMouseDoubleClick(MouseEventArgs e) { base.OnMouseDoubleClick(e); if (e.Button == MouseButtons.Left) Close(); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if ((ModifierKeys & Keys.Control) != 0)
                Opacity = Math.Max(0.2, Math.Min(1.0, Opacity + (e.Delta > 0 ? 0.1 : -0.1)));
            else
                SetZoom(zoom * (e.Delta > 0 ? 1.1f : 1f / 1.1f), e.Location);
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            if (keyData == (Keys.Control | Keys.C)) { try { Clipboard.SetImage(img); } catch { } return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) img.Dispose();
            base.Dispose(disposing);
        }
    }

    // Small countdown badge shown while the timer runs. Click it to cancel.
    class CountdownForm : Form
    {
        int left;
        readonly System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
        public bool Cancelled;
        public event EventHandler Finished;

        public CountdownForm(int seconds, Rectangle area)
        {
            left = seconds;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(32, 32, 35);
            Opacity = 0.9;
            Cursor = Cursors.Hand;
            float sc;
            using (Graphics g = CreateGraphics()) sc = Math.Max(1f, g.DpiX / 96f);
            int sz = (int)(56 * sc);
            Size = new Size(sz, sz);
            // just outside the top-right corner of the area so it never ends up in the shot
            int x = area.Right + (int)(8 * sc), y = area.Top;
            Rectangle wa = Screen.FromRectangle(area).WorkingArea;
            if (x + sz > wa.Right) x = area.Left - sz - (int)(8 * sc);
            if (x < wa.Left) x = wa.Left;
            Location = new Point(x, y);
            using (GraphicsPath gp = new GraphicsPath()) { gp.AddEllipse(0, 0, sz, sz); Region = new Region(gp); }
            t.Interval = 1000;
            t.Tick += delegate
            {
                left--;
                if (left <= 0) { t.Stop(); Hide(); if (Finished != null) Finished(this, EventArgs.Empty); }
                else Invalidate();
            };
            Click += delegate { t.Stop(); Cancelled = true; Hide(); if (Finished != null) Finished(this, EventArgs.Empty); };
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); t.Start(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(Color.FromArgb(0x8E, 0xDC, 0xF0), Width * 0.06f))
                g.DrawEllipse(p, Width * 0.05f, Width * 0.05f, Width * 0.9f, Height * 0.9f);
            using (Font f = new Font("Segoe UI", Height * 0.42f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                g.DrawString(left.ToString(CultureInfo.InvariantCulture), f, Brushes.White, ClientRectangle, sf);
            }
        }

        protected override void Dispose(bool disposing) { if (disposing) t.Dispose(); base.Dispose(disposing); }
    }

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
            ToolStripItem unpinItem = menu.Items.Add("", null, delegate
            {
                List<Form> pins = new List<Form>();
                foreach (Form f in Application.OpenForms) if (f is PinForm) pins.Add(f);
                foreach (Form f in pins) f.Close();
            });
            menu.Items.Add(new ToolStripSeparator());
            ToolStripItem exitItem = menu.Items.Add("", null, delegate { ExitThread(); });
            menu.Opening += delegate
            {
                captureItem.Text = L.T("Зробити скріншот", "Take screenshot");
                openItem.Text = L.T("Відкрити папку зі скріншотами", "Open screenshots folder");
                chooseItem.Text = L.T("Вибрати папку для збереження…", "Choose save folder…");
                autostartItem.Text = L.T("Запускати разом із Windows", "Start with Windows");
                exitItem.Text = L.T("Вихід", "Exit");
                int pins = 0;
                foreach (Form f in Application.OpenForms) if (f is PinForm) pins++;
                unpinItem.Text = L.T("Закрити всі закріплені знімки", "Close all pinned shots") + " (" + pins + ")";
                unpinItem.Visible = pins > 0;
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

        // App icon (the gradient one from app.ico), at the size Windows uses for the tray.
        const string AppIconData = "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";

        static Icon MakeIcon()
        {
            using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(AppIconData)))
                return new Icon(ms, SystemInformation.SmallIconSize);
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
