using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using DrugiSet.Api.Data;
using Microsoft.AspNetCore.Identity;

namespace DrugiSet.Api.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/login", async (
            LoginRequest request,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            JwtTokenService jwtTokenService) =>
        {
            var user = await userManager.FindByEmailAsync(request.Email);
            if (user is null)
            {
                return Results.Problem(title: "Nieprawidłowy e-mail lub hasło.", statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: false);
            if (!result.Succeeded)
            {
                return Results.Problem(title: "Nieprawidłowy e-mail lub hasło.", statusCode: StatusCodes.Status401Unauthorized);
            }

            var roles = await userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault() ?? string.Empty;
            var (token, expiresAtUtc) = jwtTokenService.CreateToken(user.Id, user.Email!, role);

            return Results.Ok(new LoginResponse(token, expiresAtUtc, user.Email!, role));
        });

        app.MapGet("/api/auth/me", (ClaimsPrincipal principal) =>
        {
            var id = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var email = principal.FindFirstValue(JwtRegisteredClaimNames.Email);
            var role = principal.FindFirstValue(ClaimTypes.Role);

            return Results.Ok(new MeResponse(id!, email!, role!));
        }).RequireAuthorization();
    }
}

public record LoginRequest(string Email, string Password);

public record LoginResponse(string Token, DateTime ExpiresAtUtc, string Email, string Role);

public record MeResponse(string Id, string Email, string Role);
