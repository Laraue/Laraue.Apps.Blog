---
title: Laraue.Telegram.NET — ASP.NET-подобные контроллеры для Telegram-ботов на C#
type: project
tags: [telegram, dotnet, csharp, telegram-bot, aspnet-core, mvc, middleware, authentication, localization, bot-development, webhook, long-polling]
description: Устали от цепочек if-else в Telegram-боте? Laraue.Telegram.NET привносит контроллеры, middleware, аутентификацию и локализацию в стиле ASP.NET Core в разработку ботов на .NET.
createdAt: 2025-03-04
updatedAt: 2026-09-30
---
Если вы уже разрабатывали ASP.NET Core API, написание Telegram-бота не должно ощущаться как нечто новое.
**Laraue.Telegram.NET** переносит знакомый паттерн контроллеров и middleware в разработку Telegram-ботов —
маршрутизация, dependency injection, аутентификация, роли и локализация, всё собрано в одном месте.

[![NuGet](https://img.shields.io/nuget/v/Laraue.Telegram.NET.Core)](https://www.nuget.org/packages/Laraue.Telegram.NET.Core)
[![Downloads](https://img.shields.io/nuget/dt/Laraue.Telegram.NET.Core)](https://www.nuget.org/packages/Laraue.Telegram.NET.Core)
[![MIT License](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Laraue/Laraue.Telegram.NET)

---

## Проблема типичного кода Telegram-бота

Большинство Telegram-ботов начинаются чисто, но быстро превращаются в нечто трудноподдерживаемое.
Bot API отдаёт вам объект обновления и оставляет маршрутизацию полностью на ваше усмотрение,
что обычно приводит к чему-то вроде этого:

```csharp
if (update.Message?.Text == "/start") Start();
else if (update.Message?.Text == "/settings") OpenSettings();
else if (update.CallbackQuery?.Data.StartsWith("/change")) ChangeSettings();
// ...и так до бесконечности
```

Такой код сложно читать, невозможно нормально тестировать и мучительно поддерживать по мере роста числа команд.

---

## Решение: контроллеры и атрибуты

Laraue.Telegram.NET позволяет объявлять маршруты через атрибуты на методах контроллеров —
та же ментальная модель, что и в ASP.NET Core MVC:

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

Сервисы разрешаются из стандартного DI-контейнера Microsoft. Все команды видны с первого взгляда.

---

## Быстрый старт

**Установите core-пакет:**
```bash
dotnet add package Laraue.Telegram.NET.Core
```

**Webhooks (продакшн):**
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTelegramCore(new TelegramBotClientOptions("ВАШ_ТОКЕН"));
var app = builder.Build();
app.MapTelegramRequests("ваш-секретный-путь-вебхука");
app.Run();
```

**Long Polling (локальная разработка):**
```csharp
builder.Services.AddTelegramCore(new TelegramBotClientOptions("ВАШ_ТОКЕН"));
builder.Services.AddTelegramLongPoolingService();
```

Никакой дополнительной инфраструктуры — просто поменяйте регистрацию и готово.

---

## Аутентификация и авторизация

Включите аутентификацию пользователей, подключив сервис для поиска и сохранения пользователей:

```csharp
services.AddTelegramCore()
    .AddTelegramAuthentication<User, Guid, TelegramUserQueryService, RequestContext>();
```

Защитите endpoints с помощью ролевой авторизации:

```csharp
public class AdminController : TelegramController
{
    [RequiresUserRole(Roles.Admin)]
    [TelegramMessageRoute("/stats")]
    public Task SendStatsAsync(RequestContext ctx, CancellationToken ct)
    {
        // Сюда попадут только администраторы
    }
}
```

Роли определяются через собственную реализацию `IUserRoleProvider` или через встроенный
`StaticUserRoleProvider`, загружающий роли из конфигурации приложения.

---

## Middleware

Перехватывайте запросы до того, как они достигнут контроллеров — точно так же, как `IMiddleware` в ASP.NET:

```csharp
public class LogExceptionsMiddleware : ITelegramMiddleware
{
    private readonly ITelegramMiddleware _next;

    public LogExceptionsMiddleware(ITelegramMiddleware next) => _next = next;

    public async Task<object?> InvokeAsync(CancellationToken ct = default)
    {
        try { return await _next.InvokeAsync(ct); }
        catch (Exception ex) { /* логирование */ }
        return null;
    }
}
```

Регистрация:
```csharp
services.AddTelegramMiddleware<LogExceptionsMiddleware>();
```

---

## Локализация

Поддерживайте несколько языков, реализовав `BaseCultureInfoProvider` для определения предпочтительного
языка пользователя, и используйте стандартные `.resx`-файлы ресурсов:

```
Resources/Buttons.resx       ← Английский
Resources/Buttons.fr.resx    ← Французский
```

Обращайтесь к строкам через `Resources.Buttons.Menu` — нужный перевод подбирается автоматически для каждого пользователя.

---

## Интеграционное тестирование

Отдельный пакет позволяет писать интеграционные тесты для кода, созданного с использованием библиотеки,
без необходимости поднимать реальное Telegram-соединение.

---

## Пакеты

| Пакет | Назначение |
|---|---|
| `Laraue.Telegram.NET.Core` | Маршрутизация, контроллеры, DI |
| `Laraue.Telegram.NET.Authentication` | Аутентификация пользователей и ролевой доступ |
| `Laraue.Telegram.NET.Localization` | Определение языка на уровне пользователя |

Устанавливайте только то, что нужно — пакеты независимы друг от друга.

---

## Использование в реальных проектах

Библиотека используется в двух продакшн-проектах: приложении для изучения иностранных языков и боте
мониторинга недвижимости — в обоих Telegram является основным интерфейсом взаимодействия с пользователем.

**Исходный код:** [github.com/Laraue/Laraue.Telegram.NET](https://github.com/Laraue/Laraue.Telegram.NET)
