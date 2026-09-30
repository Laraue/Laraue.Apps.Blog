---
title: Telegram Login Widget vs Mini App auth in .NET — two validation schemes, one JWT
description: Part 13 of building a Telegram task tracker solo. Users asked for a web version outside Telegram, so it needed its own login. Adding the Telegram Login Widget meant a second validation scheme alongside the Mini App's — and both end at the same JWT, because the app barely depends on Telegram past login.
type: article
createdAt: 2026-07-01
updatedAt: 2026-09-30
projects: [boards]
tags: [dotnet, aspnet-core, telegram, authentication, telegram-login-widget, jwt, devlog]
previousLink: telegram-media-group-album-bot
---

> **Architecture First: Building a Jira Alternative Solo, AI-Assisted** — Part 13.
> The [previous article](telegram-media-group-album-bot) finished the last piece of functionality planned for the bot's MVP. Here we move to the web version users asked for — and to ship it, the app first needs authentication that works outside Telegram Mini App.

Until now the only way to log in was launching the Telegram Mini App, which authenticates the client through data Telegram provides. But feedback came in that the Mini App was not always convenient to work with — users wanted a browser tab with their boards close at hand. A web version opened in a browser, outside Telegram, cannot rely on Telegram's init data for authentication the way the Mini App does. Setting up authentication for the web version through Telegram is the subject of this article. It is also where, working on that authentication, we finally understood that Telegram in our architecture is not core functionality but rather one of the possible integrations.

## Telegram as one authentication provider

The Mini App authenticates through Telegram's init data, validated on the backend, which then issues the app's own JWT — the sequence described in [the auth article](telegram-mini-app-authentication-dotnet). The token is the app's own bearer, holding an internal user identifier, not the user's Telegram ID. Every request after login carries this JWT in its headers. So for an authenticated session, Telegram is needed exactly once — when the bearer token is issued.

That is what makes adding a new authentication provider simple. It just has to validate the user's identity somehow and issue the same JWT — and the rest of the app keeps working exactly as it did in the Mini App version.

## How web-version auth differs from Telegram Mini App auth

A browser opened outside Telegram has no init data object, which the Mini App has. The standard way to authenticate through Telegram on a web page is the [Telegram Login Widget](https://core.telegram.org/widgets/login) — a script that adds a "Log in with Telegram" button and returns a user object once the user authorises. The [source](https://github.com/Laraue/laraue-boards/blob/master/app/pages/index.vue) shows how such a callback is handled:

```ts
(window as any).onTelegramAuth = async (user: any) => {
    const { authViaWebApp } = useTelegramUserApi()
    const bearer = await authViaWebApp(user)
    await initUserWithBearer(bearer)
};
```

When the user authorises through Telegram's standard authorization window, the widget calls `onTelegramAuth` with the signed data, the frontend posts it to the backend, and gets back a bearer token — after which [`initUserWithBearer`](https://github.com/Laraue/laraue-boards/blob/master/app/composables/auth.ts) puts the app in exactly the state a Mini App login would.

On the backend, widget authorization gets its own endpoint in [`TelegramAuthController`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.WebApiHost/Controllers/TelegramAuthController.cs):

```csharp
[HttpPost("auth")]
public Task<string> Authenticate(
    TelegramWidgetAuthRequest request,
    CancellationToken cancellationToken)
{
    return authService.Authenticate(request, cancellationToken);
}
```

The reason this is not the same method used before is that widget data is validated differently from the Mini App's init data. On top of that, init data is a URL-encoded string containing the user object, while the widget sends a plain object to the backend — so the contracts of the two auth methods differ too.

The job of the auth method is to confirm the data came from Telegram, but the signature-checking scheme differs here. This is the web version's variant — [`ValidateWidgetData`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.WebApiHost/TelegramAuthService.cs):

```csharp
private MiniAppUser ValidateWidgetData(TelegramWidgetAuthRequest request)
{
    // Reject stale auth — replay attack protection
    var authAge = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(request.AuthDate);
    if (authAge > TimeSpan.FromHours(24))
        throw new ForbiddenException("Auth is expired");

    // Build data-check-string: only fields that are actually present,
    // sorted alphabetically, joined with \n, hash excluded
    var fields = new SortedDictionary<string, string>
    {
        ["auth_date"] = request.AuthDate.ToString(),
        ["first_name"] = request.FirstName,
        ["id"] = request.Id.ToString(),
    };

    if (request.LastName is not null)
        fields["last_name"] = request.LastName;
    if (request.Username is not null)
        fields["username"]  = request.Username;
    if (request.PhotoUrl is not null)
        fields["photo_url"] = request.PhotoUrl;

    var dataCheckString = string.Join("\n",
        fields.Select(kv => $"{kv.Key}={kv.Value}"));

    // Secret key = SHA256(botToken) — plain hash, not HMAC
    var secretKey = SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.Token));

    // Signature = HMAC-SHA256(dataCheckString, secretKey)
    var computedHash = Convert.ToHexString(
        HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString))).ToLower();

    if (computedHash != request.Hash)
        throw new ForbiddenException("Authorization Failed");

    return new MiniAppUser
    {
        FirstName = request.FirstName,
        LastName = request.LastName,
        Id = request.Id,
        Username = request.Username,
        LanguageCode = null,
    };
}
```

The key difference is in the signing. The widget uses `SHA256(botToken)` as the secret, then `HMAC-SHA256(dataCheckString, secretKey)`; the Mini App's init data uses a different secret — `HMAC(botToken)` keyed with the constant `WebAppData`. Because the authorizations differ, we chose to split the methods. We wanted to avoid the classic development situation: fix one thing, break another. Changes to one authorization will not affect the other.

The rest is similar between the two: build a data-check-string from the present fields, sorted alphabetically and joined with `\n` (hash excluded), and return `403` if `auth_date` is older than 24 hours. The exact and current rules for both schemes are in [Telegram's docs](https://core.telegram.org/widgets/login#checking-authorization).

Because `ValidateWidgetData` returns a `MiniAppUser` object, everything from there works exactly as it does in the Mini App. The object is used to issue a JWT, which is returned to the frontend. The frontend adds an `Authorization: Bearer {key}` header to every backend call, and the request is treated as authenticated.

## After login the app does not depend on Telegram

Since the [app](https://boards.laraue.com) was already a public web address — just hidden from outside eyes, opening only from the Telegram Mini App — all that was left was to share it with users.

As a bonus, we suddenly realised the app can run in a browser without Telegram at all, once past login. The resulting architecture — which separates the internal `user_id` from `telegram_user_id`, and system `issues` from `telegram_messages` — would let us add Google login in the future, or create new issues from a Slack message, or build some other integration, without any large refactoring.

## Conclusions

The web version now has authentication and can be opened in a browser — exactly what users asked for. Adding it touched only one endpoint and one method — `Authenticate` and `ValidateWidgetData` on the backend, plus the login page on the frontend.

## What comes next

As the developers and at the same time some of the most active users of Laraue Boards, we ran into an inconvenience — epics alone were not enough to separate issues. Epics tied to personal life ended up next to project activities, and we wanted to keep them apart. The solution was to add spaces — groups of epics belonging to one project. That way you could have a separate space for personal activities and a separate one for project work.