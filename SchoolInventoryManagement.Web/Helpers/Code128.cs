using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;

namespace SchoolInventoryManagement.Web.Helpers
{
    // Draws a Code 128 barcode (code set B) as SVG, so the app needs no
    // barcode package. Code set B covers every character an asset code can
    // hold: capital letters, digits and "-". Any USB scanner reads it and
    // types the code followed by Enter.
    public static class Code128
    {
        // Bar/space widths for symbol values 0-106, in modules. Each starts
        // with a bar. 104 is Start B; 106 is Stop (seven elements).
        private static readonly string[] Patterns =
        {
            "212222","222122","222221","121223","121322","131222","122213","122312","132212","221213",
            "221312","231212","112232","122132","122231","113222","123122","123221","223211","221132",
            "221231","213212","223112","312131","311222","321122","321221","312212","322112","322211",
            "212123","212321","232121","111323","131123","131321","112313","132113","132311","211313",
            "231113","231311","112133","112331","132131","113123","113321","133121","313121","211331",
            "231131","213113","213311","213131","311123","311321","331121","312113","312311","332111",
            "314111","221411","431111","111224","111422","121124","121421","141122","141221","112214",
            "112412","122114","122411","142112","142211","241211","221114","413111","241112","134111",
            "111242","121142","121241","114212","124112","124211","411212","421112","421211","212141",
            "214121","412121","111143","111341","131141","114113","114311","411113","411311","113141",
            "114131","311141","411131","211412","211214","211232","2331112"
        };

        private const int StartB = 104;
        private const int Stop = 106;
        private const int QuietZone = 10; // modules of white on each side

        // True = black, one entry per module, quiet zones included.
        public static bool[] Modules(string text)
        {
            if (string.IsNullOrEmpty(text))
                throw new ArgumentException("Nothing to encode.", nameof(text));

            var values = new List<int> { StartB };
            foreach (var ch in text)
            {
                if (ch < 32 || ch > 126)
                    throw new ArgumentException($"'{ch}' cannot be put in a Code 128 barcode.", nameof(text));
                values.Add(ch - 32);
            }

            var checksum = StartB;
            for (var i = 1; i < values.Count; i++)
                checksum += values[i] * i;
            values.Add(checksum % 103);
            values.Add(Stop);

            var modules = new List<bool>(new bool[QuietZone]);
            foreach (var value in values)
            {
                var black = true;
                foreach (var width in Patterns[value])
                {
                    for (var w = 0; w < width - '0'; w++)
                        modules.Add(black);
                    black = !black;
                }
            }
            modules.AddRange(new bool[QuietZone]);
            return modules.ToArray();
        }

        // The barcode with the code printed under it. moduleWidth and
        // barHeight are in SVG units (pixels on screen).
        public static string Svg(string text, int moduleWidth = 2, int barHeight = 60)
        {
            var modules = Modules(text);
            var width = modules.Length * moduleWidth;
            const int textHeight = 18;
            var height = barHeight + textHeight + 4;

            var svg = new StringBuilder();
            svg.Append(CultureInfo.InvariantCulture,
                $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\" shape-rendering=\"crispEdges\">");
            svg.Append(CultureInfo.InvariantCulture, $"<rect width=\"{width}\" height=\"{height}\" fill=\"#fff\"/>");

            // One rect per run of black modules.
            for (var i = 0; i < modules.Length;)
            {
                if (!modules[i]) { i++; continue; }
                var start = i;
                while (i < modules.Length && modules[i]) i++;
                svg.Append(CultureInfo.InvariantCulture,
                    $"<rect x=\"{start * moduleWidth}\" y=\"0\" width=\"{(i - start) * moduleWidth}\" height=\"{barHeight}\" fill=\"#000\"/>");
            }

            svg.Append(CultureInfo.InvariantCulture,
                $"<text x=\"{width / 2}\" y=\"{barHeight + textHeight}\" text-anchor=\"middle\" font-family=\"Consolas, monospace\" font-size=\"16\" fill=\"#000\">{WebUtility.HtmlEncode(text)}</text>");
            svg.Append("</svg>");
            return svg.ToString();
        }
    }
}
