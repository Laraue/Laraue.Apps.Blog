---
title: Сохраняем медиа из Telegram-бота — храним превью, стримим оригинал
description: Часть 11 цикла о разработке Telegram-таск-трекера в одиночку. Учим бота работать с фото и видео, разбираем решение с хранением файлов - маленькие превью в локальном хранилище и прямой стриминг больших файлов прямиком из Telegram, без буферизации в памяти.
type: article
createdAt: 2026-06-26 09:00
updatedAt: 2026-09-30
projects: [boards]
tags: [dotnet, aspnet-core, telegram-bot, file-storage, nginx, streaming, devlog]
previousLink: telegram-saved-messages-bot-lesson
nextLink: telegram-media-group-album-bot
---

> **Architecture First: как в одиночку с ИИ сделать альтернативу Jira** — Часть 11.
> [Предыдущая статья](telegram-saved-messages-bot-lesson) закончилась тем, что реальный пользователь сломал нашего бота, отправив ему картинку, вместо обычного сообщения. Эта статья посвящена исправлению этой проблемы — реализуем сохранение изображения и принимаем решение о том, как хранить файлы, отправленные боту.

Предыдущая статья закончилась нахождением ошибки `500` в логах: кто-то попробовал сохранить картинку вместо текстового сообщения, и бот, работавший до этого только с текстовыми сообщениями, не смог обработать запрос этого пользователя. Этот сбой и дал нам идею для первой фичи приложения - сохранению не только текстовых, но также фото и видеофайлов. Обработка и сохранение объекта с фото, которое присылает Telegram — оказалась ненамного сложнее обработки объекта текстового сообщения. А вот над решением, как хранить файлы, чтобы не занять все место на сервере, пришлось долго думать.

## Процесс сохранения должен быть максимально простым

Процесс сохранения изображения должен работать аналогично сохранению текстового сообщения в прошлой статье: при отправке картинки в чат — бот сохраняет ее и реагирует проставлением 👍. С точки зрения пользователя, сохранение медиа и текста абсолютно идентично.

## Маппинг сообщений Telegram в сервисные DTO

Работа с медиа начинается там же, где и процессинг всех остальных сообщений бота, — в [`HandleAllMessagesMiddleware`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.TelegramHost/HandleAllMessagesMiddleware.cs) представленном ещё в [первой итерации бэкенда](clean-dotnet-telegram-bot-architecture). Теперь у него появляются новые функции — преобразование любых апдейтов от Telegram с аудио и видео в набор запросов на изменение, которые принимает приложение:

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

Именно здесь чинится сбой, описанный вначале. Мы реализуем это не за счёт обработки всех возможных типов апдейтов Telegram, а за счёт сообщению пользователя о *неподдерживаемой* операции. Если у пришедшего апдейта нет маппинга (`_ => null`), бот больше не логирует ошибку `500`, а отвечает пользователю, что этот тип сообщения не поддерживается. Надёжная обработка ввода — это не всегда про поддержку всех возможных случаев, а про грамотную обработку не предусмотренных ситуаций. Теперь обработка изображений поддерживается приложением, но понятное сообщение о неподдерживаемом формате — возможно, даже более важное изменение.

Здесь принимается архитектурное решение: **приложение определяет собственный набор медиа и маппит апдейты Telegram в них, а не пытается повторить контракты Telegram.** Telegram имеет разные типы апдейтов для видео и *анимации* (GIF) — это разные сообщения с разным набором полей. Для приложения это деление избыточно: мы работаем с анимацией также, как с видео. Поэтому и `GetAnimationRequest` и `GetVideoRequest` мапятся в один и тот же `SaveVideoMessageTelegramRequest`. Слой маппинга — это место, где сообщения Telegram разных типов маппятся в определенные приложением. Благодаря этому, на уровне сервисов мы будем думать об обработке GIF и MP4 не как об отдельных типах, а как об общем - видеофайле. Определять собственные контракты, вместо того чтобы позволять контрактам внешнего API расползаться по всей системе - важная привычка опытного разработчика. Она позволяет четко разграничить интеграционные и сервисные контракты, и свести к минимуму количество правок в случае их изменения.

Middleware передаёт смаппленный запрос в `HandleSaveMessage` сервиса [обработки Telegram запросов](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.TelegramServices/Services/Messages/TelegramMessageService.cs). Тот сохраняет полученный объект с помощью вызова `Save` отдельного сервиса [сохранения Telegram запросов](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.TelegramServices/Services/Messages/TelegramSaveMessageService.cs) и сигнализирует о результате выполнения проставлением реакции 👍 на полученное сообщение. Эта реакция и является подтверждением о сохранении — никаких текстовых ответов, длинных диалогов или кнопок. Разделение на обработку и сохранение отделяет слой данных от интеграции с Telegram (слой данных выполняет сохранение и возвращает результат), а слой, работающий с Telegram информирует пользователя о результате.

## Архитектурное решение: хранить копию медиафайла или ссылку на него?

Теперь об архитектурном решении, лежащим в основе статьи. Когда бот получает объект фото или видео, есть два варианта того, как их хранить.

**Хранить копию.** Скачать целиком файл из Telegram и сохранить его в хранилище. Это простой в реализации подход, но он может оказаться дорогим с финансовой точки зрения: каждый файл съедает объем диска на VPS, объем данных постоянно растёт, а видео может весить сотни мегабайт. Хранить такое на бюджетном сервере, чтобы оно большую часть времени лежало нетронутым - выглядит неоправданно.

**Хранить ссылку на объект.** Отправленные боту файлы хранятся в Telegram. А значит, мы можем загрузить объект из Telegram именно в тот момент, когда его запросили. Однако, у Telegram есть лимиты, которые не позволят грузить файлы бесконечно.

В save-сервисе мы даже нашли комментарий разработчика, ставший тезисом к данной статье:

> Мы не можем делать постоянные запросы к Telegram — открытие доски с большим количеством медиа упрется в лимиты. И мы не можем хранить всегда файлы — они занимают слишком много места.

В итоге, наша архитектура оказалась чем-то средним между двумя подходами: **загружаем маленькие превью, храним ссылки на оригиналы.** Как это работает: при сохранении изображения Telegram предоставляет ссылки на несколько файлов в разных разрешениях. Мы сохраняем в базу две записи [`TelegramFile`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.DataAccess/Models/TelegramFile.cs) - ссылки на полноразмерный файл и на миниатюру. При сохранении ссылки на миниатюру осуществляется скачивание файла в локальное хранилище. Для видео - ситуация похожа, Telegram присылает ссылки на его миниатюру и сам объект, которые мы сохраняем по той же логике, что и для фото.

Реализацию сохранения можно посмотреть в методе `GetOrCreateMessageFileId`:

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
Запись и чтение в локальное хранилище выполняется через абстракцию [`IFileStorage`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.Services/FileStorage.cs), ее настройки - `FileStorageOptions` имеют лишь одно свойство, — корневая директория, для записи файлов:

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

Реализация — тонкая обёртка над локальным диском: локальный путь файла комбинируется с `FilesDirectory`, чтобы получить его физическое расположение, `WriteFile` при необходимости создаёт директорию и копирует входящий стрим в файл (через `CopyToAsync`, чтобы избежать полной буферизации), а `ReadFile` открывает файл как стрим. Это простой интерфейс, не привязанный к локальному диску. В случае переезда в объектное хранилище вроде S3, поменяется только этот класс. Пока же файлы будут храниться в самом дешёвом для нас хранилище, в файловой системе арендованного VPS.

Стоит отдельно упомянуть модель — [`TelegramFile`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.DataAccess/Models/TelegramFile.cs), используемую для хранения ссылок на файлы в Telegram:
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

`FileId` позволяет запросить контент файла в Telegram, `FileUniqueId` используется для дедупликации (чтобы хранить идентичные контенты при локальной загрузке только один раз). Пересылка боту одной и той же картинки приведет к сохранению ее миниатюры в локальное хранилище под названием `FileUniqueId` только в первый раз. Вся остальная система ссылается на файлы по их `Guid`, а не по специфичным для Telegram идентификаторам.

## Как отображаются сохраненные из Telegram медиафайлы

Как упоминалось ранее, разные подходы хранения медиа и их чтения нужны из-за разного характера работы с ними.

**Загрузка доски с задачами** потенциально может вызвать загрузку большого количество превью изображений и видео. Если бы загрузка каждого из них потребовала запроса в Telegram, мы бы могли упереться в ограничения API, или просто получить медленную загрузку файлов. Но, поскольку миниатюры сохранены локально, они могут читаться напрямую с нашего хранилища — быстро и без дополнительных внешних вызовов.

Каждая карточка с issue на доске, возвращаемая на фронтенд, имеет набор ссылок на медиа-объекты:

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

Каждый медиа объект `MediaInfo` содержит две ссылки — `PreviewFileId` и `OriginalFileId`, обе указывают на строки `TelegramFile` по их `Guid`. Фронтенд использует ссылки на превью, чтобы показать миниатюры на доске, используя код вида `<img :src = BackendHost + '/api/telegram-files/' + PreviewFileId>` (бэкенд вернет стрим файла с локального диска в методе [`GetFileById`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.WebApiHost/Controllers/TelegramFilesController.cs)). Если пользователь нажмет на превью, бэкенд отправит стрим оригинального файла, полученный через Telegram API и фронтенд отрисует (воспроизведет в случае видео) объект в компоненте просмотра медиа.

Отметим, что список `Media` у `IssueListDto` не запрашивается напрямую из БД, а *обогащается* медиафайлами уже после их получения из БД. Это является намеренным архитектурным решением. Псевдокод обогащения из [оригинального](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.WebApiServices/IssuesService.cs) файла:
```csharp
var issues = await GetIssues(request);
await EnrichMedia(issues);
```

Такой подход позволяет сократить количество джойнов при запросе issues, реализовав загрузку из БД информации о файлах отдельным методом. На принципе "Лучше несколько простых, чем один сложный запрос" и работает весь наш бэкенд (ведь он масштабируется намного проще и дешевле, чем БД).

**Открытие оригинала элемента** — происходит по нажатию превью фото или видео. Фронтенд, как и в случае с миниатюрами, запрашивает метод бэкенда [`GetFileById`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.WebApiHost/Controllers/TelegramFilesController.cs), передавая `OriginalFileId` и получает в стрим с файлом. Однако, в случае с оригинальным файлом, файл не будет найден на диске и будет запрошен у Telegram.

Здесь стоит упомянуть лимиты методов Bot API `GetFile`/`DownloadFile` — что выше использовались для *сохранения* миниатюр. `DownloadFile` позволяет скачивать файлы размером не более 20 МБ. Чтобы не столкнуться с этим ограничением при работе с большими файлами, контроллер отдаёт содержимое файла через **прямой файловый URL** Telegram - `https://api.telegram.org/file/bot{botToken}/{filePath}`. Этот URL указывает адрес в файловом хранилище Telegram и не имеет таких ограничений.

## Возвращаем стрим оригинала файла из Telegram, не буферизируя его в памяти

Потенциальное возвращение пользователю видео больших размеров из Telegram поднимает ещё одну проблему: сервер не может грузить весь файл в память перед отправкой данных. Если несколько пользователей одновременно откроют видео, размером в несколько сотен мегабайт, RAM на VPS закончится и сервер начнет выбрасывать `OutOfMemoryException`. Не говоря уже о том, что воспроизведение видео начнется только после его полной передачи. Правильное решение в таких случаях - *стриминг* — поток байтов видеофайла отправляется из Telegram через бэкенд к клиенту, не буферизуясь на сервере целиком.

Чтобы включить поддержку стриминга файла, нужно доработать конфигурацию сервера nginx, в блоке `location`, обслуживающем файловый роут:

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

Важными для `стриминга` строками здесь являются `proxy_buffering off` - сигнализируем nginx не накапливать ответ апстрима перед пересылкой — байты передаются по мере поступления, nginx не хранит файл в памяти целиком. Заголовки `Range` и `If-Range` пробрасываются в апстрим, а `Content-Range` и `Accept-Ranges` отдаются обратно — это и заставляет работать **range-запросы**. Видеоплеер может попросить только ту часть файла, что нужна ему для начала воспроизведения, или запросить участок в середине файла, не скачивая всё, что было перед ним. Поэтому запрошенное видео может начать воспроизводится почти мгновенно и подгружаться по мере необходимости, а не скачиваться целиком. Большие значения `proxy_read_timeout` и `proxy_send_timeout` не дают длинным передачам данных оборваться на полпути, а `gzip off` не дает nginx тратить процессорное время на попытки сжать уже и так сжатое медиа.

Для того чтобы nginx мог пробросить range-заголовки, бэкенд их должен предоставить. [`TelegramFilesController`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.WebApiHost/Controllers/TelegramFilesController.cs) не реализует range-логику сам — он пробрасывает заголовок `Range` браузера в Telegram, а затем ретранслирует ответ Telegram обратно, включая статус-код и заголовки, описывающие байтовый диапазон:

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

Две детали делают это правильным. Первая: когда браузер запрашивает диапазон, Telegram отвечает `206 Partial Content` и заголовком `Content-Range`, описывающим, какой кусок он отдал, — и контроллер пробрасывает и то и другое напрямую, так что статус-код и информация о диапазоне, которые получает браузер, — ровно те, что произвёл сам Telegram. Вторая, и её легко завалить: `File(stream, mimeType, enableRangeProcessing: false)`. Результат `File` в ASP.NET Core умеет сам обрабатывать диапазоны, но здесь он этого делать *не должен* — диапазон уже обработан пробросом в Telegram, и приходящий обратно стрим уже является правильным куском. Оставить обработку диапазонов включённой значило бы заставить ASP.NET Core попытаться нарезать уже нарезанный стрим, испортив ответ. Выключив её, мы говорим фреймворку стримить байты как есть. Контроллер — верный ретранслятор, а не второй обработчик диапазонов, — и поскольку он читает ответ Telegram как стрим и возвращает его напрямую, байты текут насквозь, ни разу не буферизуясь целиком и на бэкенде.

Итог: большой оригинал стримится через всю цепочку — Telegram, бэкенд, nginx, браузер — без того, чтобы хоть одно звено в ней буферизовало файл целиком. Превью сделали доску дешёвой в загрузке; это делает оригиналы дешёвыми в отдаче, даже когда они большие.

## Как фронтенд использует ссылки на превью и оригинал для отображения медиа

Фронтенд одинаково работает с идентификаторами `previewFileId`, и `originalFileId`. Для него это идентификаторы файлов, зная которые можно получить ссылки для прямого доступа. Для этого фронтенд использует — [`getImageUrl(guid)`](https://github.com/Laraue/laraue-boards/blob/master/app/composables/utils.ts), — который возвращает ссылку на загрузку файла через [`TelegramFilesController`](https://github.com/Laraue/Laraue.Apps.Boards/blob/main/src/Laraue.Apps.Boards.WebApiHost/Controllers/TelegramFilesController.cs). Фронтенд не имеет понятия, находится ли файл на сервере или будет загружен из Telegram; он запрашивает файл по его `Guid`, остальное - забота бэкенда.

В карточке на доске используются только превью. Компонент [`LnbCard`](https://github.com/Laraue/laraue-boards/blob/master/app/components/LnbCard.vue) отрисовывает медиа каждой issue набором миниатюр, ограничивая максимальное количество 4 элементами и добавлением «+N», если элементов больше. `src` каждой миниатюры рисуется как `getImageUrl(mediaInfo.previewFileId)`. Если тип медиа = видео, то превью получает оверлей с иконкой play. Как результат, отрисовка доски, полной карточек с медиа, не сделает ни одного запроса к Telegram, из-за того, что все миниатюры сохранены локально.

Клик по миниатюре вызывает обращение к общему стейту через `openMedia(media, index)`. Этот метод сохраняет в стейт список медиа и индекс открываемого элемента, а [`LnbMediaViewer`](https://github.com/Laraue/laraue-boards/blob/master/app/pages/organizations/%5BorgKey%5D.vue) отрисовывает открытый элемент. Для загрузки оригинала используется `originalFileId`: элементы image и video запрашивают его через `src`. Элемент video использует несколько дополнительных атрибутов: `controls`, `playsinline`, `poster` с `previewFileId` — для показа миниатюры, пока видео подгружается и `preload="metadata"`, заставляющий браузер скачивать только то, что нужно для старта воспроизведения видео. Запуск воспроизведения элемента `<video>` стартует описанный ранее процесс стриминга файла. 

## Работа с файлами локального storage из контейнеров приложений

Для того чтобы приложения в контейнерах имели доступ к папке сервера, живущей независимо от контейнеров, необходимо настроить volume в файле `docker-compose.yaml`, представленного в [статье про деплой](deploying-dotnet-postgres-vps-docker-compose). Файлы будут храниться в папке `/home/laraue/storage` VPS. Уничтожение контейнеров никак не будет влиять на эти файлы.

В секции nginx файла `docker-compose.yaml` прокидывается доступ к папке `storage` сервера:

```yaml
nginx:
  volumes:
    - storage:/home/laraue/storage
```

В том же файле устанавливается переменная окружения `FilesDirectory` из показанного ранее `FileStorageOptions`:

```yaml
structuredmessageswebapihost:
  environment:
    FileStorageOptions__FilesDirectory: "/home/storage/note-board"
```

Теперь все файлы, которые мы решим сохранять в приложении, окажутся в физической папке `/home/laraue/storage` сервера.

## Итоги

Сбоя, о котором говорилось в начале статьи, больше нет. В процессе починки бот научился обрабатывать фото, видео и GIF также просто, как и текст. Для реализации этого визуально небольшого изменения пришлось продумать множество деталей. Маленькие превью хранятся локально, чтобы доска, полная картинок, грузилась быстро с нашего собственного диска; полные оригиналы находятся на серверах Telegram и стримятся по требованию, так мы экономим на хранении больших файлов; реализован стриминг файлов на основе диапазонов - чтобы файлы не буферизовались в памяти сервера.

## Что дальше

В логике сохранения осталась недоработка: что происходит, когда кто-то *редактирует* уже отправленное сообщение? На самом деле, код обработки это уже учитывает. Но, целиком, история про обновление ранее сохраненного сообщения, и про сложности, которые Telegram вносит в работу отредактированных и сгруппированных сообщений — тянет на отдельную статью. Именно об этом мы и будем рассказывать далее.