using System.Collections.Generic;
using System.Windows.Media;

namespace deskmonitor
{
    internal static class AccentPalette
    {
        public static IReadOnlyList<(string Id, string Name, Color Color)> Presets { get; } =
            new (string Id, string Name, Color Color)[]
            {
                ("purple", "紫色", Color.FromRgb(0x76, 0x42, 0xBA)),
                ("blue", "蓝色", Color.FromRgb(0x00, 0x62, 0xB2)),
                ("green", "绿色", Color.FromRgb(0x10, 0x76, 0x42)),
                ("teal", "青色", Color.FromRgb(0x00, 0x71, 0x78)),
                ("orange", "橙色", Color.FromRgb(0xAE, 0x51, 0x00)),
            };

        public static Color? PresetColor(string id)
        {
            foreach (var preset in Presets)
                if (preset.Id == id) return preset.Color;
            return null;
        }
    }
}
