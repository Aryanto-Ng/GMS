using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Gms.Api.Common;
using Gms.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Gms.Api.Security;

/// <summary>Pembuatan dan hashing token sesi opaque. Hanya hash SHA-256 yang disimpan di database (ADR-002).</summary>
public static class SessionTokens
{
    /// <summary>Token acak 256 bit, dikodekan base64url.</summary>
    public static string Generate() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Nama klaim yang dikeluarkan handler autentikasi sesi.</summary>
public static class GmsClaims
{
    public const string Permission = "perm";
    public const string FullName = "full_name";
    public const string RoleId = "role_id";
    public const string RoleName = "role_name";
    public const string IsAdministrator = "is_admin";
    public const string MustChangePassword = "must_change_password";
    public const string SessionId = "session_id";
}

/// <summary>Akses ringkas ke pengguna saat ini (dari klaim yang dibangun handler autentikasi).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? Id { get; }
    string? UserName { get; }
    Guid? SessionId { get; }

    /// <summary>True bila pengguna ber-role sistem (Administrator).</summary>
    bool IsAdministrator { get; }

    bool HasPermission(string permission);
    IReadOnlyList<string> Permissions { get; }
}

internal sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? Id => Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? UserName => Principal?.Identity?.Name;

    public Guid? SessionId => Guid.TryParse(Principal?.FindFirstValue(GmsClaims.SessionId), out var id) ? id : null;

    public bool IsAdministrator => Principal?.FindFirstValue(GmsClaims.IsAdministrator) == "true";

    public bool HasPermission(string permission) => Principal?.HasClaim(GmsClaims.Permission, permission) == true;

    public IReadOnlyList<string> Permissions =>
        Principal?.FindAll(GmsClaims.Permission).Select(c => c.Value).ToList() ?? [];
}

/// <summary>
/// Aturan password FSD (AC-AM-07): minimal 8 karakter, mengandung huruf dan angka.
/// Panjang dan angka dicek oleh opsi Identity; validator ini menambahkan syarat huruf.
/// </summary>
public sealed class GmsPasswordValidator : IPasswordValidator<AppUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user, string? password)
    {
        if (!string.IsNullOrEmpty(password) && !password.Any(char.IsLetter))
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordRequiresLetter",
                Description = "Password harus mengandung huruf.",
            }));
        }

        return Task.FromResult(IdentityResult.Success);
    }
}

/// <summary>Menerjemahkan kode galat Identity menjadi pesan Bahasa Indonesia untuk field yang tepat.</summary>
public static class IdentityErrorMapper
{
    public static ProblemException ToProblem(IEnumerable<IdentityError> errors, string passwordField = "password", string userNameField = "userName")
    {
        var bag = new ErrorBag();
        foreach (var error in errors)
        {
            switch (error.Code)
            {
                case "DuplicateUserName":
                    return ProblemException.Duplicate(userNameField, "Username sudah digunakan.");
                case "InvalidUserName":
                    bag.Add(userNameField, "Username hanya boleh berisi huruf, angka, titik, strip, atau garis bawah.");
                    break;
                case "PasswordTooShort":
                    bag.Add(passwordField, "Password minimal 8 karakter.");
                    break;
                case "PasswordRequiresDigit":
                    bag.Add(passwordField, "Password harus mengandung angka.");
                    break;
                case "PasswordRequiresLetter":
                    bag.Add(passwordField, "Password harus mengandung huruf.");
                    break;
                case "PasswordMismatch":
                    bag.Add("currentPassword", "Password lama salah.");
                    break;
                default:
                    bag.Add(passwordField, error.Description);
                    break;
            }
        }

        return ProblemException.Validation(bag);
    }
}

/// <summary>Pembuat password sementara acak yang memenuhi aturan (huruf + angka), tanpa karakter ambigu.</summary>
public static class TemporaryPassword
{
    private const string Letters = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz";
    private const string Digits = "23456789";

    public static string Generate(int length = 12)
    {
        var chars = new char[length];
        chars[0] = Letters[RandomNumberGenerator.GetInt32(Letters.Length)];
        chars[1] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        var all = Letters + Digits;
        for (var i = 2; i < length; i++)
        {
            chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
        }

        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }
}
