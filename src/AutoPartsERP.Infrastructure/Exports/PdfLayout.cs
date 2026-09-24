using System.Globalization;
using AutoPartsERP.Contracts.Exports;
using AutoPartsERP.Contracts.Settings;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AutoPartsERP.Infrastructure.Exports;

/// <summary>
/// The printed form shared by every document: faint waves behind the page, the title in a bracketed frame, the meta line,
/// the parties, the tables, and the footer with the company's contacts. The optional parts of <see cref="ExportLayout"/>
/// (receipt body, totals, cards, terms, signatures, stamp, payment details, QR codes) are drawn when a document has them.
/// Monochrome by design (it prints the same on any printer); the only colour is green for credits and "WhatsApp".
/// </summary>
internal static class PdfLayout
{
    public const string Arabic = "Tajawal";
    public const string Mono = "IBM Plex Mono";
    public const string Fallback = "Noto Sans";

    private const string Ink = "#141414";
    private const string Text = "#2B2B2B";
    private const string Muted = "#5E5E5E";
    private const string Faint = "#9A9A9A";
    private const string Rule = "#DEDEDE";
    private const string Soft = "#F6F6F6";
    private const string Panel = "#F9F9F9";
    private const string Green = "#1E8A4C";
    private const string White = "#FFFFFF";

    private const float PageMargin = 24;

    /// <summary>Advance of one IBM Plex Mono character, in em — used to size figures so they never split across lines.</summary>
    private const float MonoAdvance = 0.61f;

    // ------------------------------------------------------------------ page

    public static void Compose(IDocumentContainer container, ExportDocument doc, CompanyProfileDto company)
    {
        var layout = doc.Layout;
        var landscape = layout is null && doc.Tables.Any(t => t.Columns.Count > 6);
        var width = (landscape ? PageSizes.A4.Height : PageSizes.A4.Width) - 2 * PageMargin;

        container.Page(page =>
        {
            page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
            page.MarginHorizontal(PageMargin);
            page.MarginTop(20);
            page.MarginBottom(16);
            page.ContentFromRightToLeft();
            page.DefaultTextStyle(x => x.FontFamily(Arabic, Mono, Fallback).FontSize(10.5f).FontColor(Text));
            page.Background().Svg(size => PdfArt.Waves(size));

            page.Content().Column(col =>
            {
                col.Item().Element(c => TitleFrame(c, doc.Title, doc.Subtitle));

                if (layout is null)
                {
                    GenericBody(col, doc, width);
                    return;
                }

                OfficialBody(col, doc, layout, company, width);
            });

            page.Footer().Element(c => Footer(c, doc, company));
        });
    }

    // ------------------------------------------------------------------ title

    private static void TitleFrame(IContainer container, string title, string? subtitle)
    {
        container.PaddingBottom(8).AlignCenter().Layers(layers =>
        {
            layers.PrimaryLayer().Padding(5).Border(1.1f).BorderColor(Ink).Background(White)
                .PaddingVertical(6).PaddingHorizontal(44).Column(c =>
                {
                    c.Item().AlignCenter().Text(title).FontSize(23).ExtraBold().FontColor(Ink);
                    if (!string.IsNullOrWhiteSpace(subtitle))
                    {
                        c.Item().PaddingTop(2).AlignCenter().Text($"( {subtitle} )").FontSize(10).FontColor(Muted);
                    }
                });

            foreach (var (corner, v, h) in new[] { ("tl", 0, 0), ("tr", 0, 1), ("bl", 1, 0), ("br", 1, 1) })
            {
                var layer = layers.Layer();
                layer = v == 0 ? layer.AlignTop() : layer.AlignBottom();
                layer = h == 0 ? layer.AlignLeft() : layer.AlignRight();
                layer.Width(12).Height(12).Svg(PdfArt.Corner(corner, Ink));
            }
        });
    }

    // ------------------------------------------------------------------ lists and simple documents

    private static void GenericBody(ColumnDescriptor col, ExportDocument doc, float width)
    {
        col.Spacing(12);

        if (doc.Fields.Count > 0)
        {
            col.Item().BorderBottom(1).BorderColor(Rule).PaddingBottom(8).Table(table =>
            {
                table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); });
                foreach (var field in doc.Fields)
                {
                    table.Cell().PaddingVertical(3).PaddingHorizontal(4).Text(t =>
                    {
                        t.Span($"{field.Label} : ").Bold().FontColor(Ink);
                        t.Span(string.IsNullOrWhiteSpace(field.Value) ? "-" : field.Value).FontColor(Text);
                    });
                }
            });
        }

        foreach (var t in doc.Tables)
        {
            col.Item().Element(c => DataTable(c, t, width));
        }

        if (!string.IsNullOrWhiteSpace(doc.Footer))
        {
            col.Item().Element(c => Callout(c, "ملاحظة", doc.Footer));
        }
    }

    // ------------------------------------------------------------------ official documents

    private static void OfficialBody(ColumnDescriptor col, ExportDocument doc, ExportLayout layout, CompanyProfileDto company, float width)
    {
        if (layout.Meta is { Count: > 0 })
        {
            col.Item().Element(c => MetaLine(c, layout.Meta));
        }

        if (layout.Parties is { Count: > 0 })
        {
            col.Item().PaddingTop(12).Element(c => Parties(c, layout.Parties, company));
        }

        if (layout.Opening is not null)
        {
            col.Item().PaddingTop(12).Element(c => OpeningBar(c, layout.Opening));
        }

        if (layout.Receipt is not null)
        {
            col.Item().PaddingTop(16).Element(c => ReceiptBody(c, layout.Receipt));
        }

        foreach (var t in doc.Tables)
        {
            col.Item().PaddingTop(14).Element(c => DataTable(c, t, width));
        }

        if (layout.Summary is { Count: > 0 } || layout.Total is not null)
        {
            col.Item().PaddingTop(10).Element(c => Totals(c, layout.Summary ?? [], layout.Total));
        }

        if (layout.Cards is { Count: > 0 })
        {
            col.Item().PaddingTop(12).Element(c => Cards(c, layout.Cards, width));
        }

        if (layout.Note is not null)
        {
            col.Item().PaddingTop(10).Element(c => Callout(c, layout.Note.Label, layout.Note.Value));
        }

        if (layout.Terms is { Count: > 0 } || layout.RecipientBox)
        {
            col.Item().PaddingTop(10).BorderTop(1).BorderColor(Rule).PaddingTop(10).Element(c => TermsAndRecipient(c, layout));
        }

        if (layout.Signatures is { Count: > 0 } || layout.Stamp)
        {
            // Signatures sit at the foot of the (last) page, as on the paper forms.
            col.Item().ExtendVertical().AlignBottom().PaddingTop(16).Element(c => Signatures(c, layout, company));
        }
    }

    private static void MetaLine(IContainer container, IReadOnlyList<ExportMeta> meta)
    {
        container.BorderBottom(1).BorderColor(Rule).PaddingBottom(10).Row(row =>
        {
            for (var i = 0; i < meta.Count; i++)
            {
                var m = meta[i];
                var cell = row.RelativeItem(Math.Max(8f, m.Label.Length * 1.25f + m.Value.Length));
                cell = i == 0 ? cell.AlignRight() : i == meta.Count - 1 ? cell.AlignLeft() : cell.AlignCenter();
                if (m.Badge)
                {
                    cell.Row(r =>
                    {
                        r.AutoItem().AlignMiddle().Text($"{m.Label} :").FontSize(12).ExtraBold().FontColor(Ink);
                        r.AutoItem().PaddingRight(6).AlignMiddle().Background(Ink).PaddingHorizontal(7).PaddingVertical(1.5f)
                            .Text(m.Value).FontSize(10).Bold().FontColor(White);
                    });
                    continue;
                }

                // Plain text so a long value (a period, a long number) wraps instead of overflowing its third of the line.
                var position = i;
                cell.Text(t =>
                {
                    if (position == meta.Count - 1) { t.AlignLeft(); }
                    else if (position > 0) { t.AlignCenter(); }
                    t.Span($"{m.Label} :  ").FontSize(12).ExtraBold().FontColor(Ink);
                    t.Span(m.Value).FontFamily(Mono, Arabic).FontSize(10.5f).Bold().FontColor(Ink);
                });
            }
        });
    }

    private static void Parties(IContainer container, IReadOnlyList<ExportParty> parties, CompanyProfileDto company)
    {
        container.Row(row =>
        {
            for (var i = 0; i < parties.Count; i++)
            {
                var party = parties[i];
                var (name, lines, email) = party.Company ? CompanyLines(company) : (party.Name, party.Lines ?? [], (string?)null);
                var alignLeft = i > 0;

                row.RelativeItem().Element(cell =>
                {
                    if (party.Accent)
                    {
                        cell = cell.BorderRight(2).BorderColor(Ink).PaddingRight(10);
                    }

                    var block = alignLeft ? cell.AlignLeft() : cell.AlignRight();
                    block.Column(c =>
                    {
                        c.Item().Row(r =>
                        {
                            if (party.Icon is not null)
                            {
                                r.AutoItem().AlignMiddle().PaddingLeft(5).Width(13).Height(13).Svg(PdfArt.Icon(party.Icon, Ink));
                            }

                            r.AutoItem().Text($"{party.Title} :").FontSize(12.5f).ExtraBold().FontColor(Ink);
                        });
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            c.Item().PaddingTop(3).Text(name).FontSize(12).Bold().FontColor(Ink);
                        }

                        foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
                        {
                            c.Item().PaddingTop(1).Text(line).FontSize(10).LineHeight(1.1f).FontColor(Muted);
                        }

                        if (!string.IsNullOrWhiteSpace(email))
                        {
                            c.Item().PaddingTop(2).Text(email.ToUpperInvariant()).FontFamily(Mono).FontSize(8.5f).FontColor(Muted);
                        }
                    });
                });
            }
        });
    }

    private static (string Name, IReadOnlyList<string> Lines, string? Email) CompanyLines(CompanyProfileDto company)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(company.ManagerName)) { lines.Add(company.ManagerName); }
        var address = string.Join("، ", new[] { company.Address, company.City }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (address.Length > 0) { lines.Add(address); }
        if (!string.IsNullOrWhiteSpace(company.TaxNumber)) { lines.Add($"الرقم الضريبي: {company.TaxNumber}"); }
        return (company.Name, lines, company.Email);
    }

    private static void OpeningBar(IContainer container, ExportField opening)
    {
        container.Background(Soft).BorderRight(2.5f).BorderColor(Ink).PaddingVertical(8).PaddingHorizontal(12).Row(r =>
        {
            r.RelativeItem().AlignMiddle().Text($"{opening.Label} :").FontSize(11).Bold().FontColor(Ink);
            r.AutoItem().AlignMiddle().Text(t => AmountSpans(t, opening.Value ?? "0", 11.5f, bold: true));
        });
    }

    private static void ReceiptBody(IContainer container, ExportReceipt receipt)
    {
        RoundedBox(container, 8, Ink, 1.3f, White).Padding(14).Column(col =>
        {
            col.Item().Element(c => RoundedBox(c, 5, "#E2E2E2", 0.8f, Panel)).PaddingVertical(10).PaddingHorizontal(14).Row(r =>
            {
                r.RelativeItem().AlignMiddle().Text($"{receipt.Amount.Label} :").FontSize(13).ExtraBold().FontColor(Ink);
                r.AutoItem().AlignMiddle().Element(a => RoundedBox(a, 4, "#D5D5D5", 0.8f, "#EFEFEF"))
                    .PaddingVertical(4).PaddingHorizontal(12).Text(t => AmountSpans(t, receipt.Amount.Amount, 16, bold: true));
                if (!string.IsNullOrWhiteSpace(receipt.Amount.Equivalent))
                {
                    r.AutoItem().AlignMiddle().PaddingRight(10).ContentFromLeftToRight()
                        .Text($"≈ {receipt.Amount.Equivalent}").FontFamily(Mono, Arabic).FontSize(10).Bold().FontColor(Faint);
                }
            });

            for (var i = 0; i < receipt.Lines.Count; i++)
            {
                var line = receipt.Lines[i];
                var words = receipt.WordsLine == i;
                col.Item().PaddingTop(12).Row(r =>
                {
                    r.ConstantItem(125).AlignBottom().Text($"{line.Label} :").FontSize(12).ExtraBold().FontColor(Ink);
                    r.RelativeItem().BorderBottom(1.2f).BorderColor(Ink).PaddingBottom(3).PaddingRight(8).AlignBottom()
                        .Text(t =>
                        {
                            var span = t.Span(line.Value ?? string.Empty).FontSize(words ? 11.5f : 12).FontColor(Ink);
                            if (words) { span.Italic(); }
                        });
                });
            }
        });
    }

    // ------------------------------------------------------------------ tables

    private static void DataTable(IContainer container, ExportTable t, float width)
    {
        var weights = Enumerable.Range(0, t.Columns.Count).Select(i => t.Widths is { } w && i < w.Count && w[i] > 0 ? w[i] : 1f).ToArray();
        var inner = weights.Select(w => width * w / weights.Sum() - 12).ToArray();

        // Long amounts (six figures and more) carry the currency in the column header instead of after every figure.
        var money = t.MoneyColumns ?? [];
        var unitInHeader = !string.IsNullOrWhiteSpace(t.Currency) && money.Any(c =>
            t.Rows.Append(t.Totals ?? []).Any(r => c < r.Count && Figures(t, c, r[c]) is { Length: > 9 }));
        string Header(int i) => unitInHeader && money.Contains(i) ? $"{t.Columns[i]} ({t.Currency})" : t.Columns[i];

        container.Column(block =>
        {
            if (!string.IsNullOrWhiteSpace(t.Title))
            {
                block.Item().PaddingBottom(6).Text(t.Title).FontSize(12.5f).ExtraBold().FontColor(Ink);
            }

            block.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    for (var i = 0; i < t.Columns.Count; i++)
                    {
                        c.RelativeColumn(t.Widths is { } w && i < w.Count && w[i] > 0 ? w[i] : 1);
                    }
                });

                table.Header(h =>
                {
                    for (var i = 0; i < t.Columns.Count; i++)
                    {
                        var index = i;
                        Align(h.Cell().BorderTop(1.6f).BorderBottom(1.2f).BorderColor(Ink).PaddingVertical(6).PaddingHorizontal(6), t, index)
                            .Text(Header(index)).FontSize(11).ExtraBold().FontColor(Ink);
                    }
                });

                for (var r = 0; r < t.Rows.Count; r++)
                {
                    var row = t.Rows[r];
                    var zebra = r % 2 == 0 ? Soft : White;
                    for (var i = 0; i < t.Columns.Count; i++)
                    {
                        var index = i;
                        var value = i < row.Count ? row[i] : null;
                        var background = t.ShadedColumn == i ? "#EFEFEF" : zebra;
                        Align(table.Cell().Background(background).BorderBottom(0.4f).BorderColor("#EAEAEA").PaddingVertical(3.5f).PaddingHorizontal(6), t, index)
                            .Text(text => CellSpans(text, t, index, value, inner[index], unitInHeader));
                    }
                }

                if (t.Totals is { Count: > 0 })
                {
                    for (var i = 0; i < t.Columns.Count; i++)
                    {
                        var index = i;
                        var value = i < t.Totals.Count ? t.Totals[i] : null;
                        Align(table.Cell().BorderTop(1.2f).BorderColor(Ink).Background("#EFEFEF").PaddingVertical(4).PaddingHorizontal(6), t, index)
                            .Text(text => CellSpans(text, t, index, value, inner[index], unitInHeader, bold: true));
                    }
                }
            });
        });
    }

    private static IContainer Align(IContainer cell, ExportTable t, int column) =>
        t.NumericColumns?.Contains(column) == true && t.MoneyColumns?.Contains(column) != true ? cell.AlignCenter() : cell.AlignRight();

    /// <summary>The largest mono size (up to <paramref name="max"/>) at which <paramref name="chars"/> characters fit in <paramref name="width"/>.</summary>
    private static float Fit(int chars, float width, float max, float min = 6.5f) =>
        chars == 0 ? max : Math.Clamp(width / (chars * MonoAdvance), min, max);

    /// <summary>A money cell as printed (two decimals, thousands separated, a negative in brackets); null when the cell is not a money figure.</summary>
    private static string? Figures(ExportTable t, int column, string? value) =>
        t.MoneyColumns?.Contains(column) == true && decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var money)
            ? money < 0 ? $"({(-money).ToString("N2", CultureInfo.InvariantCulture)})" : money.ToString("N2", CultureInfo.InvariantCulture)
            : null;

    private static void CellSpans(TextDescriptor text, ExportTable t, int column, string? value, float width, bool unitInHeader, bool bold = false)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var positive = t.PositiveColumns?.Contains(column) == true;
        var color = positive && value != "-" ? Green : Ink;

        if (Figures(t, column, value) is { } figures)
        {
            var withUnit = !unitInHeader && !string.IsNullOrWhiteSpace(t.Currency);
            text.Span(figures).FontFamily(Mono).FontSize(Fit(figures.Length, width - (withUnit ? 18 : 0), 10)).Bold().FontColor(color);
            if (withUnit)
            {
                text.Span($" {t.Currency}").FontSize(8.5f).FontColor(positive ? Green : Muted);
            }

            return;
        }

        if (t.NumericColumns?.Contains(column) == true && decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            var shown = number == decimal.Truncate(number) ? number.ToString("N0", CultureInfo.InvariantCulture) : number.ToString("N2", CultureInfo.InvariantCulture);
            var span = text.Span(shown).FontFamily(Mono).FontSize(Fit(shown.Length, width, 10)).FontColor(color);
            if (bold) { span.Bold(); }
            return;
        }

        if (t.CodeColumns?.Contains(column) == true)
        {
            text.Span(value).FontFamily(Mono, Arabic).FontSize(Fit(value.Length, width, 9.5f, 7)).Bold().FontColor(Ink);
            return;
        }

        var plain = text.Span(value).FontSize(10.5f).FontColor(color);
        if (bold) { plain.Bold(); }
    }

    // ------------------------------------------------------------------ totals, cards, notes

    private static void Totals(IContainer container, IReadOnlyList<ExportField> summary, ExportAmount? total)
    {
        container.AlignRight().Width(330).Column(col =>
        {
            foreach (var line in summary)
            {
                col.Item().PaddingVertical(2).Row(r =>
                {
                    r.RelativeItem().Text($"{line.Label} :").FontSize(11.5f).Bold().FontColor(Ink);
                    r.AutoItem().Text(t => AmountSpans(t, line.Value ?? "0", 10.5f, bold: true));
                });
            }

            if (total is null)
            {
                return;
            }

            col.Item().PaddingTop(4).LineHorizontal(1.6f).LineColor(Ink);
            col.Item().PaddingTop(6).Row(r =>
            {
                r.AutoItem().AlignMiddle().Text($"{total.Label} :").FontSize(15.5f).ExtraBold().FontColor(Ink);
                r.RelativeItem().AlignLeft().AlignMiddle().Row(a =>
                {
                    a.AutoItem().AlignMiddle().Text(t => AmountSpans(t, total.Amount, Fit(total.Amount.Length, 125, 15, 9), bold: false));
                    if (!string.IsNullOrWhiteSpace(total.Equivalent))
                    {
                        a.AutoItem().AlignMiddle().PaddingRight(8).ContentFromLeftToRight()
                            .Text($"≈ {total.Equivalent}").FontFamily(Mono, Arabic).FontSize(10).Bold().FontColor(Faint);
                    }
                });
            });
        });
    }

    private static void Cards(IContainer container, IReadOnlyList<ExportCard> cards, float width)
    {
        var inner = (width - 12 * (cards.Count - 1)) / cards.Count - 24;
        container.Row(row =>
        {
            row.Spacing(12);
            foreach (var card in cards)
            {
                row.RelativeItem().Element(c => card.Emphasis ? RoundedBox(c, 5, Ink, 1, Ink) : RoundedBox(c, 5, "#CFCFCF", 0.9f, White))
                    .PaddingVertical(9).PaddingHorizontal(12).Column(col =>
                    {
                        var labelColor = card.Emphasis ? "#E8E8E8" : Ink;
                        var amountColor = card.Emphasis ? White : card.Positive ? Green : Ink;
                        col.Item().Text($"{card.Label} :").FontSize(9.5f).Bold().FontColor(labelColor);
                        col.Item().PaddingTop(6).Row(r =>
                        {
                            r.AutoItem().Text(t => AmountSpans(t, card.Amount, Fit(card.Amount.Length, inner - 40, 14.5f, 8), bold: true, color: amountColor, unitColor: card.Emphasis ? "#E0E0E0" : amountColor));
                            if (!string.IsNullOrWhiteSpace(card.Equivalent))
                            {
                                r.RelativeItem().AlignLeft().AlignBottom().ContentFromLeftToRight()
                                    .Text($"≈ {card.Equivalent}").FontFamily(Mono, Arabic).FontSize(8.5f).FontColor(card.Emphasis ? "#BDBDBD" : Faint);
                            }
                        });
                    });
            }
        });
    }

    private static void Callout(IContainer container, string label, string? text)
    {
        container.Background(Panel).BorderRight(2).BorderColor(Ink).PaddingVertical(6).PaddingHorizontal(10).Text(t =>
        {
            t.Span($"{label} : ").FontSize(9.5f).ExtraBold().FontColor(Ink);
            t.Span(text ?? string.Empty).FontSize(9.5f).FontColor(Muted);
        });
    }

    private static void TermsAndRecipient(IContainer container, ExportLayout layout)
    {
        container.Row(row =>
        {
            if (layout.Terms is { Count: > 0 } terms)
            {
                row.RelativeItem().PaddingLeft(24).Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        r.AutoItem().AlignMiddle().PaddingLeft(5).Width(13).Height(13).Svg(PdfArt.Icon("file", Ink));
                        r.AutoItem().Text("الشروط والأحكام :").FontSize(12).ExtraBold().FontColor(Ink);
                    });
                    col.Item().PaddingTop(5).LineHorizontal(0.8f).LineColor(Rule);
                    foreach (var term in terms)
                    {
                        col.Item().PaddingTop(3).Row(r =>
                        {
                            r.ConstantItem(12).Text("•").FontSize(10).FontColor(Ink);
                            r.RelativeItem().Text(term).FontSize(9.5f).LineHeight(1.2f).FontColor(Text);
                        });
                    }
                });
            }
            else
            {
                row.RelativeItem();
            }

            if (layout.RecipientBox)
            {
                row.ConstantItem(200).Element(RecipientBox);
            }
        });
    }

    private static void RecipientBox(IContainer container)
    {
        RoundedBox(container, 6, "#CFCFCF", 0.9f, Panel).Padding(10).Column(col =>
        {
            col.Item().Row(r =>
            {
                r.AutoItem().AlignMiddle().PaddingLeft(4).Width(12).Height(12).Svg(PdfArt.Icon("pen", Ink));
                r.RelativeItem().AlignMiddle().Text("توقيع المستلم :").FontSize(12).ExtraBold().FontColor(Ink);
                r.AutoItem().AlignMiddle().Element(chip => RoundedBox(chip, 4, "#D5D5D5", 0.8f, White))
                    .PaddingVertical(2).PaddingHorizontal(6).Text("إقرار الاستلام").FontSize(8).FontColor(Muted);
            });
            col.Item().PaddingTop(6).LineHorizontal(0.8f).LineColor(Rule);
            col.Item().PaddingTop(14).AlignCenter().Text("(التوقيع هنا / Signature)").Italic().FontSize(8.5f).FontColor(Faint);
            col.Item().PaddingTop(3).Height(6).Svg(size => PdfArt.DashedLine(size, "#BDBDBD"));
            col.Item().PaddingTop(8).Row(r =>
            {
                r.RelativeItem().Text("اسم المستلم:").FontSize(9.5f).FontColor(Muted);
                r.AutoItem().Text("..............................").FontSize(9.5f).FontColor(Faint);
            });
            col.Item().PaddingTop(4).Row(r =>
            {
                r.RelativeItem().Text("تاريخ الاستلام:").FontSize(9.5f).FontColor(Muted);
                r.AutoItem().ContentFromLeftToRight().Text($"{DateTime.UtcNow.Year} / ____ / ____").FontFamily(Mono).FontSize(8.5f).Bold().FontColor(Ink);
            });
        });
    }

    private static void Signatures(IContainer container, ExportLayout layout, CompanyProfileDto company)
    {
        container.Row(row =>
        {
            row.Spacing(10);
            foreach (var s in layout.Signatures ?? [])
            {
                row.RelativeItem().Element(c => RoundedBox(c, 5, "#D2D2D2", 0.9f, Panel)).MinHeight(84).Padding(8).Column(col =>
                {
                    col.Item().AlignCenter().Text(s.Title).FontSize(10.5f).ExtraBold().FontColor(Ink);
                    col.Item().PaddingTop(4).LineHorizontal(0.8f).LineColor(Rule);
                    col.Item().PaddingTop(8).AlignCenter().Text(string.IsNullOrWhiteSpace(s.Name) ? " " : s.Name).FontSize(14).Light().FontColor(Ink);
                    if (!string.IsNullOrWhiteSpace(s.Caption))
                    {
                        col.Item().PaddingTop(4).AlignCenter().Text(s.Caption).FontSize(7.5f).FontColor(Faint);
                    }
                });
            }

            if (layout.Stamp)
            {
                row.RelativeItem().Element(c => RoundedBox(c, 5, "#9E9E9E", 1, White, "5 3")).MinHeight(84).Padding(6).Column(col =>
                {
                    col.Item().AlignCenter().Width(54).Height(54).Layers(l =>
                    {
                        l.Layer().Svg(PdfArt.Stamp(Text));
                        l.PrimaryLayer().AlignCenter().AlignMiddle().Column(s =>
                        {
                            s.Item().AlignCenter().Text("معتمد").FontSize(7).ExtraBold().FontColor(Text);
                            s.Item().AlignCenter().Text("قسم المحاسبة").FontSize(5).FontColor(Muted);
                            s.Item().AlignCenter().Text("PAID").FontFamily(Mono).FontSize(5).Bold().FontColor(Text);
                        });
                    });
                    col.Item().PaddingTop(4).AlignCenter().Text("ختم المنشأة").FontSize(8).FontColor(Muted);
                });
            }
        });
    }

    // ------------------------------------------------------------------ footer

    private static void Footer(IContainer container, ExportDocument doc, CompanyProfileDto company)
    {
        var layout = doc.Layout;
        container.Column(col =>
        {
            if (layout is { PaymentDetails: true })
            {
                col.Item().PaddingTop(4).BorderTop(0.8f).BorderColor(Rule).PaddingTop(8).Row(row =>
                {
                    row.RelativeItem(1.35f).Element(c => PaymentDetails(c, company));
                    row.RelativeItem(1.3f).AlignCenter().AlignMiddle().Element(c => QrPanel(c, layout.Qr, company));
                    row.RelativeItem().AlignLeft().AlignMiddle().Element(c => ContactsColumn(c, company));
                });
                return;
            }

            col.Item().PaddingTop(8).LineHorizontal(layout is null ? 0.8f : 1.4f).LineColor(layout is null ? Rule : Ink);
            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().AlignMiddle().Element(c => ContactsInline(c, company));
                if (layout?.Qr is { } qr)
                {
                    row.AutoItem().AlignMiddle().Row(q =>
                    {
                        q.AutoItem().AlignMiddle().PaddingLeft(6).ContentFromLeftToRight().Column(t =>
                        {
                            foreach (var line in (qr.Caption ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                            {
                                t.Item().Text(line).FontFamily(Mono, Arabic).FontSize(6.5f).FontColor(Muted);
                            }
                        });
                        q.AutoItem().Element(c => RoundedBox(c, 3, "#D5D5D5", 0.7f, White)).Padding(2).Width(34).Height(34).Image(QrPng(qr.Url));
                    });
                }
                else
                {
                    row.AutoItem().AlignMiddle().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(7.5f).FontColor(Faint).FontFamily(Mono, Arabic));
                        t.CurrentPageNumber();
                        t.Span(" / ");
                        t.TotalPages();
                    });
                }
            });
        });
    }

    private static void PaymentDetails(IContainer container, CompanyProfileDto company)
    {
        container.Column(col =>
        {
            col.Item().Text("طريقة الدفع :").FontSize(12).ExtraBold().FontColor(Ink);
            void Line(string label, string? value, bool mono)
            {
                if (string.IsNullOrWhiteSpace(value)) { return; }
                col.Item().PaddingTop(3).Row(r =>
                {
                    r.ConstantItem(10).Text("•").FontSize(9.5f).FontColor(Ink);
                    // Label and value apart: a number beside Arabic in one paragraph can come out reordered (7890-456-123).
                    r.AutoItem().Text($"{label} :").FontSize(10).Bold().FontColor(Ink);
                    r.RelativeItem().PaddingRight(5).Text(value).FontFamily(mono ? [Mono, Arabic] : [Arabic, Mono]).FontSize(mono ? 8.5f : 10).Bold().FontColor(Ink);
                });
            }

            Line("اسم البنك", company.BankName, false);
            Line("رقم الحساب", company.BankAccount, true);
            Line("آيبان (IBAN)", company.Iban, true);
            if (string.IsNullOrWhiteSpace(company.BankName) && string.IsNullOrWhiteSpace(company.BankAccount) && string.IsNullOrWhiteSpace(company.Iban))
            {
                col.Item().PaddingTop(4).Text("نقداً أو حسب الاتفاق").FontSize(10).FontColor(Muted);
            }
        });
    }

    private static void QrPanel(IContainer container, ExportQr? qr, CompanyProfileDto company)
    {
        var whatsApp = WhatsAppLink(company.WhatsApp);
        if (qr is null && whatsApp is null)
        {
            return;
        }

        RoundedBox(container, 5, "#DCDCDC", 0.8f, Panel).PaddingVertical(8).PaddingHorizontal(10).Row(row =>
        {
            row.Spacing(12);
            void Code(string url, string icon, string label, string color)
            {
                row.AutoItem().Column(c =>
                {
                    c.Item().AlignCenter().Element(b => RoundedBox(b, 4, "#D5D5D5", 0.7f, White)).Padding(3).Width(42).Height(42).Image(QrPng(url));
                    c.Item().PaddingTop(4).AlignCenter().Row(r =>
                    {
                        r.AutoItem().AlignMiddle().PaddingLeft(3).Width(9).Height(9).Svg(PdfArt.Icon(icon, color));
                        r.AutoItem().Text(label).FontSize(8).Bold().FontColor(color);
                    });
                });
            }

            if (qr is not null) { Code(qr.Url, "download", "تحميل المستند", Ink); }
            if (qr is not null && whatsApp is not null) { row.AutoItem().PaddingVertical(6).LineVertical(0.8f).LineColor(Rule); }
            if (whatsApp is not null) { Code(whatsApp, "message", "راسلنا واتساب", Green); }
        });
    }

    private static void ContactsColumn(IContainer container, CompanyProfileDto company)
    {
        container.Column(col =>
        {
            foreach (var (icon, value, mono) in Contacts(company))
            {
                col.Item().PaddingVertical(2).Row(r =>
                {
                    r.AutoItem().AlignMiddle().Width(16).Height(16).Border(0.9f).BorderColor(Ink).Padding(3).Svg(PdfArt.Icon(icon, Ink));
                    r.AutoItem().AlignMiddle().PaddingRight(6).Text(value).FontFamily(mono ? new[] { Mono, Arabic } : new[] { Arabic, Mono }).FontSize(8.5f).Bold().FontColor(Ink);
                });
            }
        });
    }

    private static void ContactsInline(IContainer container, CompanyProfileDto company)
    {
        container.Row(row =>
        {
            row.Spacing(14);
            foreach (var (icon, value, mono) in Contacts(company))
            {
                row.AutoItem().Row(r =>
                {
                    r.AutoItem().AlignMiddle().Width(10).Height(10).Svg(PdfArt.Icon(icon, Ink));
                    r.AutoItem().AlignMiddle().PaddingRight(4).Text(value).FontFamily(mono ? new[] { Mono, Arabic } : new[] { Arabic, Mono }).FontSize(8).Bold().FontColor(Ink);
                });
            }
        });
    }

    private static IEnumerable<(string Icon, string Value, bool Mono)> Contacts(CompanyProfileDto company)
    {
        if (!string.IsNullOrWhiteSpace(company.Phone)) { yield return ("phone", company.Phone, true); }
        if (!string.IsNullOrWhiteSpace(company.Email)) { yield return ("mail", company.Email.ToUpperInvariant(), true); }
        var address = string.Join("، ", new[] { company.Address, company.City }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (address.Length > 0) { yield return ("pin", address, false); }
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>An amount such as "352.40 ل.س": the figures in the monospaced face, the currency after them, smaller.</summary>
    private static void AmountSpans(TextDescriptor t, string amount, float size, bool bold, string color = Ink, string? unitColor = null)
    {
        var trimmed = amount.Trim();
        var split = trimmed.IndexOf(' ');
        var figures = split < 0 ? trimmed : trimmed[..split];
        var unit = split < 0 ? string.Empty : trimmed[(split + 1)..];

        var number = t.Span(figures).FontFamily(Mono).FontSize(size).FontColor(color);
        if (bold) { number.Bold(); }
        if (unit.Length > 0)
        {
            t.Span($" {unit}").FontSize(size * 0.8f).FontColor(unitColor ?? Muted);
        }
    }

    /// <summary>A rounded (optionally dashed) frame drawn behind the content, sized to whatever the content needs.</summary>
    private static IContainer RoundedBox(IContainer container, float radius, string stroke, float strokeWidth, string? fill, string? dash = null)
    {
        IContainer content = container;
        container.Layers(layers =>
        {
            layers.Layer().Svg(size => PdfArt.Box(size, radius, stroke, strokeWidth, fill, dash));
            content = layers.PrimaryLayer();
        });
        return content;
    }

    private static string? WhatsAppLink(string? number)
    {
        var digits = new string((number ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length >= 6 ? $"https://wa.me/{digits}" : null;
    }

    private static byte[] QrPng(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(10, [20, 20, 20], [255, 255, 255], drawQuietZones: false);
    }
}
