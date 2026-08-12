using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.EntityFrameworkCore;
using MyDemonList.Web.Entities;
using MyDemonList.Web.Entities.Context;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;

namespace MyDemonList.Web.Auth;

public static class DiscordAuthExtensions
{
    public static IServiceCollection AddDiscordAuthentication(this IServiceCollection services, IConfiguration config)
    {
        string? clientId = FirstNonEmpty(config,
            "Authentication:Discord:ClientId",
            "AUTHENTICATION__DISCORD__CLIENTID",
            "DISCORD_CLIENT_ID");

        string? clientSecret = FirstNonEmpty(config,
            "Authentication:Discord:ClientSecret",
            "AUTHENTICATION__DISCORD__CLIENTSECRET",
            "DISCORD_CLIENT_SECRET");

        string callbackPath = FirstNonEmpty(config,
            "Authentication:Discord:CallbackPath",
            "AUTHENTICATION__DISCORD__CALLBACKPATH") ?? "/signin-discord";

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                "Discord OAuth non configuré. Renseigne au moins l’une des paires clés suivantes :\n" +
                " - Authentication:Discord:ClientId / Authentication:Discord:ClientSecret (appsettings)\n" +
                " - AUTHENTICATION__DISCORD__CLIENTID / AUTHENTICATION__DISCORD__CLIENTSECRET (.env)\n" +
                " - DISCORD_CLIENT_ID / DISCORD_CLIENT_SECRET (.env)"
            );
        }

        services
            .AddAuthentication(o =>
            {
                o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                o.DefaultChallengeScheme = "Discord";
            })
            .AddCookie(o =>
            {
                o.LoginPath = "/login/discord";
                o.LogoutPath = "/logout";
                o.ExpireTimeSpan = TimeSpan.FromDays(365);
                o.SlidingExpiration = true;
                o.Cookie.IsEssential = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.Cookie.HttpOnly = true;
            })
            .AddOAuth("Discord", o =>
            {
                o.ClientId = clientId!;
                o.ClientSecret = clientSecret!;
                o.CallbackPath = callbackPath;

                o.AuthorizationEndpoint = "https://discord.com/oauth2/authorize";
                o.TokenEndpoint = "https://discord.com/api/oauth2/token";
                o.UserInformationEndpoint = "https://discord.com/api/users/@me";

                o.Scope.Add("identify");
                o.SaveTokens = false;
                o.UsePkce = true;

                o.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
                o.ClaimActions.MapJsonKey(ClaimTypes.Name, "username");
                o.ClaimActions.MapCustomJson("discord:global_name", u => u.TryGetProperty("global_name", out JsonElement g) ? g.GetString() : null);
                o.ClaimActions.MapJsonKey("discord:avatar", "avatar");

                o.Events = new OAuthEvents
                {
                    OnRedirectToAuthorizationEndpoint = ctx =>
                    {
                        ctx.Response.Redirect(ctx.RedirectUri);
                        return Task.CompletedTask;
                    },
                    OnCreatingTicket = async ctx =>
                    {
                        using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, ctx.Options.UserInformationEndpoint);
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.AccessToken);
                        using HttpResponseMessage res = await ctx.Backchannel.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ctx.HttpContext.RequestAborted);
                        res.EnsureSuccessStatusCode();

                        using JsonDocument payload = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                        JsonElement user = payload.RootElement;

                        ctx.RunClaimActions(user);
                        ctx.Properties.IsPersistent = true;
                        ctx.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddYears(1);

                        string discordId = user.GetProperty("id").GetString()!;
                        string? discordUsername = user.TryGetProperty("username", out JsonElement un) ? un.GetString() : null;

                        string? displayName =
                            user.TryGetProperty("global_name", out JsonElement gl) && gl.ValueKind != JsonValueKind.Null
                                ? gl.GetString()
                                : discordUsername;
                        string? avatar = user.TryGetProperty("avatar", out JsonElement av) ? av.GetString() : null;

                        MyDemonListWebDbContext db = ctx.HttpContext.RequestServices.GetRequiredService<MyDemonListWebDbContext>();
                        DiscordAccount? account = await db.DiscordAccounts
                            .Include(a => a.Utilisateur)
                            .SingleOrDefaultAsync(a => a.DiscordId == discordId);

                        if (account is null)
                        {
                            string nomDisponible = await TrouverNomDisponibleAsync(db, displayName ?? "Utilisateur Discord");

                            account = new DiscordAccount
                            {
                                DiscordId = discordId,
                                DiscordUsername = discordUsername,
                                DiscordDisplayName = displayName,
                                AvatarHash = avatar,
                                Utilisateur = new Utilisateur
                                {
                                    Nom = nomDisponible
                                },
                            };
                            db.DiscordAccounts.Add(account);
                        }
                        else
                        {
                            account.DiscordUsername = discordUsername;
                            account.DiscordDisplayName = displayName;
                            account.AvatarHash = avatar;
                        }

                        await db.SaveChangesAsync();

                        ClaimsIdentity id = ctx.Identity!;
                        id.AddClaim(new Claim("discord:id", discordId));
                        if (!string.IsNullOrEmpty(avatar)) id.AddClaim(new Claim("discord:avatar", avatar));

                        if (!string.IsNullOrEmpty(discordUsername))
                            id.AddClaim(new Claim("discord:username", discordUsername));

                        if (!string.IsNullOrEmpty(displayName))
                            id.AddClaim(new Claim("discord:display_name", displayName));

                        if (!string.IsNullOrEmpty(displayName))
                            id.AddClaim(new Claim(ClaimTypes.Name, displayName));
                    },
                    OnRemoteFailure = ctx =>
                    {
                        ctx.HandleResponse();
                        ctx.Response.Redirect("/");
                        return Task.CompletedTask;
                    }
                };
            });

        return services;
    }

    private static async Task<string> TrouverNomDisponibleAsync(MyDemonListWebDbContext db, string nomSouhaite)
    {
        if (!await db.Utilisateurs.AnyAsync(u => u.Nom.ToLower() == nomSouhaite.ToLower()))
            return nomSouhaite;

        int suffixe = 2;
        string candidat;
        do
        {
            candidat = $"{nomSouhaite} ({suffixe})";
            suffixe++;
        } while (await db.Utilisateurs.AnyAsync(u => u.Nom.ToLower() == candidat.ToLower()));

        return candidat;
    }

    private static string? FirstNonEmpty(IConfiguration config, params string[] keys)
    {
        foreach (string k in keys)
        {
            string? v = config[k];
            if (!string.IsNullOrWhiteSpace(v)) return v;

            v = Environment.GetEnvironmentVariable(k);
            if (!string.IsNullOrWhiteSpace(v)) return v;
        }
        return null;
    }

    public static IEndpointRouteBuilder MapDiscordAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/login/discord", async (HttpContext http) =>
        {
            await http.ChallengeAsync("Discord", new AuthenticationProperties { RedirectUri = "/auth/close" });
        });

        app.MapGet("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            http.Response.Redirect("/");
        });

        app.MapGet("/auth/close", async ctx =>
        {
#if DEBUG
            const string origineApplication = "https://localhost:7015";
#else
            const string origineApplication = "https://mydemonlist.com";
#endif

            string html = $$"""
                <!doctype html>
                <html lang="fr">
                    <head>
                        <meta charset="utf-8">
                        <meta name="viewport" content="width=device-width, initial-scale=1">
                        <title>Connexion Discord</title>
                        <style>
                            body {
                                font-family: system-ui;
                                background: #0f172a;
                                color: white;
                                display: flex;
                                align-items: center;
                                justify-content: center;
                                height: 100vh;
                                margin: 0;
                            }
                            p {
                                opacity: 0.7;
                            }
                        </style>
                    </head>
                    <body data-auth-origin="{{origineApplication}}">
                        <p>Connexion réussie. Cette fenêtre va se fermer…</p>
                        <script src="/js/discord-auth-close.js"></script>
                    </body>
                </html>
                """;

            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(html);
        });

        return app;
    }
}
