using System;
using Microsoft.Win32;

namespace SCAnimator.V261.UI {
    internal enum ThemeChoice { Dark, Light }

    internal static class ThemeSettings {
        private const string KeyPath = @"Software\SCAnimator\V261";
        private static ThemeChoice current = Load();
        public static event EventHandler Changed;
        public static ThemeChoice Current { get { return current; } }

        private static ThemeChoice Load() {
            try {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath)) {
                    return key != null && String.Equals(key.GetValue("Theme") as string, "Light",
                        StringComparison.OrdinalIgnoreCase) ? ThemeChoice.Light : ThemeChoice.Dark;
                }
            } catch { return ThemeChoice.Dark; }
        }
        public static void Set(ThemeChoice choice) {
            if (current == choice) return;
            current = choice;
            try {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
                    if (key != null) key.SetValue("Theme", choice.ToString(), RegistryValueKind.String);
            } catch { /* The selected theme still applies for this session. */ }
            EventHandler changed = Changed;
            if (changed != null) changed(null, EventArgs.Empty);
        }
    }
}
