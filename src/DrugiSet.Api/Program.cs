using System.Text;
using DrugiSet.Api.Auth;
using DrugiSet.Api.Data;
using DrugiSet.Api.Posts;
using DrugiSet.Api.Seed;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    // TODO: przywrócić domyślną politykę haseł (wielka litera/cyfra/znak specjalny)
    // przed wdrożeniem prawdziwej rejestracji — patrz docs/superpowers/specs/2026-09-13-api-auth-scaffold-design.md
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 4;
})
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();

var jwtSecret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret) || Encoding.UTF8.GetByteCount(jwtSecret) < 32)
{
    throw new InvalidOperationException("Konfiguracja 'Jwt:Secret' jest wymagana i musi mieć co najmniej 32 bajty.");
}

builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<PostsService>();
builder.Services.AddSingleton<ImageUploadService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Bez tego ASP.NET Core mapuje standardowe krótkie nazwy claimów JWT ("sub", "email")
        // na przestarzałe długie URI (ClaimTypes.NameIdentifier / ClaimTypes.Email), przez co
        // AuthEndpoints.MapAuthEndpoints (który czyta JwtRegisteredClaimNames.Sub/.Email) dostaje null.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "drugi-set-api",
            ValidateAudience = true,
            ValidAudience = "drugi-set-web",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();

var corsOrigins = builder.Configuration["Cors:AllowedOrigins"]
    ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

var uploadsPath = builder.Configuration["Uploads:Path"];
if (string.IsNullOrWhiteSpace(uploadsPath))
{
    throw new InvalidOperationException("Konfiguracja 'Uploads:Path' jest wymagana.");
}
uploadsPath = Path.GetFullPath(uploadsPath);
Directory.CreateDirectory(uploadsPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads",
});

app.UseExceptionHandler();

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapAuthEndpoints();
app.MapPostsEndpoints();

using (var scope = app.Services.CreateScope())
{
    await TestAccountSeeder.SeedAsync(scope.ServiceProvider);
}

app.Run();
