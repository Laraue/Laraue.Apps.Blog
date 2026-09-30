---
title: Laraue.Telegram.NET — Write Telegram Bots Like ASP.NET Controllers in C#
type: project
tags: [telegram, dotnet, csharp, telegram-bot, aspnet-core, mvc, middleware, authentication, localization, bot-development, webhook, long-polling]
description: Stop writing if-else chains for Telegram bot routing. Laraue.Telegram.NET brings ASP.NET-style controllers, middleware, authentication, and localization to .NET 9 Telegram bot development.
createdAt: 2025-11-01
updatedAt: 2026-09-30
---
If you've built ASP.NET Core APIs before, writing a Telegram bot shouldn't feel like starting over. **Laraue.Telegram.NET** brings the controller/middleware pattern you already know to Telegram bot development — routing, dependency injection, authentication, roles, and localization, all wired together cleanly.

[![NuGet](https://img.shields.io/nuget/v/Laraue.Telegram.NET.Core)](https://www.nuget.org/packages/Laraue.Telegram.NET.Core)
[![Downloads](https://img.shields.io/nuget/dt/Laraue.Telegram.NET.Core)](https://www.nuget.org/packages/Laraue.Telegram.NET.Core)
[![MIT License](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Laraue/Laraue.Telegram.NET)

---

## The Problem With Typical Telegram Bot Code

Most Telegram bots start clean and degrade fast. The Bot API gives you an update object and leaves routing entirely up to you, which typically results in something like this:

```csharp
if (update.Message?.Text == "/start") Start();
else if (update.Message?.Text == "/settings") OpenSettings();
else if (update.CallbackQuery?.Data.StartsWith("/change")) ChangeSettings();
// ...grows forever
```

This is hard to navigate, impossible to test cleanly, and a nightmare to maintain as commands multiply.

---

## The Solution: Controllers and Attributes

Laraue.Telegram.NET lets you declare routes using attributes on controller methods — the same mental model as ASP.NET Core MVC:

```csharp
public class MenuController : TelegramController
{
    private readonly IMenuService _service;

    public MenuController(IMenuService service) => _service = service;

    [TelegramMessageRoute("/start")]
    public Task ShowMenuAsync(TelegramRequestContext ctx)
        => _service.HandleStartAsync(ctx.Update.Message!);

    [TelegramCallbackRoute("/open-settings")]
    public Task OpenSettingsAsync(TelegramRequestContext ctx)
        => _service.OpenSettingsAsync(ctx.Update.CallbackQuery!);
}
```

Services are resolved from Microsoft's standard DI container. All commands are visible at a glance.

---

## Getting Started

**Install the core package:**
```bash
dotnet add package Laraue.Telegram.NET.Core
```

**Webhooks (production):**
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTelegramCore(new TelegramBotClientOptions("YOUR_BOT_TOKEN"));
var app = builder.Build();
app.MapTelegramRequests("your-secret-webhook-path");
app.Run();
```

**Long polling (local development):**
```csharp
builder.Services.AddTelegramCore(new TelegramBotClientOptions("YOUR_BOT_TOKEN"));
builder.Services.AddTelegramLongPoolingService();
```

No infrastructure needed — just swap the registration and go.

---

## Authentication & Authorization

Enable user authentication by wiring up a query service that handles user lookup:

```csharp
services.AddTelegramCore()
    .AddTelegramAuthentication<User, Guid, TelegramUserQueryService, RequestContext>();
```

Protect endpoints with role-based authorization:

```csharp
public class AdminController : TelegramController
{
    [RequiresUserRole(Roles.Admin)]
    [TelegramMessageRoute("/stats")]
    public Task SendStatsAsync(RequestContext ctx, CancellationToken ct)
    {
        // Only admins reach here
    }
}
```

Roles are resolved via your own `IUserRoleProvider` implementation, or via the built-in `StaticUserRoleProvider` loaded from app configuration.

---

## Middleware

Intercept requests before they reach your controllers, just like `IMiddleware` in ASP.NET:

```csharp
public class LogExceptionsMiddleware : ITelegramMiddleware
{
    private readonly ITelegramMiddleware _next;

    public LogExceptionsMiddleware(ITelegramMiddleware next) => _next = next;

    public async Task<object?> InvokeAsync(CancellationToken ct = default)
    {
        try { return await _next.InvokeAsync(ct); }
        catch (Exception ex) { /* log */ }
        return null;
    }
}
```

Register with:
```csharp
services.AddTelegramMiddleware<LogExceptionsMiddleware>();
```

---

## Localization

Support multiple languages by implementing `BaseCultureInfoProvider` to detect the user's preferred language, then use standard `.resx` resource files:

```
Resources/Buttons.resx      ← English
Resources/Buttons.fr.resx   ← French
```

Access strings via `Resources.Buttons.Menu` — the correct translation is resolved automatically per user.

---

## Packages

| Package | Purpose |
|---|---|
| `Laraue.Telegram.NET.Core` | Routing, controllers, DI |
| `Laraue.Telegram.NET.Authentication` | User auth + role-based access |
| `Laraue.Telegram.NET.Localization` | Per-user language resolution |

Install only what you need — the packages are independent.

---

## Real-World Usage

This library powers two production projects: a language learning app and a real estate monitoring bot, both using Telegram as their primary user interface.

**Source:** [github.com/Laraue/Laraue.Telegram.NET](https://github.com/Laraue/Laraue.Telegram.NET)
