namespace AutoPartsERP.Application.Features.Assistant;

/// <summary>
/// Keyword fallback for when the language model is not configured or not reachable: the common questions still work
/// ("رصيد محمد", "مخزون فلتر زيت", "فاتورة 41", "مبيعات اليوم", "الفواتير المتأخرة"). Anything else is Help.
/// </summary>
public static class RuleBasedIntents
{
    private static readonly Regex Invoice = new(@"(?:فاتور[ةه]|invoice|inv)\s*(?:رقم)?\s*[:#]?\s*((?:inv-?)?[0-9٠-٩][0-9٠-٩\-]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Balance = new(@"^(?:كم\s+)?(?:رصيد|حساب|ذمة|ذمه|مديونية|مديونيه|ديون|دين)\s+(?:الزبون\s+|العميل\s+|زبون\s+)?(.+?)\s*[؟?]?$", RegexOptions.Compiled);
    private static readonly Regex Stock = new(@"^(?:كم\s+)?(?:مخزون|كمية|كميه|الكمية|الكميه|يوجد|متوفر|عندنا|موجود)\s+(?:من\s+)?(.+?)\s*[؟?]?$", RegexOptions.Compiled);
    private static readonly Regex Overdue = new(@"(?:متأخر|متاخر|مستحق|متأخرة|متاخره|overdue)", RegexOptions.Compiled);
    private static readonly Regex OverdueFor = new(@"(?:متأخر|متاخر|مستحق)[ةه]?\s+(.+?)\s*[؟?]?$", RegexOptions.Compiled);
    private static readonly Regex Preposition = new(@"^(?:على|عند|من|الزبون|العميل|للزبون|للعميل)\s+", RegexOptions.Compiled);

    public static AssistantIntent Parse(string text)
    {
        var t = text.Trim();
        var args = new Dictionary<string, string>(StringComparer.Ordinal);

        if (Invoice.Match(t) is { Success: true } inv)
        {
            args["invoice_number"] = inv.Groups[1].Value;
            return new AssistantIntent(AssistantActions.InvoiceStatus, args);
        }

        if (t.Contains("مبيعات", StringComparison.Ordinal) || t.Contains("المبيعات", StringComparison.Ordinal))
        {
            args["period"] = t.Contains("امس", StringComparison.Ordinal) || t.Contains("أمس", StringComparison.Ordinal) ? "yesterday"
                : t.Contains("الشهر الماضي", StringComparison.Ordinal) ? "last_month"
                : t.Contains("الشهر", StringComparison.Ordinal) ? "this_month"
                : t.Contains("الأسبوع", StringComparison.Ordinal) || t.Contains("الاسبوع", StringComparison.Ordinal) ? "this_week"
                : "today";
            return new AssistantIntent(AssistantActions.SalesSummary, args);
        }

        if (Overdue.IsMatch(t))
        {
            if (OverdueFor.Match(t) is { Success: true } who && CustomerAfterOverdue(who.Groups[1].Value) is { } name)
            {
                args["customer_name"] = name;
            }

            return new AssistantIntent(AssistantActions.OverdueInvoices, args);
        }

        if (Balance.Match(t) is { Success: true } bal)
        {
            args["customer_name"] = bal.Groups[1].Value;
            return new AssistantIntent(AssistantActions.CustomerBalance, args);
        }

        if (Stock.Match(t) is { Success: true } st)
        {
            args["item"] = st.Groups[1].Value;
            return new AssistantIntent(AssistantActions.ItemStock, args);
        }

        return new AssistantIntent(AssistantActions.Help, args);
    }

    /// <summary>"لشركة الأفق" → "شركة الأفق", "للزبون محمد" → "محمد", "على محمد" → "محمد"; words like "اليوم" are not names.</summary>
    private static string? CustomerAfterOverdue(string rest)
    {
        var name = Preposition.Replace(rest.Trim(), string.Empty);
        if (name.StartsWith("لل", StringComparison.Ordinal))
        {
            name = "ال" + name[2..];
        }
        else if (name.StartsWith('ل') && name.Length > 3)
        {
            name = name[1..];
        }

        return name.Length < 2 || name is "اليوم" or "حاليا" or "حالياً" or "الان" or "الآن" ? null : name;
    }
}
