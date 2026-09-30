---
title: Saving media from a Telegram bot — store the preview, stream the original
description: Part 11 of building a Telegram task tracker solo. Teaching the capture bot to handle photos and video, and the storage decision behind it — keep small previews on disk, stream full files straight from Telegram on demand without buffering them in memory, using nginx range-request passthrough, instead of re-hosting everything.
type: article
createdAt: 2026-06-26 09:00
updatedAt: 2026-09-30
projects: [boards]
tags: [dotnet, aspnet-core, telegram-bot, file-storage, nginx, streaming, devlog]
previousLink: telegram-saved-messages-bot-lesson
nextLink: telegram-media-group-album-bot
---

> **Architecture First: Building a Jira Alternative Solo, AI-Assisted** — Part 11.
> The [previous article](telegram-saved-messages-bot-lesson) ended with a real user breaking the bot by sending it an image instead of a normal message. This article fixes that — we implement saving images, and decide how to store the files sent to the bot.

The previous article ended on a `500` in the logs: someone tried to save an image instead of a text message, and the bot, which until then only worked with text, could not process the request. That crash gave us the idea for the app's first real feature — saving not just text but photos and videos too. Handling and saving the photo object Telegram sends turned out to be not much harder than handling a text message. The decision that took real thinking was how to store the files without filling up the whole server.

## The save process has to stay as simple as possible

Saving an image has to work the same way as saving a text message did in the previous article: send a picture to the chat, and the bot saves it and reacts with a 👍. From the user's point of view, saving media and saving text are identical.

## Mapping Telegram messages into service DTOs

Handling media starts where the processing of all the bot's other messages starts — in [`HandleAllMessagesMiddleware`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.TelegramHost/HandleAllMessagesMiddleware.cs), introduced back in the [first backend iteration](clean-dotnet-telegram-bot-architecture). It now gains a new job — converting any Telegram update, including audio and video, into the set of change-requests the app accepts:

```csharp
SaveMessageTelegramRequest? request = message.Type switch
{
    MessageType.Text => GetMessageRequest(message),
    MessageType.Photo => GetPhotoRequest(message),
    MessageType.Video => GetVideoRequest(message),
    MessageType.Animation => GetAnimationRequest(message),
    _ => null
};

if (request is not null)
{
    await telegramMessageService.HandleSaveMessage(request, ct);
    context.SetExecutedRoute(
        new ExecutedRouteInfo("HandleAllMessagesMiddleware", text));
}
else
{
    await botClient.SendMessage(
        context.Update.GetUserId(),
        string.Format(Phrases.MessageTypeIsNotAvailable, message.Type),
        cancellationToken: ct);
}
```

This is where the crash described at the start gets fixed. We do it not by handling every possible Telegram update type, but by telling the user about the *unsupported* operation. If an incoming update has no mapping (`_ => null`), the bot no longer logs a `500`; it replies that this message type is not supported. Robust input handling is not always about supporting every possible case — it is about handling the cases you did not anticipate. Image handling is supported now, but a clear message about an unsupported format is maybe the more important change.

There is an architectural decision here: **the app defines its own set of media types and maps Telegram updates into them, rather than trying to mirror Telegram's contracts.** Telegram has separate update types for video and *animation* (GIF) — different messages with different fields. For the app that distinction is redundant: we treat animation the same as video. So both `GetAnimationRequest` and `GetVideoRequest` map into the same `SaveVideoMessageTelegramRequest`. The mapping layer is where Telegram's many message types get mapped into the ones the app defines. Because of it, at the service level we think about a GIF and an MP4 not as separate types but as one common thing — a video file. Defining your own contracts, instead of letting an external API's contracts spread through the whole system, is an important habit of an experienced developer. It draws a clear line between integration and service contracts, and keeps the number of changes to a minimum when those external contracts change.

The middleware passes the mapped request to `HandleSaveMessage` of the [Telegram request handling service](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.TelegramServices/Services/Messages/TelegramMessageService.cs). That service saves the received object through a `Save` call to a separate [Telegram request saving service](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.TelegramServices/Services/Messages/TelegramSaveMessageService.cs), and signals the result by setting a 👍 reaction on the received message. That reaction is the confirmation of saving — no text replies, no long dialogs, no buttons. Splitting handling from saving separates the data layer from the Telegram integration (the data layer saves and returns the result), while the Telegram-facing layer informs the user of the result.

## The architectural decision: store a copy of the file, or a reference to it?

Now the architectural decision at the heart of this article. When the bot receives a photo or video object, there are two options for how to store it.

**Store a copy.** Download the whole file from Telegram and save it to storage. This is simple to implement, but it can turn out expensive in financial terms: every file eats VPS disk space, the data volume keeps growing, and a video can weigh hundreds of megabytes. Storing that on a budget server, where it sits untouched most of the time, looks hard to justify.

**Store a reference to the object.** Files sent to the bot are stored in Telegram. That means we can load the object from Telegram at the exact moment it is requested. Telegram does have limits, though, that will not let us pull files endlessly.

In the save service we even found a developer's comment that became the thesis of this article:

> We can't make constant requests to Telegram — opening a board with a lot of media will hit the limits. And we can't always store files — they take up too much space.

So our architecture ended up somewhere between the two approaches: **download small previews, store references to the originals.** Here is how it works: when an image is saved, Telegram provides links to several files in different resolutions. We save two [`TelegramFile`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.DataAccess/Models/TelegramFile.cs) rows — a reference to the full-size file and one to the thumbnail. When saving the thumbnail reference, the file is downloaded into local storage. For video the situation is similar: Telegram sends links to its thumbnail and to the object itself, which we save by the same logic as for a photo.

The save implementation is in the `GetOrCreateMessageFileId` method:

```csharp
/// <param name="saveFileToStorage">
/// Store the file directly to storage.
/// If false - then when requesting the file it will be requested directly from TG.
/// We can't request always from tg - static content will make too many calls.
/// And we can't store content always - it takes too much space.
/// </param>
private async Task<Guid> GetOrCreateMessageFileId(
    File file,
    bool saveFileToStorage,
    CancellationToken cancellationToken)
{
    var oldFileData = await context.TelegramFiles
        .Where(x => x.FileUniqueId == file.FileUniqueId)
        .Select(x => new { x.Id })
        .FirstOrDefaultAsync(cancellationToken);

    if (oldFileData is not null)
        return oldFileData.Id;

    if (saveFileToStorage)
    {
        var botFile = await botClient.GetFile(file.FileId, cancellationToken);

        var stream = new MemoryStream();
        await botClient.DownloadFile(botFile, stream, cancellationToken);

        var extension = ExtensionUtility.GetExtension(file.MimeType);
        var filePath = ShardedPathStrategy.GetPath(botFile.FileUniqueId, extension);

        stream.Position = 0;
        await fileStorage.WriteFile(filePath, stream, null, cancellationToken);
    }

    var telegramFile = new TelegramFile
    {
        FileId = file.FileId,
        FileUniqueId = file.FileUniqueId,
        Name = file.FileName,
        Size = file.FileSize,
        MimeType = file.MimeType,
    };

    context.Add(telegramFile);
    await context.SaveChangesAsync(cancellationToken);
    return telegramFile.Id;
}
```

Writing to and reading from local storage goes through the [`IFileStorage`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.Services/FileStorage.cs) abstraction, whose options — `FileStorageOptions` — have a single property: the root directory to write files into:

```csharp
public interface IFileStorage
{
    Task<bool> FileExists(string path, CancellationToken cancellationToken = default);
    Task<FileStream> ReadFile(string path, CancellationToken cancellationToken = default);
    Task WriteFile(
        string path,
        Stream content,
        IDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);
}

public class FileStorageOptions
{
    public required string FilesDirectory { get; set; }
}
```

The implementation is a thin wrapper over the local disk: a file's local path is combined with `FilesDirectory` to get its physical location, `WriteFile` creates the directory if needed and copies the incoming stream to a file (via `CopyToAsync`, to avoid full buffering), and `ReadFile` opens the file as a stream. It is a simple interface, not tied to local disk. If we ever move to object storage like S3, only this class changes. For now the files will be stored in the cheapest storage we have — the filesystem of a rented VPS.

The model used to store references to Telegram files — [`TelegramFile`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.DataAccess/Models/TelegramFile.cs) — is worth a separate look:

```csharp
public class TelegramFile
{
    public Guid Id { get; set; }

    [MaxLength(255)]
    public required string FileId { get; set; }

    [MaxLength(64)]
    public required string FileUniqueId { get; set; }

    public long? Size { get; set; }

    [MaxLength(255)]
    public string? Name { get; set; }

    [MaxLength(32)]
    public required string? MimeType { get; set; }
}
```

`FileId` lets us request the file's content from Telegram; `FileUniqueId` is used for deduplication, to store identical content only once on local download. Forwarding the same image to the bot saves its thumbnail to local storage under the name `FileUniqueId` only the first time. The rest of the system refers to files by their `Guid`, not by Telegram-specific identifiers.

## How saved Telegram media is displayed

As mentioned, the different approaches to storing and reading media are needed because of the different ways we work with them.

**Loading a task board** can potentially trigger loading a large number of image and video previews. If loading each one required a request to Telegram, we could hit the API limits, or simply get slow file loading. But because the thumbnails are saved locally, they can be read directly from our storage — fast, and with no extra external calls.

Each issue card on the board returned to the frontend carries a set of references to media objects:

```csharp
public record IssueListDto : ICanContainMedia
{
    // ...
    public List<MediaInfo> Media { get; set; } = [];
}

public class MediaInfo
{
    public Guid? PreviewFileId { get; set; }
    public Guid? OriginalFileId { get; set; }
    public MediaType Type { get; set; }
}

public enum MediaType
{
    Photo,
    Video,
}
```

Each `MediaInfo` object holds two references — `PreviewFileId` and `OriginalFileId`, both pointing at `TelegramFile` rows by their `Guid`. The frontend uses the preview reference to show the thumbnail on the board, with code like `<img :src = BackendHost + '/api/telegram-files/' + PreviewFileId>` (the backend returns a stream of the file from local disk in the [`GetFileById`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.WebApiHost/Controllers/TelegramFilesController.cs) method). If the user clicks a preview, the backend returns a stream of the original file fetched through the Telegram API, and the frontend renders it (or plays it, for video) in the media-viewer component.

Note that the `Media` list on `IssueListDto` is not requested directly from the database — it is *enriched* with media after the issues themselves are fetched from the DB. This is a deliberate architectural choice. Pseudocode of the enrichment from the [original](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.WebApiServices/IssuesService.cs) file:

```csharp
var issues = await GetIssues(request);
await EnrichMedia(issues);
```

This approach cuts down the number of joins in the issues query by loading file information in a separate method. The whole backend runs on the principle "several simple queries are better than one complex one" — because it scales much more easily and cheaply than the database does.

**Opening the original of an item** happens when a photo or video preview is clicked. As with the thumbnails, the frontend requests the backend's [`GetFileById`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.WebApiHost/Controllers/TelegramFilesController.cs) method, passing `OriginalFileId`, and gets back a stream with the file. For the original file, though, the file is not found on disk and is requested from Telegram.

Here it is worth mentioning the limits of the Bot API methods `GetFile`/`DownloadFile` used above to *save* thumbnails. `DownloadFile` can download files no larger than 20 MB. To avoid hitting this limit with large files, the controller serves the file content through Telegram's **direct file URL** — `https://api.telegram.org/file/bot{botToken}/{filePath}`. This URL points at an address in Telegram's file storage and has no such limit.

## Streaming the original file from Telegram without buffering it in memory

Potentially returning large videos from Telegram to the user raises one more problem: the server cannot load the whole file into memory before sending the data. If several users open a video weighing a few hundred megabytes at the same time, the VPS runs out of RAM and the server starts throwing `OutOfMemoryException`. Not to mention that video playback would only start after the file has been fully transferred. The right solution in cases like this is *streaming* — the byte stream of the video file is sent from Telegram through the backend to the client, without being buffered on the server as a whole.

To enable file streaming, we need to adjust the nginx server configuration, in the `location` block serving the file route:

```nginx
location ^~ /api/notes-board/telegram-files {
    set $upstream http://structuredmessageswebapihost:5007;
    rewrite ^/api/notes-board/(.*)$ /api/$1 break;
    proxy_pass              $upstream;
    proxy_buffering         off;
    proxy_cache             off;
    proxy_set_header        Range $http_range;
    proxy_set_header        If-Range $http_if_range;
    proxy_pass_header       Content-Range;
    proxy_pass_header       Accept-Ranges;
    gzip                    off;
    proxy_read_timeout      10m;
    proxy_send_timeout      10m;
    proxy_next_upstream error timeout http_502 http_503 http_504;
    proxy_next_upstream_tries 5;
    proxy_next_upstream_timeout 30s;
}
```

The important lines are the ones that turn nginx from a buffering proxy into a pass-through pipe. `proxy_buffering off` tells nginx not to accumulate the upstream response before forwarding it — bytes are passed along as they arrive, so neither nginx nor the backend ever holds the whole file. The `Range` and `If-Range` headers are forwarded upstream, and `Content-Range` and `Accept-Ranges` are passed back, which is what makes **range requests** work: a video player can ask for just the slice of the file it needs to start playing, and seek to the middle without downloading everything before it. That is why a saved video can begin playing almost immediately and scrub smoothly, rather than downloading in full first. The long `proxy_read_timeout` and `proxy_send_timeout` keep slow, large transfers from being cut off mid-stream, and `gzip off` avoids wasting effort trying to compress already-compressed media.

nginx can only pass those range headers through because the backend produces them in the first place. The [`TelegramFilesController`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.WebApiHost/Controllers/TelegramFilesController.cs) does not implement range logic itself — it forwards the browser's `Range` header to Telegram and then relays Telegram's response back faithfully, including the status code and the headers that describe the byte range:

```csharp
Response.StatusCode = (int)telegramResponse.StatusCode;
Response.Headers.Append("Accept-Ranges", "bytes");

// Content-Length comes from content headers
if (telegramResponse.Content.Headers.ContentLength is { } contentLength)
    Response.Headers.ContentLength = contentLength;

// Content-Range is also a content header on 206 responses
if (telegramResponse.Content.Headers.ContentRange is { } contentRange)
    Response.Headers.Append("Content-Range", contentRange.ToString());

var stream = await telegramResponse.Content.ReadAsStreamAsync(cancellationToken);

// enableRangeProcessing: false — we've already handled the range manually
// by forwarding it to Telegram. ASP.NET Core must not try to slice again.
return File(stream, mimeType, enableRangeProcessing: false);
```

Two details make this correct. First, when the browser requests a range, Telegram answers with a `206 Partial Content` and a `Content-Range` header describing which slice it sent — and the controller passes both straight through, so the status code and range information the browser receives are exactly the ones Telegram produced. Second, and easy to get wrong: `File(stream, mimeType, enableRangeProcessing: false)`. ASP.NET Core's `File` result can do range processing on its own, but here it must *not* — the range was already handled by forwarding it to Telegram, and the stream coming back is already the correct slice. Leaving range processing on would make ASP.NET Core try to slice an already-sliced stream and corrupt the response. Turning it off tells the framework to stream the bytes as is. The controller is a faithful relay, not a second range processor — and because it reads Telegram's response as a stream and returns it directly, the bytes flow through without the file being buffered whole on the backend either.

The result: a large original streams through the whole chain — Telegram, backend, nginx, browser — without any link buffering the file whole. The previews made the board cheap to load; this makes the originals cheap to serve, even when they are large.

## How the frontend uses the preview and original references to display media

The frontend works the same way with both `previewFileId` and `originalFileId`. To it they are file identifiers — given them, it can build links for direct access. For that the frontend uses [`getImageUrl(guid)`](https://github.com/Laraue/laraue-boards/blob/master/app/composables/utils.ts), which returns a download link for the file through [`TelegramFilesController`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.WebApiHost/Controllers/TelegramFilesController.cs). The frontend has no idea whether the file is on the server or will be loaded from Telegram; it requests the file by its `Guid`, and the rest is the backend's concern.

A card on the board uses only previews. The [`LnbCard`](https://github.com/Laraue/laraue-boards/blob/master/app/components/LnbCard.vue) component renders each issue's media as a set of thumbnails, capping the count at 4 and adding "+N" if there are more. Each thumbnail's `src` is rendered as `getImageUrl(mediaInfo.previewFileId)`. If the media type is video, the preview gets a play-icon overlay. As a result, rendering a board full of media cards makes not a single request to Telegram, because all the thumbnails are stored locally.

Clicking a thumbnail calls into shared state through `openMedia(media, index)`. This method saves the media list and the index of the opened item into the state, and [`LnbMediaViewer`](https://github.com/Laraue/laraue-boards/blob/master/app/pages/organizations/%5BorgKey%5D.vue) renders the opened item. To load the original, `originalFileId` is used: the image and video elements request it through `src`. The video element uses a few extra attributes: `controls`, `playsinline`, `poster` with `previewFileId` — to show the thumbnail while the video loads — and `preload="metadata"`, which makes the browser download only what is needed to start playback. Starting playback of the `<video>` element kicks off the streaming process described earlier.

## Working with local storage files from the application containers

For applications in containers to have access to a server folder that lives independently of the containers, you need to configure a volume in the `docker-compose.yaml` file presented in the [deploy article](deploying-dotnet-postgres-vps-docker-compose). The files will be stored in the `/home/laraue/storage` folder of the VPS. Destroying the containers will not affect these files in any way.

In the nginx section of `docker-compose.yaml`, access to the server's `storage` folder is passed in:

```yaml
nginx:
  volumes:
    - storage:/home/laraue/storage
```

In the same file, the `FilesDirectory` environment variable from the `FileStorageOptions` shown earlier is set:

```yaml
structuredmessageswebapihost:
  environment:
    FileStorageOptions__FilesDirectory: "/home/storage/note-board"
```

Now any files we decide to save in the app will end up in the physical folder `/home/laraue/storage` on the server.

## Conclusions

The crash described at the start of the article is gone. While fixing it, the bot learned to handle photos, videos, and GIFs as simply as text. Implementing this visually small change took thinking through a lot of details. Small previews are stored locally, so a board full of pictures loads fast from our own disk; full originals stay on Telegram's servers and are streamed on demand, which saves us on storing large files; range-based file streaming is implemented, so files are not buffered in the server's memory.

## What comes next

There is a gap left in the save logic: what happens when someone *edits* a message they already sent? The handling code actually already accounts for it. But the whole story of updating a previously saved message — and the difficulties Telegram introduces around edited and grouped messages — is worth its own article. That is exactly what we will cover next.