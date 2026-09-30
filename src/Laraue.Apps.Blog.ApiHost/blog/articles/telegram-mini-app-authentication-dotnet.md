---
title: Telegram Mini App authentication in .NET end to end — initData validation, JWT issuing and the Nuxt frontend
description: Part 8 of building a Telegram task tracker solo. The full Telegram Mini App authentication flow in a real .NET and Nuxt app — validating the initData signature on the server with HMAC-SHA256, issuing and using a JWT bearer, reading the user from HttpContext, and why CORS matters.
type: article
createdAt: 2026-06-24 08:00
updatedAt: 2026-09-30 07:44
projects: [boards]
tags: [dotnet, aspnet-core, nuxt, telegram-mini-app, authentication, initdata, jwt, cors, devlog]
previousLink: deploy-nuxt-telegram-mini-app-https-nginx
---

> **Architecture First: Building a Jira Alternative Solo, AI-Assisted** — Part 8.
> In the [previous article](deploy-nuxt-telegram-mini-app-https-nginx) we got the Mini App opening inside Telegram and displaying JSON with the user object. But that data cannot be trusted yet. In this article we build real authentication — adding a backend for the app and validating the data on its side.

At the end of the previous article the Mini App prototype started opening and showing the user object from the init data that Telegram injects into the app. But that data could not be used yet. Anyone could hand the app a forged init data string, and the app has to verify its authenticity. To validate it, the app first needs to add the backend it has been missing.

## Backend: the Dotnet Web API Host

The backend is a new host that will handle API requests coming from the Laraue Boards frontend. Its name — `WebApiHost` — matches its function. The host was already mentioned in the [backend architecture article](clean-dotnet-telegram-bot-architecture) but did not exist when that article was written. Now [`WebApiHost`](https://github.com/Laraue/Laraue.Apps.Boards/tree/main/src/Laraue.Apps.Boards.WebApiHost) is added to the repository.

The new backend shares common code with `TelegramHost` — the same models from `DataAccess`, the same core services from `Services`. The database, accordingly, is also shared by the two services — not quite a microservice approach, but there is no point overcomplicating the architecture at this stage. Creating an issue from the web API contains the same base logic as creating one from Telegram. What is new here is the service layer for the web API and the host itself.

The host's [`Program.cs`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.WebApiHost/Program.cs) is small and mostly follows the ASP.NET Core web API template, with two additions that matter for this article: authentication and CORS.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<TelegramOptions>();
builder.Services.Configure<TelegramOptions>(
    builder.Configuration.GetSection("Telegram"));

const string dbConnectionStringName = "Postgre";

builder.Services.AddAuthorization();
builder
    .AddAuthentication()
    .AddApplicationServices()
    .AddDatabaseServices(dbConnectionStringName);

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Services.UseLinq2Db();
app.UseMiddleware<ExceptionHandleMiddleware>();

using (var scope = app.Services.CreateScope())
{
    await using var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
    await db.Database.MigrateAsync();
}

var origins = builder
    .Configuration
    .GetSection("Cors:Hosts")
    .Get<string[]>();

if (origins is not null)
{
    app.UseCors(corsPolicyBuilder =>
        corsPolicyBuilder.WithOrigins(origins)
            .AllowCredentials()
            .AllowAnyMethod()
            .AllowAnyHeader());
}

app.MapHealthChecks("/_health");

app.Run();
```

Most of this code is the same as in `TelegramHost` and was explained earlier. `TelegramOptions` holds the bot token — the web API will need it to validate init data. `AddDatabaseServices` registers the same data layer as the bot's; the migration-on-startup block and `MapHealthChecks("/_health")` are the same patterns as in the bot host; `app.Services.UseLinq2Db()` is the same EF Core and linq2db pairing from the [backend architecture article](clean-dotnet-telegram-bot-architecture).

New here are a couple of lines: `AddAuthentication()` / `UseAuthentication()` and the `UseCors()` block. Authentication will be needed to create a Bearer token for the app once the init data from Telegram has been verified, and the CORS configuration is needed for local development — a detailed description is at the end of the article.

### Splitting services into core and host-specific

`AddApplicationServices` is not a method shared by all hosts — the web API has [its own `AddApplicationServices`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.WebApiHost/WebApplicationBuilderExtensions.cs). The purpose of such a method is to register the host-specific services and to call the registration of the common (core) services.

`ExceptionHandleMiddleware` is a custom middleware from our shared [Laraue.Core](https://github.com/Laraue/Laraue.Core) package that automatically maps the library's web exceptions to HTTP codes. If an unhandled `BadRequestException` is thrown in the code, the client gets a `400` error; a `ForbiddenException` turns into a `403`, and so on.

## Authenticating the user by init data from the Telegram Mini App

Before moving to the code, let's define the sequence of steps for a login from the Mini App:

1. The app checks whether it is running inside Telegram, by making sure init data is available (the [`auth.init.ts`](https://github.com/Laraue/laraue-boards/blob/master/app/plugins/auth.init.ts) plugin from the previous article).
2. Send the init data to the web API's authentication endpoint.
3. The backend validates the init data signature against the bot token and returns an authorization **bearer token**.
4. The frontend saves the bearer to local storage.
5. The frontend requests the user's information from the backend, using the bearer obtained earlier.
6. The backend finds the user in the database and returns their data.
7. The frontend puts the user information into the shared `appState.ts` composable.

After that, every backend call adds the authorization header with the bearer, and any component can read the user's fields from the shared state. Now let's take each step in order.

### Steps 1–2: the frontend sends init data

The trigger is the startup plugin from the previous article, [`auth.init.ts`](https://github.com/Laraue/laraue-boards/blob/master/app/plugins/auth.init.ts). It is a Nuxt plugin from the `/app/plugins` directory that runs automatically when the app loads. In the first version the plugin simply set the user object into `appState` from the available init data: `setUser(WebApp.initData)`. Now the init data is sent to the backend for validation instead.

Each backend controller has a matching composable on the frontend, which defines the endpoint calls as typed functions. For example, this is `loadUser` in `userApi.ts`, calling the backend's `GET /user` method and returning a `UserDto`:

```ts
export const useUserApi = () => {
    const client = useUserClient();

    const loadUser = () => {
        return client<UserDto>('/user', {
            method: 'GET'
        });
    }
    // ...
}
```

The `client` itself is configured in a separate `userClient.ts`. This is where the base address from the app settings is set, and a client that works with authorization headers is used:

```ts
export const useUserClient = () => {
    const configuration = useRuntimeConfig();
    const { createClient } = useUserAuthApi();
    return createClient(configuration.public.messagesBaseAddress);
}
```

`useUserAuthApi` is what actually creates the client and defines its behaviour. This is where the bearer from localStorage is automatically added to requests:

```ts
export const useUserAuthApi = () => {
    const { getUserToken } = useLocalStorageUtils();

    const createClient = (baseURL: string) => $fetch.create({
        baseURL: baseURL,
        headers: {
            Authorization: `Bearer ${getUserToken()}`
        },
        // ...
    })

    return { createClient }
}
```

So the architecture is: `userApi` knows *what* to call (the endpoints and their shapes), while `userClient`/`useUserAuthApi` know *how* to talk to the API (the base address and the bearer header). The `messagesBaseAddress` comes from the Nuxt runtime configuration in [`nuxt.config.ts`](https://github.com/Laraue/laraue-boards/blob/master/nuxt.config.ts), where it is supplied from an environment variable:

```ts
runtimeConfig: {
    public: {
        messagesBaseAddress: process.env.NUXT_PUBLIC_MESSAGES_BASE_ADDRESS
    }
}
```

### Step 3: the backend validates init data

Over to the server side. The frontend has sent it the `initData` string — an encoded string with the user's data, signed by Telegram. The backend's job is to use the bot's key to check that the signature is correct, and to return a bearer token for authorization.

> The init data approach only works for authorization through a Telegram Mini App. Telegram also supports logging in from a regular website through the "Log In with Telegram" widget — that is a separate mechanism, covered in the article about the [widget login](telegram-login-widget-dotnet-auth).

The request arrives at [`TelegramAuthController`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.WebApiHost/Controllers/TelegramAuthController.cs), which accepts the request with init data and proxies it to the internal service:

```csharp
[ApiController]
[Route("api/auth")]
public class TelegramAuthController(ITelegramAuthService authService) : ControllerBase
{
    [HttpPost("mini-app")]
    public Task<string> AuthenticateViaMiniApp(
        [FromBody] AuthenticateViaStringInitDataRequest request,
        CancellationToken cancellationToken)
    {
        return authService.Authenticate(request, cancellationToken);
    }
}
```

[`TelegramAuthService`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.WebApiHost/TelegramAuthService.cs) performs two operations: it validates the init data and issues a token for the verified user.

```csharp
public Task<string> Authenticate(
    AuthenticateViaStringInitDataRequest request,
    CancellationToken cancellationToken)
{
    var userData = ValidateInitData(request.InitData);
    return CreateBearerToken(userData, cancellationToken);
}
```

The validation method's job is to compute a hash of all the user data using the bot's secret key, and compare it with the hash that was used for the signature in the init data object. If the hashes match, the data is genuine and can be trusted.

```csharp
private MiniAppUser ValidateInitData(string initData)
{
    var parsedData = HttpUtility.ParseQueryString(initData);
    var receivedHash = parsedData["hash"];
    parsedData.Remove("hash");

    if (string.IsNullOrEmpty(receivedHash))
        throw new ForbiddenException("Hash is missing");

    var generatedHash = BuildHash(parsedData);

    var result = generatedHash.Equals(receivedHash, StringComparison.OrdinalIgnoreCase);
    if (!result)
        throw new ForbiddenException("Hash mismatch");

    var user = parsedData["user"];
    return JsonSerializer.Deserialize<MiniAppUser>(user!, JsonBotAPI.Options)!;
}

public string BuildHash(NameValueCollection collection)
{
    var sortedKeys = collection.AllKeys.OrderBy(key => key, StringComparer.Ordinal).ToList();
    var dataCheckStrings = sortedKeys.Select(key => $"{key}={collection[key]}");
    var dataCheckString = string.Join("\n", dataCheckStrings);

    var secretKey = HMACSHA256.HashData(
        "WebAppData"u8.ToArray(),
        Encoding.UTF8.GetBytes(options.Value.Token));

    var generatedHashBytes = HMACSHA256.HashData(
        secretKey,
        Encoding.UTF8.GetBytes(dataCheckString));

    var generatedHash = Convert.ToHexString(generatedHashBytes).ToLower();
    return generatedHash;
}
```

`BuildHash` follows the Telegram algorithm from the documentation:

1. **Build the data-check-string.** Take every field of init data except hash, sort the keys alphabetically, turn each field into a `key=value` string, and join the strings into one with `\n` as the separator.
2. **Get the secret key.** The secret key is the `HMAC-SHA256` of the string literal `"WebAppData"`, keyed with the bot token.
3. **Compute the signature.** Run `HMAC-SHA256` over the data-check-string with the secret key from step 2, and convert the result to a hex string.

If the hash computed by the server matches the hash from Telegram, the data is genuine. Then the service deserializes the `user` field into a `MiniAppUser` and considers the request valid. Any difference raises a `ForbiddenException`, and the client gets a `403` code.

### Step 4: the backend issues a bearer, the frontend saves it to local storage

Working with `initData` on every request would be wasteful, so the server issues a **bearer token** for further authorized interaction with it.

#### Issuing the token and auto-registering the user on first interaction

Once `ValidateInitData` has confirmed the user is real, `Authenticate` calls `CreateBearerToken` to create a token with their identifier:

```csharp
private async Task<string> CreateBearerToken(
    MiniAppUser userData, CancellationToken cancellationToken)
{
    var data = await context.Users
        .Where(x => x.TelegramId == userData.Id)
        .Select(x => new { x.Id })
        .FirstOrDefaultAsyncEF(cancellationToken);

    if (data is not null)
        return authService.CreateUserToken(data.Id);

    var newUserId = await RegisterUser(userData, cancellationToken);
    return authService.CreateUserToken(newUserId);
}
```

`CreateBearerToken` can register the user automatically. It looks the user up by the Telegram ID found in the init data; if found, it issues a token for them; if not, it registers the user and then issues the token. So the app has no separate registration step.

The token itself is issued by the `AuthService`, signing it with the `Auth__Key` secret from the app configuration. It is a standard JWT containing a single claim — the user's internal ID:

```csharp
public string CreateUserToken(Guid userId)
{
    var claims = new List<Claim>
    {
        new("id", userId.ToString())
    };

    var jwt = new JwtSecurityToken(
        issuer: Issuer,
        audience: UserAudience,
        claims: claims,
        signingCredentials: new SigningCredentials(
            GetSymmetricSecurityKey(options.Value.Key),
            SecurityAlgorithms.HmacSha256));

    return new JwtSecurityTokenHandler().WriteToken(jwt);
}

public static SymmetricSecurityKey GetSymmetricSecurityKey(string key)
{
    return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
}
```

#### How the bearer authenticates every subsequent request

The `AddAuthentication()` and `UseAuthentication()` lines in `Program.cs` are responsible for checking the bearer. The JWT is issued under a named authentication *scheme*, which explains how to validate a token of this kind — which issuer and audience to expect and which key to verify the signature with. `AddAuthentication()` registers the scheme, `UseAuthentication()` adds the middleware to the request pipeline, and when a request arrives with an `Authorization: Bearer <token>` header, it verifies the signature, checks the issuer/audience/token lifetime and, if the data is valid, builds a `ClaimsPrincipal` object from its claims and assigns it to the base controller's `HttpContext.User` property. A missing, expired, or wrongly-signed token is rejected with a `401` before the code of any controller method marked with the `[Authorize]` attribute runs.

This way any controller inheriting `ControllerBase` can access the `HttpContext.User` object in its methods marked `[Authorize]`:

```csharp
[HttpGet]
public Task<UserDto> GetAsync(CancellationToken ct)
{
    return service.GetUser(HttpContext.User.GetId(), ct);
}
```

`HttpContext.User` is the `ClaimsPrincipal` the middleware built from the bearer, and `GetId()` is a small extension that pulls the user ID back out of the `id` claim baked into the token at login:

```csharp
public static Guid GetId(this ClaimsPrincipal claimsPrincipal)
{
    var id = claimsPrincipal.FindFirstValue("id");
    return Guid.Parse(id!);
}
```

The bearer in this solution is a single signed JWT, without the "short-lived access plus long-lived refresh token" pair used in large production applications. We stick to the approach of everything in its own time, and do not want to spend a lot of effort protecting an app that so far has a small number of users.

### Steps 5–7: load the user data and put it into the app state

Saving the token is a thin wrapper over the browser's local storage:

```ts
const userTokenKey = 'bearer'

const getUserToken = () => localStorage.getItem(userTokenKey)
const setUserToken = async (bearerToken: string) =>
    localStorage.setItem(userTokenKey, bearerToken)
```

After saving the token, the app loads the user — the `loadUser` call through `GET /user`, which `UserController` answers by reading `HttpContext.User.GetId()`. The result comes back as a `UserDto` (name, language, colour, avatar, initials, user settings). We do not store this data in the bearer token, because that would grow its size and complicate the logic of updating user data (we would have to implement revoking old tokens after a data change).

Two functions in the `auth.ts` composable are used for the authentication work:

```ts
const initUserWithBearer = async (bearerToken: string) => {
    await setUserToken(bearerToken)
    const { loadUser } = useUserApi();
    const user = await loadUser();
    await initUserWithUserData(user);
}

const tryAuthWithStoredBearer = async () => {
    const { getUserToken } = useLocalStorageUtils()
    const bearer = getUserToken()

    if (!bearer)
        return false

    const { loadUser } = useUserApi();
    try {
        const user = await loadUser();
        await initUserWithUserData(user);
        return true;
    }
    catch (error) {
        console.error(error);
        return false;
    }
}
```

`initUserWithBearer` is called after login and covers steps 4–7: save the bearer, call `loadUser` to fetch the `UserDto`, and pass it to `initUserWithUserData`, which sets the user's data in the shared `appState` composable.

`tryAuthWithStoredBearer` is called from the plugin when the app loads. It is an optimization for repeat visits. If local storage already holds a bearer from a previous session, the app goes straight to loading the user, skipping the init data authorization step. But if there is no saved token, or it has expired (the `loadUser` call fails with a `401`), the app runs the full authorization flow. This makes reopening the Mini App faster.

So we now have verified user data in `appState`; what remains is to deploy the new host and make sure everything works the same on the server.

## Adding the web API host to Docker Compose

Since the web API host with the minimal authentication flow is ready, we can move on to its setup: we will run it on the same VPS where `TelegramHost` runs. The configuration must make the frontend's API calls reach this host. Add the service configuration to the `docker-compose` file, next to the Telegram bot and PostgreSQL containers:

```yaml
structuredmessageswebapihost:
  build:
    context: .
    dockerfile: "StructuredMessagesWebapiHostDockerfile"
  expose:
    - "5007"
  ports:
    - "8087:5007"
  restart: always
  environment:
    ASPNETCORE_ENVIRONMENT: "Production"
    Kestrel__EndPoints__Http__Url: "http://+:5007"
    Telegram__Token: "TokenHere"
    ConnectionStrings__Postgre: "User ID=PostgresUser;Password=PostgresPass;Host=postgres;Port=5432;Database=laraue_messages_board;Command Timeout=0;"
    Auth__Key: "SecretKeyHere"
    Logging__LogLevel__Default: "Warning"
  depends_on:
    postgres:
      condition: service_healthy
  networks:
    - dockerapi-dev
  deploy:
    resources:
      limits:
        memory: 256M
  healthcheck:
    test: ["CMD-SHELL", "curl -fsS http://localhost:5007/_health || exit 1"]
    interval: 5s
    timeout: 3s
    retries: 10
    start_period: 10s
```

The Dockerfile the container is built from is almost identical to the bot's container; only the paths change:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY structured-messages-webapi-host app
WORKDIR app
ENTRYPOINT ["dotnet", "Laraue.Apps.StructuredMessages.WebApiHost.dll"]
```

The Dockerfile configuration mostly repeats what was described in the [deploy article](deploying-dotnet-postgres-vps-docker-compose): the .NET runtime image and `curl` installed so the container healthcheck can run. The `docker-compose` part is the same story — the memory limit, the `curl` healthcheck of the `/_health` address, and `depends_on`, which makes the service wait for Postgres to start first.

A word on the host-specific environment variables. `Telegram__Token` is the bot token from `@BotFather`, which the web API needs to validate init data. `Auth__Key` is the secret for signing the JWT bearer tokens it issues. `Cors__Hosts__0/1/2` are the allowed CORS origins — the list of hosts allowed to call this backend.

## Routing api requests to the web API host in nginx

`location` blocks are added to the server block of `nginx.conf`, saying that if a request arrives at `{servername}/api/notes-board/{0}`, it should be handled by the web API host at `/api/{0}`:

```nginx
location ^~ /api/notes-board {
    set $upstream http://structuredmessageswebapihost:5007;
    rewrite ^/api/notes-board/(.*)$ /api/$1 break;
    proxy_pass         $upstream;
    proxy_next_upstream error timeout http_502 http_503 http_504;
    proxy_next_upstream_tries 5;
    proxy_next_upstream_timeout 30s;
}
```

As a result, the frontend calls the `/api/notes-board/user` address on its own origin (`boards.laraue.com`), and nginx calls the backend host's `/api/user`.

## CORS: configuration for local development

CORS — Cross-Origin Resource Sharing — is a browser security mechanism. By default the browser refuses to let a page from one origin read responses from another origin, where an "origin" is the combination of scheme, host, and port (`https://boards.laraue.com` is one origin; `https://abc123.ngrok-free.app` is another).

We can define exceptions to these rules. When a server *wants* to allow specific other origins to call it, it declares that by returning headers — `Access-Control-Allow-Origin` and the ones related to it — with the origins it trusts. That is what the `UseCors(...)` block in `Program.cs` does: it reads the list of allowed origins from configuration and tells the browser that requests from those origins are allowed.

In production the frontend and the API share one origin. The app is served from `boards.laraue.com`, and its API calls go to `boards.laraue.com/api/notes-board/...` — the same origin, so from the browser's point of view it is valid, and no extra CORS configuration is required.

Local development changes things. As described in the [previous article](deploy-nuxt-telegram-mini-app-https-nginx), we test the Mini App locally through ngrok, and ngrok gives the frontend and the backend *two different* public URLs. Their origins differ, so the browser blocks such requests. When launching the Mini App this looks like a **white screen** where nothing happens. The app loads, sends its first API request, gets nothing back, and never renders. To fix it, add the frontend's current ngrok address to the backend's allowed origins — the `Cors__Hosts` list the web API reads at startup.

## Conclusions

Visually the Mini App has barely changed: the app opens and shows some set of user data. But now that data can be trusted — which means we can start building the app's real functionality that previously required authentication.

## What comes next

The next article describes the web app's first real feature: working with issues. We will turn the Laraue Boards prototype from an AI-generated HTML template into real Vue components working with the backend over the API, and tell about the first product mistake, discovered after the first real users tried the app.