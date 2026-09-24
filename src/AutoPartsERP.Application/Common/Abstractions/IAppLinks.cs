namespace AutoPartsERP.Application.Common.Abstractions;

/// <summary>Absolute links into the web application (printed as QR codes on documents).</summary>
public interface IAppLinks
{
    /// <summary>The address of <paramref name="path"/> in the application, e.g. "https://erp.example.com/invoices/…".</summary>
    string To(string path);
}
