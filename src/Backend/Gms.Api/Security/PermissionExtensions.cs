using Gms.Api.Common;

namespace Gms.Api.Security;

public static class PermissionExtensions
{
    /// <summary>
    /// Mewajibkan login dan permission tertentu (penegakan di server bersifat final, FSD AC-AM-21).
    /// Bila pengguna tidak berhak: 403 <c>forbidden</c>.
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireAuthorization();
        builder.AddEndpointFilter(async (context, next) =>
        {
            var user = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
            if (!user.HasPermission(permission))
            {
                throw ProblemException.Forbidden();
            }

            return await next(context);
        });
        return builder;
    }

    /// <summary>Wajib login saja (tanpa permission khusus).</summary>
    public static TBuilder RequireLogin<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireAuthorization();
        return builder;
    }
}

/// <summary>
/// Pengguna yang wajib mengganti password hanya boleh memanggil /api/v1/auth/* (AC-AM-08).
/// Dijalankan setelah autentikasi dan sebelum endpoint.
/// </summary>
public sealed class MustChangePasswordMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true
            && user.HasClaim(GmsClaims.MustChangePassword, "true")
            && context.Request.Path.StartsWithSegments("/api/v1")
            && !context.Request.Path.StartsWithSegments("/api/v1/auth"))
        {
            await ProblemWriter.WriteAsync(context, ProblemWriter.From(ProblemException.MustChangePassword()));
            return;
        }

        await next(context);
    }
}
