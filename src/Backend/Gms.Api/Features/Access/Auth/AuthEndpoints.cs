using Gms.Api.Security;
using Gms.Shared.Access;

namespace Gms.Api.Features.Access.Auth;

public static class AuthEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/login", async (LoginRequest request, LoginHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(request, ct))).AllowAnonymous();

        group.MapPost("/logout", async (LogoutHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(ct);
            return Results.NoContent();
        }).RequireLogin();

        group.MapGet("/me", (MeHandler handler) => Results.Ok(handler.Handle())).RequireLogin();

        group.MapPost("/change-password", async (ChangePasswordRequest request, ChangePasswordHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(request, ct);
            return Results.NoContent();
        }).RequireLogin();
    }
}
