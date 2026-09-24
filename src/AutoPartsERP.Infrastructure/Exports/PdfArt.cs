using System.Globalization;
using QuestPDF.Infrastructure;

namespace AutoPartsERP.Infrastructure.Exports;

/// <summary>
/// The vector pieces of the printed forms, as SVG: the faint wave lines behind every page, the corner brackets of the title
/// frame, rounded and dashed boxes (sized to the space they get), and the small line icons (Lucide, ISC licence).
/// </summary>
internal static class PdfArt
{
    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Soft wave lines across the top and the bottom of the page (drawn behind everything, very light grey).</summary>
    public static string Waves(Size size)
    {
        double w = size.Width, h = size.Height;
        var paths = new List<string>();
        void Wave(double y, double amplitude, double phase, string color, double width) =>
            paths.Add($"<path d='M-20 {F(y)} C {F(w * 0.25)} {F(y - amplitude + phase)}, {F(w * 0.55)} {F(y + amplitude)}, {F(w + 20)} {F(y - amplitude * 0.4 + phase)}' " +
                      $"fill='none' stroke='{color}' stroke-width='{F(width)}'/>");

        for (var i = 0; i < 6; i++)
        {
            Wave(18 + i * 16, 14 + i * 2, i % 2 == 0 ? 4 : -6, i % 3 == 0 ? "#E6E6E6" : "#EEEEEE", i % 2 == 0 ? 1.1 : 0.8);
        }

        for (var i = 0; i < 5; i++)
        {
            Wave(h - 110 + i * 20, 12 + i * 3, i % 2 == 0 ? -5 : 5, i % 3 == 0 ? "#E6E6E6" : "#EEEEEE", i % 2 == 0 ? 1.1 : 0.8);
        }

        return $"<svg xmlns='http://www.w3.org/2000/svg' width='{F(w)}' height='{F(h)}' viewBox='0 0 {F(w)} {F(h)}'>{string.Concat(paths)}</svg>";
    }

    /// <summary>One L-shaped corner bracket of the title frame; <paramref name="corner"/> is tl, tr, bl or br.</summary>
    public static string Corner(string corner, string color, double size = 12, double stroke = 2.2)
    {
        var s = size - stroke / 2;
        var o = stroke / 2;
        var d = corner switch
        {
            "tl" => $"M{F(o)} {F(s)} V{F(o)} H{F(s)}",
            "tr" => $"M{F(o)} {F(o)} H{F(s)} V{F(s)}",
            "bl" => $"M{F(o)} {F(o)} V{F(s)} H{F(s)}",
            _ => $"M{F(o)} {F(s)} H{F(s)} V{F(o)}",
        };
        return $"<svg xmlns='http://www.w3.org/2000/svg' width='{F(size)}' height='{F(size)}' viewBox='0 0 {F(size)} {F(size)}'>" +
               $"<path d='{d}' fill='none' stroke='{color}' stroke-width='{F(stroke)}' stroke-linecap='square'/></svg>";
    }

    /// <summary>A rounded rectangle filling <paramref name="size"/>: optional fill, a border, dashed when <paramref name="dash"/> is set.</summary>
    public static string Box(Size size, double radius, string stroke, double strokeWidth, string? fill = null, string? dash = null)
    {
        var inset = strokeWidth / 2;
        var dashAttr = dash is null ? string.Empty : $" stroke-dasharray='{dash}'";
        return $"<svg xmlns='http://www.w3.org/2000/svg' width='{F(size.Width)}' height='{F(size.Height)}' viewBox='0 0 {F(size.Width)} {F(size.Height)}'>" +
               $"<rect x='{F(inset)}' y='{F(inset)}' width='{F(Math.Max(0, size.Width - strokeWidth))}' height='{F(Math.Max(0, size.Height - strokeWidth))}' " +
               $"rx='{F(radius)}' fill='{fill ?? "none"}' stroke='{stroke}' stroke-width='{F(strokeWidth)}'{dashAttr}/></svg>";
    }

    /// <summary>A dashed horizontal line across <paramref name="size"/>.</summary>
    public static string DashedLine(Size size, string color, double width = 1.2) =>
        $"<svg xmlns='http://www.w3.org/2000/svg' width='{F(size.Width)}' height='{F(size.Height)}' viewBox='0 0 {F(size.Width)} {F(size.Height)}'>" +
        $"<line x1='0' y1='{F(size.Height / 2)}' x2='{F(size.Width)}' y2='{F(size.Height / 2)}' stroke='{color}' stroke-width='{F(width)}' stroke-dasharray='6 4'/></svg>";

    /// <summary>The round company stamp of a receipt: a double ring with "approved" and the company's name.</summary>
    public static string Stamp(string color) =>
        "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64' viewBox='0 0 64 64'>" +
        $"<circle cx='32' cy='32' r='30' fill='none' stroke='{color}' stroke-width='1.6'/>" +
        $"<circle cx='32' cy='32' r='26.5' fill='none' stroke='{color}' stroke-width='0.6'/></svg>";

    private static readonly Dictionary<string, string> Icons = new(StringComparer.Ordinal)
    {
        ["phone"] = "<path d='M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72 12.84 12.84 0 0 0 .7 2.81 2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45 12.84 12.84 0 0 0 2.81.7A2 2 0 0 1 22 16.92z'/>",
        ["mail"] = "<rect x='2' y='4' width='20' height='16' rx='2'/><path d='m22 7-8.97 5.7a1.94 1.94 0 0 1-2.06 0L2 7'/>",
        ["pin"] = "<path d='M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0Z'/><circle cx='12' cy='10' r='3'/>",
        ["file"] = "<path d='M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z'/><path d='M14 2v4a2 2 0 0 0 2 2h4'/><path d='M10 9H8'/><path d='M16 13H8'/><path d='M16 17H8'/>",
        ["pen"] = "<path d='M12 20h9'/><path d='M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z'/>",
        ["download"] = "<path d='M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4'/><path d='M7 10l5 5 5-5'/><path d='M12 15V3'/>",
        ["message"] = "<path d='M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z'/>",
        ["building"] = "<rect width='16' height='20' x='4' y='2' rx='2'/><path d='M9 22v-4h6v4'/><path d='M8 6h.01M16 6h.01M12 6h.01M12 10h.01M12 14h.01M16 10h.01M16 14h.01M8 10h.01M8 14h.01'/>",
        ["user"] = "<path d='M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2'/><circle cx='9' cy='7' r='4'/><path d='M16 11l2 2 4-4'/>",
        ["calendar"] = "<rect width='18' height='18' x='3' y='4' rx='2'/><path d='M16 2v4M8 2v4M3 10h18'/>",
    };

    /// <summary>A line icon by name (phone, mail, pin, file, pen, download, message, building, user, calendar).</summary>
    public static string Icon(string name, string color, double strokeWidth = 1.8) =>
        $"<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24' fill='none' stroke='{color}' stroke-width='{F(strokeWidth)}' " +
        $"stroke-linecap='round' stroke-linejoin='round'>{Icons[name]}</svg>";
}
