---
title: Объединение группы изображений в одну запись ботом в Telegram — обрабатываем медиагруппы и правки без использования таймеров
description: Часть 12 цикла о разработке Telegram-таск-трекера в одиночку. Telegram присылает альбом изображений отдельными сообщений, а правки — новыми апдейтами. Разбираем, как собрать медиагруппу в одну запись без привычного таймера-аккумулятора и как трактовать правку как обновление, а не дубль.
type: article
createdAt: 2026-06-26 15:00
updatedAt: 2026-09-30 07:44
projects: [boards]
tags: [dotnet, telegram-bot, media-groups, devlog]
previousLink: telegram-bot-file-storage-stream
nextLink: telegram-login-widget-dotnet-auth
---

> **Architecture First: как в одиночку с ИИ сделать альтернативу Jira** — Часть 12.
> В [Предыдущей статье](telegram-bot-file-storage-stream) мы обучили бота сохранять одиночные сообщения с медиафайлами, но оставили несколько незакрытых тем. Сообщение может редактироваться - что должно приводить к обновлению раннее добавленной записи, или состоять из набора медиа - такое должно корректно сохраняться в issue. В этой статье разбираем обработку этих двух кейсов.

Ранее процесс сохранения сообщения предполагал, что сообщение в чате никогда не меняется пользователем. Из-за этого issues на доске всегда оставались в том виде, в котором их сохранили, несмотря на уже сделанные пользователем изменения в чате. Кроме того, отправка группы изображений в одном сообщении, приводила к созданию карточки для каждого медиа-элемента из группы, вместо объединения всех медиа в одном issue (это связано с особенностями обработки группы медиа-объектов в Telegram).

## Обработка апдейта с редактированием сообщения

Начнём с простого. Когда кто-то правит сообщение, ранее отправленное в чат, приложение получит от Telegram апдейт *отредактированного сообщения* и должно обработать его как `upsert` (`update`, если сообщение было сохранено ранее, `insert` - если нет).

Для этого в Middleware из [прошлой статьи](telegram-bot-file-storage-stream) ([`HandleAllMessagesMiddleware`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.TelegramHost/HandleAllMessagesMiddleware.cs)), разрешается чтение двух типов апдейтов:

```csharp
private static readonly UpdateType[] AllowedUpdates =
[
    UpdateType.Message,
    UpdateType.EditedMessage,
];
```

Объект `Message` в двух типах сообщения одинаков, мы используем следующую конструкцию для его получения:

```csharp
var message = context.Update.Message ?? context.Update.EditedMessage;
```

Для приложения нет никакой разницы, было сообщение отредактировано или сохраняется впервые - его логика обработки и так была построена на `upsert` — сохранить, если не было; обновить, если было. Причина этого - стремление к отказоустойчивости. При лагах в системе, любое сообщение может обработаться несколько раз и без `upsert` логики могли бы появляться фантомные записи. Как результат, отредактированное фото всё так же маппится через `GetPhotoRequest`, отредактированный текст — через `GetMessageRequest`, и так далее. [Middleware](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.TelegramHost/HandleAllMessagesMiddleware.cs) не содержит ветвлений вида «правка это или нет»; он занимается маппингом, а разбор как обрабатывать запись, как новую или старую, делегируется в [save-сервис](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.TelegramServices/Services/Messages/TelegramSaveMessageService.cs).

## Реализация upsert логики для обработки нового или редактирования старого сообщения

Сообщения от Telegram обрабатываются последовательно, поэтому логика «создание или обновления» работает в разрезе одного сообщения. Для начала [save-сервис](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.TelegramServices/Services/Messages/TelegramSaveMessageService.cs) пытается найти существующую запись по внешнему идентификатору — ID сообщения Telegram + чат, из которого оно пришло. Важно использовать именно связку идентификаторов, так как ID сообщения в разных чатах может повторяться:

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

Дальнейшая логика зависит от того, существует ли уже карточка для этого сообщения. Если сохранённого сообщения ещё нет или к нему не привязан issue — это обработка нового объекта: выполняется логика сохранения и в вызывающий код возвращается результат `MainMessageCreated`.

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

Если же сообщение найдено и к нему привязан issue — запрос является *правкой* старого объекта - выполняется обновление и возвращается результат `MainMessageUpdated`:

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

`GetStatusIdToSaveMessage` определяет статус, в котором сохранится новое сообщение; детали того, как он определяется, относятся к более поздним этапам и здесь опущены. Результат обработки сообщения — `MainMessageCreated` или `MainMessageUpdated` — в конечном счете и увидит пользователь.

## Реакция вместо сообщения о результате обработки

Фидбэк пользователю минимальный: это реакция на его собственное сообщение. [`TelegramMessageService`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.TelegramServices/Services/Messages/TelegramMessageService.cs) выставляет реакцию в зависимости от результата сохранения:

```csharp
var result = await saveMessageService.Save(request, cancellationToken);

if (result.Result is Result.MainMessageCreated)
    await SetReaction(request, "👍", cancellationToken);
else if (result.Result is Result.MainMessageUpdated)
    await SetReaction(request, "❤", cancellationToken);
```

Если при обработке был создан новый объект ставится реакция — 👍; обновлен старый — ❤. Логику, почему мы решили взаимодействовать с пользователем именно таким образом можно найти в [почему пользователи снова и снова выбирали «Избранное»](telegram-saved-messages-bot-lesson): бот только подтверждает обработку, но не захламляет чат лишними сообщениями или кнопками.

Выставление реакции — это один вызов Bot API:

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

Здесь была еще одна небольшая идея: менять реакцию на *каждую* правку, чтобы повторные изменения тоже меняли эмодзи. Сейчас тяжело понять, была ли обработана повторная правка - пользователь всегда видит ❤. На данный момент Telegram не даёт возможности прочитать текущий набор реакций сообщения, так что для этого пришлось бы хранить текущий эмодзи каждого сообщения самим — и было решено не переусложнять систему для такого редкого кейса.

## Обработка группы медиа Telegram

Когда пользователь шлёт несколько фото разом, Telegram не отправляет одно сообщение с несколькими фото, как этого многие ожидают. Он отправляет *несколько отдельных апдейтов*, с одинаковым **media group id**, приходящих без гарантированного порядка. Собирать эту группу обратно в один объект Telegram предлагает приложениям самостоятельно. За это в коде отвечает метод [`SaveGroupMessageEntity`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.TelegramServices/Services/Messages/TelegramSaveMessageService.cs).

Для пользователя группа изображений — это одно сообщение. И он ожидает увидеть на доске карточку с N вложениями. [Save сервис](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.TelegramServices/Services/Messages/TelegramSaveMessageService.cs) имеет ветвление, чтобы обрабатывать групповые случаи отдельно:

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

Распространённое решение сохранения групп, которое можно увидеть на форумах, — **реализация на таймере**: при получении сообщения с media group id, его сохраняют в памяти, запускают (или обновляют) таймер, связанный с этой группой. Если таймер истек - группу считают завершённой и выполняют сохранение всех сообщений, относящихся к ней. Такой подход не имеет ничего общего с отказоустойчивостью. Состояние хранится в памяти, и перезапуск сервера может привести к потере части данных навсегда.

Наша реализация избегает таймера, опираясь на использование базы, а не оперативной памяти. Мы не ждем, когда альбом закончится, а связываем сообщения по мере их появления. В основе — несколько идей. 

Во-первых, медиагруппа получает локальный [идентификатор](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.DataAccess/Models/TelegramMediaGroup.cs) в базе. Все отдельные сообщения одного альбома связываются с этой строкой с помощью [`GetOrCreateTelegramMediaGroupId`](https://github.com/Laraue/Laraue.Apps.Boards/blob/1876814afe9fdef5fdcfb4468e581c55bb379550/src/Laraue.Apps.Boards.TelegramServices/Services/Messages/TelegramSaveMessageService.cs):
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

**Только первое сообщение группы создаёт карточку.** Последующие сообщения группы находят существующий issue и привязывают к нему свои медиа. Поскольку всё это живёт в базе, а не в буфере в памяти, перезапуск сервера во время обработки группы не приводит к проблемам — уже обработанные части сохранены, остальные будут добавлены после перезапуска.

Пропущенными остаются нестандартные случаи — где нужно тщательно думать, стоит ли вообще делать их обработку. А если делать - то как. В таких местах можно найти `TODO` - их реализация отложена до появления настоящих проблем. Один из сложных случаев, что мы все же решили обработать: первое сообщение группы, содержавшее текстовое содержимое, — удаляется, а текст пользователь добавляет к другому сообщению из группы. Код предусматривает обновление текста issue из любого сообщения группы медиа, чтобы поддерживать такие кейсы:

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

## Почему мы не обрабатываем удаления

Начнем с того, что Telegram не доставляет боту апдейт, когда пользователь удаляет сообщение из своего чата. События для этого просто не предусмотрено, — поэтому мы не можем узнать, что сообщение было удалено в чате Telegram. В результате мы можем удалять issues только из веб-интерфейса, но не из чата.

*Почему* Telegram вообще не отправляет это событие? Мы думаем, из-за неоднозначности. Что должно произойти, когда пользователь удаляет всю историю чата с ботом? Должны ли прийти удаления по каждому отдельному сообщению? Тяжело ответить однозначно.

Поэтому мы реализовали удаление только в веб-версии приложении — а чат с ботом является некоторым логом с историей взаимодействия с ним.

Есть небольшая идея на будущее: делать удаление, если пользователь ставит на свое сообщение определенное эмодзи. Удобно ли это — отдельный вопрос: реакция даст возможность делать удаление, но двустороннее общения с ботом с помощью эмодзи выглядит неуклюже и неинтуитивно, — так что пока это остаётся только идеей.

## Итоги

Бот теперь поддерживает все кейсы, с которыми столкнулись реальные пользователи: обработка текста и медиа, их редактирование, обработка групп сообщений. Бот по-прежнему просто сохраняет то, что ему отправили и ставит реакцию об успешной обработке, которая теперь разная в зависимости от сохранения или правки. На этом часть продукта отвечающая за обработку сообщений ботом завершена.

## Что дальше

Бот закончен, и вся дальнейшая функциональность будет разрабатываться в веб-версии — режим организации, управление issues, их атрибуты. Но перед тем, как заниматься всем этим, необходимо добавить авторизацию в веб-версию. Дело в том, что пользователям оказалось не всегда удобно использовать Mini App для работы с досками. И они попросили добавить для них веб-версию, для публикации которой необходимо сначала настроить авторизацию. В следующей статья происходит разворот от Mini App Telegram к веб-приложению — мы продолжим строить продукт, тесно связанный с Telegram, но уже способный работать отдельно от него.