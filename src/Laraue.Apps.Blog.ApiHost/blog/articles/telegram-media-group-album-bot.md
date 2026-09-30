---
title: One card from a Telegram album — handling media groups and edits in a bot, without timeout hacks
description: Part 12 of building a Telegram task tracker solo. Telegram delivers an album as a burst of separate messages and an edit as a fresh update — here is how to turn a media group into a single record without the usual timeout accumulator, and how to treat an edit as an update instead of a duplicate.
type: article
createdAt: 2026-06-26 15:00
updatedAt: 2026-09-30 07:44
projects: [boards]
tags: [dotnet, telegram-bot, media-groups, devlog]
previousLink: telegram-bot-file-storage-stream
nextLink: telegram-login-widget-dotnet-auth
---

> **Architecture First: Building a Jira Alternative Solo, AI-Assisted** — Part 12.
> The [previous article](telegram-bot-file-storage-stream) taught the bot to save single messages with media, but left a few threads open. A message can be edited — which should update the record that was already saved — or it can consist of several media at once, which should be saved into one issue. This article handles both cases.

Until now the save process assumed a message in the chat never changes after the user sends it. Because of that, issues on the board stayed exactly as they were first saved, even after the user edited them in the chat. On top of that, sending a group of images in one message created a separate card for each media element in the group, instead of merging all the media into one issue — a consequence of how Telegram delivers media groups.

## Handling the edited-message update

Start with the simpler case. When someone edits a message they already sent to the chat, the app receives an *edited message* update from Telegram and has to handle it as an `upsert` — `update` if the message was saved before, `insert` if not.

For that, the middleware from the [previous article](telegram-bot-file-storage-stream) ([`HandleAllMessagesMiddleware`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.TelegramHost/HandleAllMessagesMiddleware.cs)) allows reading two update types:

```csharp
private static readonly UpdateType[] AllowedUpdates =
[
    UpdateType.Message,
    UpdateType.EditedMessage,
];
```

The `Message` object is the same in both update types, so we read it like this:

```csharp
var message = context.Update.Message ?? context.Update.EditedMessage;
```

For the app there is no difference whether a message was edited or saved for the first time — its handling logic was built around `upsert` from the start: save if it was not there, update if it was. The reason is fault tolerance. When the system lags, any message can be processed more than once, and without `upsert` logic that would create phantom records. As a result, an edited photo is still mapped by `GetPhotoRequest`, an edited text by `GetMessageRequest`, and so on. The [middleware](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.TelegramHost/HandleAllMessagesMiddleware.cs) has no branch like "is this an edit or not"; it does the mapping, and the decision of how to handle the record — as new or existing — is delegated to the [save service](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.TelegramServices/Services/Messages/TelegramSaveMessageService.cs).

## Implementing upsert for a new or edited message

Messages from Telegram are processed one at a time, so the create-or-update logic works in terms of a single message. First, the [save service](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.TelegramServices/Services/Messages/TelegramSaveMessageService.cs) tries to find an existing record by its external identity — the Telegram message id plus the chat it came from. The pair matters, because a message id can repeat across different chats:

```csharp
var savedMessage = await context.TelegramMessages
    .Where(x => x.ExternalMessageId == request.ExternalMessageId)
    .Where(x => x.ExternalChatId == request.ExternalUserId)
    .Select(x => new
    {
        IssueId = x.Issue != null ? (long?)x.Issue.Id : null,
        x.Id,
    })
    .FirstOrDefaultAsync(cancellationToken);
```

What happens next depends on whether a card already exists for this message. If there is no saved message yet, or it has no issue attached, this is a new object: the save logic runs and a `MainMessageCreated` result is returned to the caller.

```csharp
if (savedMessage?.IssueId is null)
{
    var statusId = await GetStatusIdToSaveMessage(request.UserId, cancellationToken);

    await using var transaction = await context.Database
        .BeginTransactionAsync(cancellationToken);

    // create the TelegramMessage row if it does not exist,
    // then create the issue/card for it
    await coreIssuesService.Create(
        new CreateIssueRequest
        {
            CreatedAt = request.SentAt,
            Text = request.Text,
            TelegramMessageId = messageId,
            StatusId = statusId,
            UserId = request.UserId,
        }, cancellationToken);

    await transaction.CommitAsync(cancellationToken);

    return new GetOrCreateMessageResult
    {
        Result = Result.MainMessageCreated,
        TelegramMessageId = messageId
    };
}
```

If the message is found and has an issue attached, the request is an *edit* of an existing object — an update runs and a `MainMessageUpdated` result is returned:

```csharp
await context.Issues
    .Where(x => x.TelegramMessageId == savedMessage.Id)
    .ExecuteUpdateAsync(upd => upd
        .SetProperty(x => x.Content, request.Text),
        cancellationToken);

return new GetOrCreateMessageResult
{
    Result = Result.MainMessageUpdated,
    TelegramMessageId = savedMessage.Id,
};
```

`GetStatusIdToSaveMessage` resolves the status the new message is saved into; the details of how it is chosen belong to a later stage and are skipped here. The result of handling the message — `MainMessageCreated` or `MainMessageUpdated` — is what the user ends up seeing.

## A reaction instead of a status message

The feedback to the user is minimal: a reaction on their own message. [`TelegramMessageService`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.TelegramServices/Services/Messages/TelegramMessageService.cs) sets the reaction based on the save result:

```csharp
var result = await saveMessageService.Save(request, cancellationToken);

if (result.Result is Result.MainMessageCreated)
    await SetReaction(request, "👍", cancellationToken);
else if (result.Result is Result.MainMessageUpdated)
    await SetReaction(request, "❤", cancellationToken);
```

If a new object was created during handling, a 👍 is set; if an existing one was updated, a ❤. The reasoning behind interacting with the user this way is in [why users kept choosing Saved Messages](telegram-saved-messages-bot-lesson): the bot only confirms that it handled the message, without cluttering the chat with extra messages or buttons.

Setting the reaction is a single Bot API call:

```csharp
private async Task SetReaction(
    SaveMessageTelegramRequest request,
    string? reaction,
    CancellationToken ct)
{
    await client.SetMessageReaction(
        request.ExternalUserId,
        request.ExternalMessageId,
        reaction is not null
            ? [new ReactionTypeEmoji { Emoji = reaction }]
            : [],
        cancellationToken: ct);
}
```

There was one more small idea here: changing the reaction on *every* edit, so repeated edits would change the emoji too. As it stands, it is hard to tell whether a repeat edit was handled — the user always sees a ❤. Telegram currently gives no way to read a message's current reaction set, so doing this would mean storing the current emoji for each message ourselves — and we decided not to overcomplicate the system for such a rare case.

## Handling a Telegram media group

When a user sends several photos at once, Telegram does not send one message with several photos, as many expect. It sends *several separate updates* with the same **media group id**, arriving with no guaranteed order. Telegram leaves it to the app to assemble that group back into one object. The [`SaveGroupMessageEntity`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.TelegramServices/Services/Messages/TelegramSaveMessageService.cs) method is responsible for this.

To the user, a group of images is one message, and they expect to see one card with N attachments on the board. The [save service](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.TelegramServices/Services/Messages/TelegramSaveMessageService.cs) has a branch to handle the group case separately:

```csharp
private Task<GetOrCreateMessageResult> SaveMessageEntity(
    SaveMessageTelegramRequest request,
    CancellationToken cancellationToken)
{
    return request.MediaGroupId == null
        ? SaveSingleMessageEntity(request, cancellationToken)
        : SaveGroupMessageEntity(request, cancellationToken);
}
```

The common solution for saving groups, the one you find on forums, is a **timer-based implementation**: when a message with a media group id arrives, store it in memory and start (or reset) a timer tied to that group. When the timer expires, consider the group complete and save all the messages belonging to it. This approach has nothing to do with fault tolerance. The state is held in memory, and a server restart can lose part of the data for good.

Our implementation avoids the timer by relying on the database rather than RAM. We do not wait for the album to finish; we link the messages as they arrive. It rests on a few ideas.

First, the media group gets a local [identifier](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.DataAccess/Models/TelegramMediaGroup.cs) in the database. All the separate messages of one album are tied to that row through [`GetOrCreateTelegramMediaGroupId`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.TelegramServices/Services/Messages/TelegramSaveMessageService.cs):

```csharp
private async Task<long> GetOrCreateTelegramMediaGroupId(string groupId)
{
    var data = await context.TelegramMediaGroups
        .Where(x => x.ExternalId == groupId)
        .Select(x => new { x.Id })
        .FirstOrDefaultAsync(cancellationToken);

    if (data is not null)
        return data.Id;

    var group = new TelegramMediaGroup
    {
        ExternalId = groupId,
    };

    context.Add(group);
    await context.SaveChangesAsync(cancellationToken);

    return group.Id;
}
```

**Only the first message of the group creates the card.** The later messages of the group find the existing issue and attach their media to it. Because all of this lives in the database rather than an in-memory buffer, a server restart during group handling causes no problems — the parts already processed are saved, and the rest are added after the restart.

What is left are the non-standard cases — where you have to think carefully about whether to handle them at all, and if so, how. Those spots carry `TODO`s, their implementation deferred until they become real problems. One of the harder cases we did decide to handle: the first message of the group, the one that held the text content, is deleted, and the user adds the text to a different message in the group. The code allows updating the issue's text from any message in the media group, to support cases like this:

```csharp
// The case when first message was deleted and text added to the second
if (request.Text is not null && firstGroupMessageData is not null)
{
    // TODO - here we can detect and remove previous messages. But should we?
    await context.Issues
        .Where(x => x.Id == firstGroupMessageData.CardId)
        .ExecuteUpdateAsync(upd => upd
                .SetProperty(x => x.Content, request.Text),
            cancellationToken);
    // ...
}
```

## Why we do not handle deletions

To start with, Telegram does not deliver an update to the bot when a user deletes a message from their chat. There is simply no event for it, so we cannot know that a message was deleted in the Telegram chat. As a result, we can only delete issues from the web interface, not from the chat.

*Why* does Telegram not send this event at all? We think it is because of the ambiguity. What should happen when a user deletes their entire chat history with the bot? Should a delete arrive for every single message? It is hard to answer that unambiguously.

So we implemented deletion only in the web version of the app — and the chat with the bot serves as a kind of log of the history of interacting with it.

There is a small idea for the future: delete when the user puts a particular emoji on their message. Whether that is convenient is a separate question — a reaction would make deletion possible, but two-way communication with the bot through emoji looks awkward and unintuitive, so for now it stays just an idea.

## Conclusions

The bot now supports all the cases real users ran into: handling text and media, editing them, and handling message groups. The bot still just saves what it was sent and sets a reaction confirming successful handling, which now differs depending on whether it was a save or an edit. With this, the part of the product responsible for the bot's message handling is finished.

## What comes next

The bot is done, and all further functionality will be built in the web version — the organising mode, managing issues, their attributes. But before any of that, the web version needs authentication. The reason: users did not always find the Mini App convenient for working with boards, and asked for a web version — and to publish it, auth has to be set up first. The next article turns from the Telegram Mini App to the web app — we keep building a product closely tied to Telegram, but now able to work separately from it.