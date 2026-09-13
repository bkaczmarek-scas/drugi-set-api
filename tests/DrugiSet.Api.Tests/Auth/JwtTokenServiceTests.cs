using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using DrugiSet.Api.Auth;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DrugiSet.Api.Tests.Auth;

public class JwtTokenServiceTests
{
    private static JwtTokenService CreateService(string? secret = "test-secret-at-least-32-characters-long")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = secret,
            })
            .Build();

        return new JwtTokenService(configuration);
    }

    [Fact]
    public void CreateToken_EncodesUserIdEmailAndRoleAsClaims()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();

        var (token, _) = service.CreateToken(userId, "admin@drugiset.pl", "Admin");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(userId.ToString(), jwt.Subject);
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Email && c.Value == "admin@drugiset.pl");
        Assert.Contains(jwt.Claims, c => c.Type == ClaimTypes.Role && c.Value == "Admin");
    }

    [Fact]
    public void CreateToken_SetsExpiryToApproximatelyEightHoursFromNow()
    {
        var service = CreateService();

        var (_, expiresAtUtc) = service.CreateToken(Guid.NewGuid(), "admin@drugiset.pl", "Admin");

        var expectedExpiry = DateTime.UtcNow.AddHours(8);
        Assert.True(Math.Abs((expiresAtUtc - expectedExpiry).TotalMinutes) < 1);
    }

    [Fact]
    public void Constructor_ThrowsWhenSecretIsMissing()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Throws<InvalidOperationException>(() => new JwtTokenService(configuration));
    }
}
