---
title: When not to use a nullable foreign key — modelling the empty state as a default row
description: Part 9 of building a Telegram task tracker solo. The issue layer in the web app and the database design decision at its centre — when a nullable foreign key is the wrong choice and a dedicated default row is better, using issues, epics and the backlog as the example.
type: article
createdAt: 2026-06-25 09:00
updatedAt: 2026-09-30 07:44
projects: [boards]
tags: [database-design, dotnet, aspnet-core, postgres, vue, devlog]
previousLink: telegram-mini-app-authentication-dotnet
nextLink: telegram-saved-messages-bot-lesson
---

> **Architecture First: Building a Jira Alternative Solo, AI-Assisted** — Part 9.
> In the [previous article](telegram-mini-app-authentication-dotnet) the app got a backend for authorization. In this one we develop the first real feature — the issues domain — and design the database for it.

The issues domain is the part of the app for working with tasks, their statuses and epics, letting the user manage all of it through the web interface. We will not walk through how to build CRUD for tasks — the source code is open, and the web is full of guides for that. Instead we will cover one practical problem of database schema design: how to represent an issue that does not belong to any epic. The obvious answer is to make the foreign key nullable, and we started with that approach. Later it significantly complicated the whole codebase, and we will look at why in detail.

## The Issue, Status and Epic models

An issue is a card on the board, created by the bot when it handles a message. It can also be created directly through the web interface:

```csharp
public class Issue
{
    public long Id { get; set; }

    [MaxLength(4096)]
    public required string? Content { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public long StatusId { get; set; }
    public Status? Status { get; set; }

    public TelegramMessage? TelegramMessage { get; set; }
    public long? TelegramMessageId { get; set; }
}
```

The key relation is `StatusId`: an issue is always in some status, and a status is always tied to some board (that is, an epic). A `Status` is one column on the board:

```csharp
public class Status
{
    public long Id { get; set; }

    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(7)]
    public string Color { get; set; } = string.Empty;

    public long EpicId { get; set; }
    public Epic? Epic { get; set; }

    public int SortOrder { get; set; }

    public IList<Issue>? Issues { get; set; }
}
```

A status belongs to an epic through `EpicId`. The chain is `Issue → Status → Epic` — an issue does not reference its epic directly; it reaches it through the status.

## A nullable foreign key or a default row?

Most issues live in some epic; but the app also provides a backlog — where tasks do not yet belong to any epic. So the database schema has to account for creating an issue without an epic, and there are two ways to do it.

### First way: make the foreign key nullable.

In the backlog, tasks have no status, so at the model level an issue could have a nullable `StatusId` field, with `null` meaning the backlog. That is the construction we started with.

In practice the approach brought quite a few problems. When `null` means not "absent" but "backlog", every piece of code has to remember that and account for it in its implementation. Branches like `if (StatusId == null) || …` multiply through the code, and you constantly have to ask whether some extra logic or validation is needed when the status field is `null`. In short, every new feature working with issues has to handle the null case all over again.

The second problem: the backlog loses its customizability. From the interface point of view, the backlog can behave partially like the other epics (renaming, changing the colour), and the nullable model has no graceful way to handle those cases: either create a separate table duplicating the fields of `epics`, with branching logic in the business layer, or accept a backlog that can never be configured.

### Second way: a default row.

For every user, at creation time, a **default epic** with **one default status** is added to the database — that is, the backlog is an epic carrying the `IsDefault` flag in the model:

```csharp
public class Epic
{
    public long Id { get; set; }

    [MaxLength(128)]
    public required string Name { get; set; }

    public Guid UserId { get; set; }
    public long SpaceId { get; set; }

    public bool IsDefault { get; set; }
    public IList<Status>? Statuses { get; set; }
    // ... timestamps
}
```

With this approach it is harder to make critical mistakes in the code, like showing backlog tasks to a user they do not belong to. But like any approach, it has its price. The default epic must exist for every user, and its creation has to be guaranteed — we settled on the scheme where the backlog is created at registration. Besides that, branches in the style of "if the epic is the backlog, deletion is forbidden" are unavoidable (on the backend as well as on the frontend). However, such branches are implemented only in the few places where the logic of regular epics and the backlog must differ, and do not spread across the code of every service.

For the future we settled on using a nullable foreign key when `null` means a genuinely absent relation: an employee with no manager in `manager_id`, an order with no courier assigned yet. Even where a nullable column looks like a good option, it is worth remembering that as functionality grows, a case may appear that is hard to handle with that approach.

### Data redundancy or an extra join

The relation chain `Issue → Status → Epic` means that to find an issue's epic, the database has to join: `issues → statuses → epics`. This could be avoided by adding an `EpicId` relation directly to the issue — an optimization sometimes used to speed up reads.

We declined the premature optimization: storing the extra field would require duplicating information in the database and updating it correctly — a potential source of bugs. At the current scale the extra join causes no problems, while hunting the potential bugs could cost the developers a lot of time.

## Backend: controllers, Host-level services, Core-level services

In the backend we try to stick to a layered architecture. The easiest way to see it is a concrete example — creating an issue.

The request is received by the thin [`IssuesController`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.WebApiHost/Controllers/IssuesController.cs). All its endpoints require authorization — the controller is marked with the `[Authorize]` attribute. The job of each controller method is to enrich the request with the user's data from `HttpContext.User` and forward it to the Host-level service [`IssuesService`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.WebApiServices/IssuesService.cs):

```csharp
public interface IIssuesService
{
    Task<ColumnIssues[]> GetBoard(
        GetBoardRequest request,
        CancellationToken cancellationToken);

    Task Delete(DeleteIssueRequest request, CancellationToken ct);
    Task<long> Create(CreateIssueRequest request, CancellationToken ct);
    Task Update(UpdateIssueRequest request, CancellationToken ct);

    Task<IssueDetailDto> GetIssue(
        GetIssueRequest request,
        CancellationToken cancellationToken);
}
```

The service level's job covers checking the caller's permissions, validation, opening a transaction when needed, and data mapping. The service does not perform the create/update/delete database operations itself — that is the responsibility of the Core-level service [`ICoreIssuesService`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.Services/CoreIssuesService.cs), which handles the shared change logic:

```csharp
public interface ICoreIssuesService
{
    Task<long> Create(
        CreateIssueRequest request,
        CancellationToken cancellationToken);

    Task Update(
        long issueId,
        Action<UpdateSettersBuilder<Issue>> setters,
        CancellationToken cancellationToken);

    Task Delete(
        long id,
        CancellationToken cancellationToken);
}
```

As a result, the request route when creating an issue looks like this: controller → host service (permission check, transaction opening) → core service (row write).

### Splitting responsibility between Host and Core services

The **Core service**'s job is to encapsulate complex reusable logic. Host-level services can simply call an issue update and not think about the fact that besides updating the `Issue` entity, they must not forget to update `TouchedAt` on the epic the issue belongs to, or add a record to the change history table.

The reason for the split: core services are used by both the web API and the telegram hosts. An issue can be changed both from a Telegram chat and from the web interface, and the change has to be handled correctly in both cases. We avoid duplicating code while keeping room to manoeuvre at the host level (for example, doing validation differently).

### Checking access permissions

At the current stage access is separated quite simply — a user can read and change only the issues and epics they created. But since the possibility of an organization mode is planned for the future, we extract the methods for getting available entities into a separate [`AccessService`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.Services/AccessService.cs) class — this will cut the refactoring time later:

```csharp
public interface IAccessService
{
    Task<T> GetAvailableEpics<T>(
        Guid userId,
        Func<IQueryable<Epic>, Task<T>> map,
        CancellationToken cancellationToken);

    Task<T> GetAvailableIssues<T>(
        Guid userId,
        Func<IQueryable<Issue>, Task<T>> map,
        CancellationToken cancellationToken);

    Task<bool> CanMoveToStatus(
        Guid userId, long statusId, CancellationToken cancellationToken);

    Task<bool> CanModifyStatus(
        Guid userId, long statusId, CancellationToken cancellationToken);
}
```

`GetAvailableEpics` and `GetAvailableIssues` take a `userId` and a `map` function that projects the entities available to the user. The host service decides itself what to do with the available entities (count, project, fetch a row) — and by construction cannot get an `IQueryable` with objects that do not belong to the user.

### Transactions

The transaction is always managed by the host service. Only it knows when the operation is fully complete. We could have used nested transactions, or an application-level transaction counter, and dropped these rules. But, as practice shows, it is easier to set constraints at the architecture level than to run into constraints at the infrastructure level later (for example, nested transactions are not supported in every database).

Even though the host-level service manages the transaction, there are situations when a core service needs atomicity regardless of who called it — it writes to several tables at once. It cannot blindly hope that the caller has already opened a transaction. For that, the internal service uses `EnsureTransaction`: if a transaction was not opened at the host level, an exception is thrown.

## From the AI HTML prototype to Vue components

Once the API is ready, the frontend can display real data. The visual design was generated at the [start of the series](prototyping-ui-with-ai-before-code). But it was only an HTML mockup. Time to turn it into real components.

In general, AI can split the mockup file into components on its own, but we mostly did it by hand: looked at the generated markup and asked the AI to make one component from a specific piece of code. A repeating `<div class="card">` is a `Card` component, a repeating column wrapper is a `Column`.

The reason we did not ask it to generate all the components at once is the wish to control the process. A huge pull request cannot be reviewed attentively — it will contain mistakes we will not even notice. Extracting one component at a time keeps every step small and checkable. The result is in the [components folder](https://github.com/Laraue/laraue-boards/tree/master/app/components).

### Frontend API clients

Each backend controller has a matching composable client on the frontend, in the same style as `userApi` from the previous article. Epics have [`epicsApi.ts`](https://github.com/Laraue/laraue-boards/blob/master/app/composables/epicsApi.ts), issues have their own.

We write them ourselves. The alternative is generating the clients from an OpenAPI/Swagger document, so the frontend types stay automatically in sync with the backend. But that takes time — to make the code generate in exactly the shape we need. While the API has not grown large, we settled on the manual option.

### A few words about applyInsets: fitting the app into the Telegram frame

Opening the app in the Telegram Mini App, we ran into part of it being covered by Telegram's own frames. To fix this, Telegram provides *insets* — paddings describing how much space Telegram's UI takes at the edges. The app applies them in [`app.vue`](https://github.com/Laraue/laraue-boards/blob/master/app/app.vue) through an `applyInsets` step, adding inner paddings to the layout.

Insets alone do not fully solve the problem: apply them the safe way, as described in the documentation, and you can get large empty areas on the screen. Apply them the unsafe way, and the Telegram Mini App controls will overlap the app's UI elements. We settled on detecting that the app is running inside Telegram and adjusting the CSS separately for that case. We recommend debugging this part locally — making everything render nicely can take a lot of attempts.

## Conclusions

The web app shows the user's issues on a board, pulled from the authenticated backend. Issues can be created, moved between statuses, and deleted, and every user works only with their own data. The line-by-line CRUD implementation was not covered — the code is in the repositories ([backend](https://github.com/Laraue/Laraue.Apps.Boards), [frontend](https://github.com/Laraue/laraue-boards)).

Now it is time to show the app to people and get honest reactions. We posted about it in our Telegram channels, wrote a short product presentation on Threads for people who had never heard of it, and sent direct messages to friends asking them to try it. The goal is to watch how real people use the app and find out where it works and where it does not.

## What comes next

The next article is about that feedback. It is about a product mistake we made, and how we fixed it.