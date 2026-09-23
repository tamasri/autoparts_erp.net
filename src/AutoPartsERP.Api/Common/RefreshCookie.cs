namespace AutoPartsERP.Api.Common;

/// <summary>
/// The refresh token lives only in this cookie: HttpOnly (page script cannot read it), SameSite=Strict (never sent by another site),
/// Secure outside Development, and scoped to the auth endpoints so no other request carries it. The access token stays in the
/// page's memory. Refresh and logout also require the <see cref="CsrfHeader"/> header, which a cross-site form cannot set.
/// </summary>
public static class RefreshCookie
{
    public const string Name = "erp_rt";
    public const string CsrfHeader = "X-Requested-With";
    private const string CookiePath = "/api/v1/auth";

    public static string? Read(HttpContext context) =>
        context.Request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    public static void Write(HttpContext context, string token, DateTimeOffset expiresAtUtc) =>
        context.Response.Cookies.Append(Name, token, Options(context, expiresAtUtc));

    public static void Clear(HttpContext context) =>
        context.Response.Cookies.Delete(Name, Options(context, null));

    public static bool HasCsrfHeader(HttpContext context) => context.Request.Headers.ContainsKey(CsrfHeader);

    private static CookieOptions Options(HttpContext context, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        // Behind nginx the API itself is reached over plain HTTP, so Secure cannot follow Request.IsHttps.
        Secure = !context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment(),
        SameSite = SameSiteMode.Strict,
        Path = CookiePath,
        Expires = expires,
        IsEssential = true,
    };
}
