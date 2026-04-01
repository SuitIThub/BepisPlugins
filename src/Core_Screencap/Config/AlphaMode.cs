using System;
using System.ComponentModel;
using System.Linq;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Screencap
{
    /// <summary>  
    /// Different types of transparency used in screenshots.
    /// </summary>  
    public enum AlphaMode
    {
        [Description("No transparency")]
        None = 0,
        [Description("Cutout transparency")]
        blackout = 1,
        [Description("Gradual transparency")]
        rgAlpha = 2,
        [Description("Composite")]
        composite = 3,
    }

    internal static class AlphaModeUtils
    {
        private static string GetDisplayName(this AlphaMode mode)
        {
            switch (mode)
            {
                case AlphaMode.None:
                    return "No";
                case AlphaMode.blackout:
                    return "Cutout";
                case AlphaMode.rgAlpha:
                    return "Gradual";
                case AlphaMode.composite:
                    return "Composite";
                default:
                    return null;
            }
        }

        public static readonly AlphaMode Default = AlphaMode.rgAlpha;

        public static readonly string[] AllModes = Enum.GetValues(typeof(AlphaMode)).Cast<AlphaMode>().OrderBy(x => (int)x).Select(x => x.GetDisplayName()).ToArray();

        /// <summary>
        /// Parses a transparency mode from UI labels (<see cref="AllModes"/>), enum member names, or a numeric index string.
        /// Availabe modes are: No, Cutout, Gradual, Composite
        /// </summary>
        internal static bool TryParseAlphaModeName(string name, out AlphaMode mode)
        {
            mode = default;
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var trimmed = name.Trim();
            if (int.TryParse(trimmed, out int idx) && idx >= 0 && idx <= (int)AlphaMode.composite)
            {
                mode = (AlphaMode)idx;
                return Enum.IsDefined(typeof(AlphaMode), mode);
            }

            foreach (AlphaMode m in Enum.GetValues(typeof(AlphaMode)))
            {
                if (string.Equals(m.ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    mode = m;
                    return true;
                }

                if (string.Equals(m.GetDisplayName(), trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    mode = m;
                    return true;
                }
            }

            return false;
        }
    }
}
