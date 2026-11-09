namespace Gms.Web.Infrastructure.Auth;

/// <summary>
/// Pengguna yang wajib mengganti password hanya boleh membuka halaman ganti password, logout, dan aset (FSD Access Management AC-AM-08).
/// Penegakan sebenarnya tetap di API (403 must-change-password); ini hanya mengarahkan agar pengguna tidak melihat halaman yang gagal.
/// </summary>
public sealed class MustChangePasswordMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true && user.MustChangePasswordNow()
            && !path.StartsWithSegments("/account") && !path.StartsWithSegments("/_framework") && !path.StartsWithSegments("/_content")
            && !path.StartsWithSegments("/_blazor") && !Path.HasExtension(path.Value))
        {
            context.Response.Redirect($"{AccountEndpoints.ChangePasswordPath}?returnUrl={Uri.EscapeDataString(path + context.Request.QueryString)}");
            return;
        }

        await next(context);
    }
}
