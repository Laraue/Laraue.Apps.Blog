---
title: Clean Telegram bot architecture in .NET — controllers instead of a giant switch
description: Part 5 of building a Telegram task tracker solo. A clean .NET Telegram bot with ASP.NET-style controllers and middleware instead of a giant switch, a layered solution structure, and EF Core paired with linq2db on the same models for the queries EF Core handles poorly.
type: article
createdAt: 2026-06-21
updatedAt: 2026-09-30 07:44
projects: [boards]
tags: [dotnet, telegram, postgres, ef-core, linq2db, clean-architecture, devlog]
previousLink: choosing-stack-for-solo-project
nextLink: deploying-dotnet-postgres-vps-docker-compose
---

> **Architecture First: Building a Jira Alternative Solo, AI-Assisted** — Part 5.
> The first four articles were theory: why and what we are building, the user journey, the project stack. In this article we write the backend of a real Telegram bot.

The goal of the first development iteration is minimal: build a backend that can handle messages from Telegram users and save them. No web API and no frontend yet — just accepting messages from the user and putting them into the database.

## Telegram Host

At this stage we work with a single runnable project (host): [`TelegramHost`](https://github.com/Laraue/Laraue.Apps.Boards/tree/main/src/Laraue.Apps.Boards.TelegramHost). Its job is simple — connect to Telegram, pick up new updates, and put them into the database.

When the whole product is already in your head, it is tempting to lay out the entire project structure upfront — web API, background workers, services for future features. We want to move sequentially. Moving messages from the chat into the database is the minimal functionality the projects can be deployed with. That is where we stop.

## Solution structure

Even though we work on a single host, we try to make the solution structure as close as possible to what we want to end up with. It barely differs from project to project: the `src` folder holds all the projects, the `tests` folder holds the tests. The source code is in the [backend repository](https://github.com/Laraue/Laraue.Apps.Boards/tree/main/src); below is a breakdown of why the structure is the way it is.

We create the projects by hand — adding each `.csproj` through the code editor's context menu. In large companies this process is usually automated: a service with the right structure and settings is generated from a template. But that automation pays off when new services are created every day. We have few projects, and creating them takes very little time compared to other activities.

### Projects in the solution and naming conventions

The backend is split into separate projects, each responsible for its own level in the layered architecture. Naming follows the convention we use in all Laraue backend repositories — every project carries a namespace prefix.

- **`Laraue.Apps.Boards.DataAccess`** — the EF Core models, the enums related to them, and the database context. No business logic here — only the classes tied to the database and their configuration.
- **`Laraue.Apps.Boards.Services`** — business logic shared by all hosts. Creating an issue, for example, may add a record to the database, write to the change history and to the audit log. And since creation can be performed from different hosts, there is no point duplicating that logic.
- **`Laraue.Apps.Boards.TelegramServices`** — services specific to the Telegram host. They wrap calls to the shared services with host-specific logic. For example, the Telegram service, besides creating an issue through the shared service, must also attach a `TelegramMessage` reference to it.
- **`Laraue.Apps.Boards.TelegramHost`** — the runnable project (host) that talks to Telegram.

A single prefix of the form `Laraue.Apps.Boards.*` gives every project in the company a unique name, which makes its purpose obvious and helps avoid configuration mistakes.

### One host per function

A host is named after the function it performs: `TelegramHost` handles the Telegram bot's requests, `WebApiHost` will serve HTTP API requests, and other projects may have a `RabbitWorkerHost` consuming a queue. The name should answer unambiguously what the process does.

This is a deliberate decision that simplifies managing the hosts. Each can get its own resource limits (a bot and a heavy background worker may consume memory and CPU very differently), its own network rules (the web API is exposed to the outside, a worker does not need that access), and its own restart behaviour. One host for all the functions would not allow any of this.

The Telegram host, for example, uses long polling — it reaches out to Telegram for updates itself, rather than working with webhooks. So it needs no public access at all. Its main jobs are moving new messages into a processing queue and working through that queue sequentially, one record at a time. Which means its memory limits can be kept low.

### Core and Host services

One of our rules: a host may only interact with its own services. Using Core services directly in controllers is not allowed. That is, the Telegram host can inject classes from `TelegramServices`, and those in turn can work with classes from `Services`.

This helps add new entry points to the app quickly. When the web API appears, it will implement its own host-level services. They will have their own permission checks and validation, but they will call the same Core services the bot already calls.

This split undeniably costs developer effort and is not always worth it. If the solution contained only the Telegram bot's code, and we knew no new hosts were coming, we would not spend the effort on it.

## The first database models

The database schema follows from the user journey defined earlier: the user writes a message to the bot and it is saved, to be shown on a board later. The first tables of this iteration are the user, the message (a card on the board), and the Telegram message.

The main table is `Message`. It is one of the messages on the board:

```csharp
public class Message
{
    public long Id { get; set; }

    [MaxLength(4096)]
    public required string? Content { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // The user the message belongs to.
    public Guid UserId { get; set; }
    public User? User { get; set; }

    // The message's current status.
    public long StatusId { get; set; }
    public Status Status { get; set; }

    // Category it belongs to. Null for backlog.
    public long? CategoryId { get; set; }
    public Category? Category { get; set; }

    // Linked Telegram message. Null means it was not created through Telegram.
    public TelegramMessage? TelegramMessage { get; set; }
    public long? TelegramMessageId { get; set; }
}
```

The link to the Telegram message identifier is optional: the `TelegramMessageId` field is nullable. From the very first iteration the model allows a card on the board to be created without using the messenger at all.

The [`User`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.DataAccess/Models/User.cs) model implements `ITelegramUser<Guid>` — an interface from our [Laraue.Telegram.NET](https://github.com/Laraue/Laraue.Telegram.NET) library. The user is saved by the library automatically on the first interaction, which is why the model has to implement this interface.

The [`TelegramMessage`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.DataAccess/Models/TelegramMessage.cs) model holds references to a message's various Telegram identifiers. With it we try to keep Telegram-specific identifiers out of the app's main business logic.

### Limiting the length of string columns

Every string in the models gets an explicit maximum length: `Content` — 4096, `Name` — 128, `Color` — 7. A string column with no length limit implicitly turns into `text`/`varchar(max)`, which leads to indexing problems and suboptimal database behaviour. A limit removes those problems.

Choosing the limit also makes you think about what data is going to be stored in the column. For example, 4096 for `Content` is not accidental — the column matches Telegram's real message length limit. Limiting `Color` to seven characters is exactly right for storing a hex code like `#1D9E75`.

### The mistake: picking the entity name without enough thought

Initially we named the model for a card on the board `Message`. At later stages, when card creation through the web API was added, an ambiguity appeared. The interface showed labels like "create message" / "edit message", which looked illogical. We renamed the entity to `Card`. But that name fit the bot poorly — the user creates a note, not a card. In the end we settled on the final name, `Issue`, which we live with to this day.

Each such rename is not just a find-and-replace operation: the name is woven into the models and the names of services, their methods, and DTOs. The names also show up in the user interfaces.

We never did come up with an idea of how this could have been avoided. The right name was not known in the first iteration, and `Message` looked like a correct choice. The only conclusion: renaming models can take a lot of time, so think twice before picking the final name. And our mistake is not unique: Atlassian renamed Jira's "projects" to ["spaces"](https://community.atlassian.com/forums/Jira-articles/Jira-Spaces-have-landed/ba-p/3117620), and "issues" to "work", as the product evolved.

#### The project naming changed too

We could not settle on a project name for a long time, so we started with `Laraue.Apps.StructuredMessages`. When the name `Laraue Boards` settled, the repository, the solution, all the namespaces, and the deploy files were renamed (not all of them, actually — we periodically find something we forgot to change). Good practice for changes like this is one PR with no other changes in it, to keep the review easy and a rollback simple.

## Clean architecture for handling Telegram messages

The database is where a message ends up. The more interesting part is how it gets there.

The bot is designed to handle two kinds of messages. The first is commands: the user asks for something to be done, the bot does it. The `/start` command, for example. The second is all the other messages: if the user typed something and it is not a command, the message is simply saved to the database, to show up on the board later.

Commands are routed to controllers, ASP.NET-style, with our [Laraue.Telegram.NET](https://github.com/Laraue/Laraue.Telegram.NET) library. The `/start` command, for example, looks like this:

```csharp
public class CommandsController(ITelegramCommandsService commandsService)
    : TelegramController
{
    [TelegramMessageRoute("/start")]
    public Task HandleStart(
        RequestContext requestContext,
        CancellationToken cancellationToken)
    {
        return commandsService.HandleStart(
            ReplyData.FromMessageRequest(requestContext),
            cancellationToken);
    }
}
```

In .NET bots this pattern is almost never used; we made this library ourselves. Most C# Telegram bot examples look like a single method handling all the messages with a big set of switches. That works in small bots but becomes hard to maintain in large ones. So we decided to take the MVC architecture and carry it over into this library.

When the user sent something that is not a command, none of the routes will match, and the message is handled by [`HandleAllMessagesMiddleware`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.TelegramHost/HandleAllMessagesMiddleware.cs), working as a fallback. It is not ASP.NET middleware; it is added to the container with `AddTelegramMiddleware<HandleAllMessagesMiddleware>()` — an extension method from [Laraue.Telegram.NET](https://github.com/Laraue/Laraue.Telegram.NET).

```csharp
if (context.GetExecutedRoute() is null && AllowedUpdates.Contains(context.Update.Type))
{
    var message = context.Update.Message ?? context.Update.EditedMessage;

    SaveMessageTelegramRequest? request = message.Type switch
    {
        MessageType.Text => GetMessageRequest(message),
        // photo, video, animation handled here too, later in the series
        _ => null
    };

    if (request is not null)
        await telegramMessageService.HandleSaveMessage(request, ct);
}
```

When a text update arrives, it is mapped into a `SaveTextMessageTelegramRequest` and passed to the host-level service for saving. The business logic does not need to know about Telegram's update types, so we try to map to internal types as early as possible.

The full path of an update looks like this:

1. **The middleware** maps the Telegram update into a `SaveTextMessageTelegramRequest` and passes it to the host-level message-saving service.
2. **The host-level message service** opens a transaction, saves the `TelegramMessage`, calls issue saving in the Core service, and commits the transaction.
3. **The Core issues service** adds the `Message` record (in later iterations, `Issue`) to the database.

## Configuring the Telegram host

The host is set up and configured in [`Program.cs`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.TelegramHost/Program.cs):

```csharp
var builder = WebApplication.CreateBuilder(args);

const string dbConnectionStringName = "Postgre";

builder
    .AddTelegramOptions("Telegram")
    .AddApplicationServices()
    .AddDatabaseServices(dbConnectionStringName);

builder.Services.AddHealthChecks();

var app = builder.Build();

app.Services.UseLinq2Db();

using (var scope = app.Services.CreateScope())
{
    await using var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
    await db.Database.MigrateAsync();
    app.MapTelegramRequests();
}

app.MapHealthChecks("/_health");

app.Run();
```

The registration is grouped into extension methods — `AddTelegramOptions`, `AddApplicationServices`, `AddDatabaseServices` — to keep the service's dependencies easy to understand. We try to avoid long chains of `services.Add()` calls.

Before each service start we run the migrations with `await db.Database.MigrateAsync()`. You will rarely see this approach in corporate applications — companies prefer maximum control and run the migration step themselves. For small applications it is a conscious simplification.

### Creating migrations

To create a migration, run this command in the `src` folder:

```bash
dotnet ef migrations add MigrationName \
  -p Laraue.Apps.Boards.DataAccess \
  -s Laraue.Apps.Boards.TelegramHost \
  -v
```

We try to stick to the approach: one pull request = one migration. While a PR is in draft, new migrations keep appearing in it — something is added, changed, removed. But before the PR is ready, we merge them. Migrations can be merged by hand, moving the generated code from one file into another; or delete all the ones made in this PR, roll back the `DatabaseContextSnapshot`, and generate one new migration containing all the changes.

The reason is convenient history navigation. A project that lives for years accumulates a lot of migrations: an endless list of tiny changes is hard to read when you are trying to understand how a table evolved. And initializing a fresh database replays all the migrations in order, which can take significant time.

### Combining EF Core and linq2db in one project

The project uses EF Core as the default ORM for queries. But some cases are not supported by that framework, and that is where linq2db helps. Both ORMs work with the same models — the registration is the `app.Services.UseLinq2Db()` line. `UseLinq2Db()` is just our [wrapper](https://github.com/Laraue/Laraue.Core/blob/master/src/Laraue.Core.DataAccess.Linq2DB/Extensions/ServiceCollectionExtensions.cs) for working with the official [`LinqToDB.EntityFrameworkCore`](https://github.com/linq2db/linq2db/tree/master/Source/LinqToDB.EntityFrameworkCore) adapter.

Why not use linq2db all the time? Two reasons. First: there are cases EF Core handles that linq2db does not. Second: EF Core has very convenient change tracking and migrations tooling, which linq2db lacks.

## Health checks from day one

In `Program.cs` you can see the lines `AddHealthChecks()` and `MapHealthChecks("/_health")`. They go into every host from the first commit. A request to `/_health` returns whether the host is alive and able to serve requests. The health checks will come in handy later, when the infrastructure is being set up.

## Conclusions

The result of the iteration is a .NET backend with an architecture split into layers and a single Telegram host. It accepts Telegram messages and routes commands through controllers. Ordinary messages are mapped through the fallback middleware and passed to the Host-level service, go from there to the Core service, and finally end up in the PostgreSQL database. The host runs migrations at startup and has a health check endpoint.

None of this is deployed yet: the code builds and runs locally, but has not reached a server.

## What comes next

The next article is about deploying the host to a real server. We will tell about the VPS with self-hosted PostgreSQL, how we set up the build and artifact delivery pipeline, what is in the Dockerfile and Docker Compose files, and how it all turned out.