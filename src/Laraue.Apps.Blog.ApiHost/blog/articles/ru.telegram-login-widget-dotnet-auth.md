---
title: Telegram Login Widget vs авторизация через Mini App в .NET — две схемы валидации, один JWT
description: Часть 13 цикла о разработке Telegram-таск-трекера в одиночку. Пользователи просили добавить веб-версию вне Telegram Mini App, но для этого нужно было реализовать отдельную авторизацию. Добавление Telegram Login Widget не потребовало больших доработок - виджет работает через тот же JWT, что и Mini App, а само приложение почти не зависит от Telegram после выполнения авторизации.
type: article
createdAt: 2026-07-01
updatedAt: 2026-09-30
projects: [boards]
tags: [dotnet, aspnet-core, telegram, authentication, telegram-login-widget, jwt, devlog]
previousLink: telegram-media-group-album-bot
---

> **Architecture First: как в одиночку с ИИ сделать альтернативу Jira** — Часть 13.
> В [предыдущей статье](telegram-media-group-album-bot) была доделана последняя функциональность, запланированная в MVP бота. Здесь мы переходим к веб-версии, о которой просили пользователи. Чтобы её выпустить, приложению сначала нужна авторизация, работающая вне Telegram Mini App.

До сих пор единственным вариантом логина оставался запуск Telegram Mini App, который авторизует клиента через данные, предоставленные Telegram. Однако, поступил фидбэк, что Mini App не всегда удобен для работы с приложением - пользователям хотелось иметь вкладку с досками в браузере под рукой. Веб-версия, открытая в браузере вне Telegram, не может полагаться на объект init data от Telegram, для аутентификации, как это происходило в случае с Mini App. Настройка авторизации веб-версии приложения через Telegram и является темой этой статьи. А еще расскажем о том, как в процессе работы над авторизацией, мы, наконец, поняли, что Telegram в нашей архитектуре не является core - функциональностью, а скорее - лишь одна из возможных интеграций.

## Telegram как один из провайдеров авторизации

Mini App выполняет авторизацию через init data Telegram, валидируемую на бэкенде, который затем выпускает JWT для приложения, — последовательность описана в [статье про авторизацию](telegram-mini-app-authentication-dotnet). Токен — это собственный bearer приложения, который содержит внутренний идентификатор пользователя, а не его Telegram ID. Каждый запрос после логина содержит этот JWT в заголовках. То есть для авторизованной сессии Telegram нужен только один раз — при получении Bearer токена.

Именно это делает добавление нового провайдера авторизации простым. Он просто должен провалидировать как-то личность пользователя и выпустить тот же JWT, — остальное приложение продолжит работать также, как и ранее, в Mini App версии.

## Отличия авторизации в веб-версии от авторизации через Telegram Mini App

У браузера, открытого вне Telegram, нет объекта init data, который есть в Mini App. Стандартный способ выполнить авторизацию через Telegram на веб-странице — [Telegram Login Widget](https://core.telegram.org/widgets/login): скрипт, который добавляет кнопку «Log in with Telegram» и возвращает объект пользователя при выполнении авторизации. [В исходниках](https://github.com/Laraue/laraue-boards/blob/master/app/pages/index.vue) можно увидеть, как обрабатывается подобный коллбэк:

```ts
(window as any).onTelegramAuth = async (user: any) => {
    const { authViaWebApp } = useTelegramUserApi()
    const bearer = await authViaWebApp(user)
    await initUserWithBearer(bearer)
};
```

Когда пользователь авторизуется через стандартное окно авторизации Telegram, виджет вызывает `onTelegramAuth` с подписанными данными, фронтенд отправляет их на бэкенд и получает bearer-токен — после чего [`initUserWithBearer`](https://github.com/Laraue/laraue-boards/blob/master/app/composables/auth.ts) приводит приложение ровно в то состояние, в которое привёл бы и логин через Mini App.

На бэкенде для авторизации через виджет сделан отдельный эндпоинт в ([`TelegramAuthController`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.WebApiHost/Controllers/TelegramAuthController.cs)):

```csharp
[HttpPost("auth")]
public Task<string> Authenticate(
    TelegramWidgetAuthRequest request,
    CancellationToken cancellationToken)
{
    return authService.Authenticate(request, cancellationToken);
}
```

Причина, по которой это не тот же метод, что использовался ранее — данные виджета валидируются иначе, чем init data у Mini App. А еще init data - это Url-encoded строка с объектом пользователя, в то время как виджет отправляет на бэкенд обычный объект - то есть контракты в двух методах авторизации различаются.

Задача метода авторизации - подтвердить, что данные пришли от Telegram, но схема проверки подписи здесь отличается. Вот вариант для веб-версии — [`ValidateWidgetData`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.WebApiHost/TelegramAuthService.cs):

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

Ключевое различие — в подписании. Виджет использует `SHA256(botToken)` в роли секрета, а затем `HMAC-SHA256(dataCheckString, secretKey)`; init data Mini App использует другой секрет — `HMAC(botToken)` с константой `WebAppData` в роли ключа. Из-за различий в авторизациях мы и решили разделить методы. Хотелось избежать классической ситуации в разработке: чиним одно - ломается другое. Правки одной из авторизаций не будут влиять на другую.

Остальные части у авторизаций похожи: собрать data-check-string из присутствующих полей, отсортированных по алфавиту и соединённых через `\n` (hash исключён), и возвращать `403`, если `auth_date` старше 24 часов. Точные и актуальные правила обеих схем можно найти в [документации Telegram](https://core.telegram.org/widgets/login#checking-authorization).

Так как `ValidateWidgetData` возвращает объект `MiniAppUser`, то дальше все работает точно так же как и в Mini App. Объект используется для выпуска JWT, который возвращается на фронтенд. Фронтенд добавляет заголовок `Aurhorization: Bearer {key}` при каждом вызове бэкенда — запрос считается авторизованным.

## После логина приложение не зависит от Telegram

Так как [приложение](https://boards.laraue.com) и так являлось публичным веб-адресом (просто скрытым от посторонних глаз, открывавшимся только из Telegram Mini App), оставалось поделиться им с пользователями. 

Как бонус, мы вдруг осознали, что приложение может работать в браузере и без Telegram (после логина). Получившаяся архитектура, которая отделяет внутренний `user_id` от `telegram_user_id` и системные `issues` от `telegram_messages`, позволяла в будущем добавить авторизацию через Google или создавать новые issues по сообщению из Slack, или выполнить еще какую-то интеграцию, не делая больших рефакторингов.

## Итоги

У веб-версии теперь есть авторизация, и её можно открыть в браузере — именно то, о чём просили пользователи. Добавление авторизации затронуло только один эндпоинт и один метод — `Authenticate` и `ValidateWidgetData` на бэкенде и страницу логина на фронтенде.

## Что дальше

Мы — разработчики и одновременно одни из самых активных пользователи Laraue Boards, столкнулись с неудобством — одних эпиков не хватало, чтобы разделять issues. Эпики, связанные с личной жизнью оказывались рядом с проектными активностями — хотелось это разделить. Решением стало добавление спейсов - групп эпиков относящихся к одному проекту. Таким образом можно было бы иметь отдельный спейс под личные активности и отдельный - под проектные.