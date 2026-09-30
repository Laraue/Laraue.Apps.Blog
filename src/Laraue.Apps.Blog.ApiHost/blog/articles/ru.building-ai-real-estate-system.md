---
title: Парсинг JavaScript-сайтов недвижимости и оценка фото с Ollama на C#
type: article
projects: [real-estate]
description: Как построить агрегатор недвижимости на .NET — парсинг через PuppeteerSharp с ранним завершением, интеграция Ollama vision model для оценки фото, штрафная формула ранжирования и уведомления в Telegram. Исходный код на GitHub.
createdAt: 2026-04-16
updatedAt: 2026-09-30
---
**Парсинг JavaScript-сайтов с объявлениями о недвижимости на C#, оценка каждого фото локальной vision-моделью и ранжирование результатов по качеству ремонта** — звучит как задача на выходные, пока не наткнёшься на реальные проблемы: редиректы для защиты от ботов, GPU-зависимый инференс, блокирующий краулер, и TensorFlow-модели, которые застревают на бесполезной точности. Эта статья разбирает, как [Laraue.Apps.RealEstate](https://github.com/Laraue/Laraue.Apps.RealEstate) решает каждую из этих проблем — с реальным кодом из репозитория.

Работающее приложение доступно на [apartments.laraue.com](https://apartments.laraue.com). Если вас интересует, что оно делает с точки зрения пользователя, а не как устроено внутри — смотрите [описание продукта](../projects/real-estate).

---

## Архитектура: три хоста, одна задача

```
WorkerHost       → парсит объявления + вычисляет рейтинги
GpuWorkerHost    → запускает задачи инференса Ollama
ApiHost          → обслуживает фронтенд и Telegram-бота
```

Разделение `WorkerHost` и `GpuWorkerHost` — ключевое архитектурное решение. Инференс изображений привязан к GPU и медленный: на потребительском железе оценка фотографий одного объявления может занимать несколько секунд. Запуск инференса в том же процессе, что и краулер, означал бы остановку краулера в ожидании предсказаний. Разделение позволяет каждому работать в своём темпе: краулер собирает объявления каждые 4 часа, предиктор непрерывно разбирает очередь необработанных с частотой одно объявление в минуту.

`ApiHost` — стандартный ASP.NET Core без интересной архитектуры. Вся сложность сосредоточена в двух других хостах.

---

## Краулер: PuppeteerSharp + извлечение на основе схем

Циан (основной российский агрегатор недвижимости) рендерит страницы объявлений через JavaScript. AngleSharp, хорошо работающий со статическим HTML, не видит отрендеренный DOM. Краулер использует **PuppeteerSharp** — обёртку над headless Chromium — для навигации по страницам и извлечения данных после выполнения JavaScript.

### BaseCrawlingSchemaParser: повторные попытки, рандомизация, защита от блокировок

`BaseCrawlingSchemaParser` ([исходник](https://github.com/Laraue/Laraue.Apps.RealEstate/blob/main/src/Laraue.Apps.RealEstate.Crawling.AppServices/BaseCrawlingSchemaParser.cs)) управляет жизненным циклом браузера и навигацией по страницам:

```csharp
public Task<CrawlingResult> ParseLinkAsync(string link, CancellationToken cancellationToken = default)
{
    return Policy.Handle<PageOpenException>()
        .WaitAndRetryAsync(
            10,
            i => TimeSpan.FromSeconds(i * 100),
            (ex, timeSpan) => _logger.LogError(ex, "The page scheduled to be opened again in {Time}", timeSpan))
        .ExecuteAsync(ct => ParseLinkInternalAsync(link, ct), cancellationToken);
}
```

Три важных момента:

**Повторные попытки Polly с экспоненциальной задержкой.** Если страница не открылась — сетевая ошибка, блокировка бота, rate limit — парсер ждёт `i * 100` секунд и пробует снова, до 10 раз. Это устраняет временные сбои без ручного вмешательства.

**Случайная задержка между страницами.** Перед извлечением каждой страницы парсер спит случайный интервал между `MinTimeoutBeforeSwitchToNextPage` и `MaxTimeoutBeforeSwitchToNextPage` (настраивается для каждого источника). Это имитирует поведение живого пользователя и снижает fingerprint, на который нацелены системы антибот-защиты.

**Обнаружение редиректа как сигнал завершения.** Циан перенаправляет на другой URL, когда результаты заканчиваются. Парсер обнаруживает это и бросает `SessionInterruptedException` — задача перехватывает его и чисто завершает парсинг:

```csharp
if (result?.Url != link)
{
    throw new SessionInterruptedException($"Redirect to {result?.Url} received. All pages have been parsed.");
}
```

### CianCrawlingSchema: декларативное извлечение DOM

`CianCrawlingSchema` ([исходник](https://github.com/Laraue/Laraue.Apps.RealEstate/blob/main/src/Laraue.Apps.RealEstate.Crawling.AppServices/Cian/CianCrawlingSchema.cs)) определяет логику извлечения декларативно через fluent API `PuppeterSharpSchemaBuilder` из библиотеки [Laraue.Crawling](https://github.com/win7user10/Laraue.Crawling):

```csharp
return new PuppeterSharpSchemaBuilder<CrawlingResult>()
    .HasArrayProperty(x => x.Advertisements, "article", pageBuilder =>
    {
        // Простой CSS-селектор → привязка свойства
        pageBuilder.HasProperty(
            x => x.ShortDescription,
            "div[data-name=Description]");

        // Селектор + трансформация: извлечение цифр из строки цены
        pageBuilder.HasProperty(
            x => x.TotalPrice,
            builder => builder
                .UseSelector("span[data-mark=MainPrice]")
                .Map(s => long.Parse(s.GetOnlyDigits())));

        // Ручная привязка: разрешение href, извлечение ID объявления из пути URL
        pageBuilder.BindManually(async (e, b) =>
        {
            var linkElement = await e.QuerySelectorAsync("div[data-name=LinkArea] a");
            var href = await linkElement.GetAttributeValueAsync("href");
            if (href is null || !Uri.TryCreate(href, UriKind.Absolute, out var url))
                return;

            b.BindProperty(x => x.Id, url.AbsolutePath.GetIntOrDefault().ToString());
            b.BindProperty(x => x.Link, new Uri(href).LocalPath);
        });

        // Массив свойств: все атрибуты src изображений галереи
        pageBuilder.HasArrayProperty(
            x => x.ImageLinks,
            "div[data-name=Gallery] img",
            el => el!.GetAttributeValueAsync("src"));
    })
    .Build()
    .BindingExpression;
```

Схема обрабатывает три уровня сложности:

**Простые привязки** — CSS-селектор напрямую маппится на типизированное свойство. Библиотека берёт на себя null-безопасность и приведение типов.

**Маппированные привязки** — селектор плюс трансформация `.Map()`. Поле цены использует `GetOnlyDigits()` для удаления символа валюты перед парсингом в `long`.

**Ручные привязки** — `BindManually` даёт прямой доступ к `IElementHandle` для случаев, не укладывающихся в паттерн селектора. Блок со станцией метро, например, требует чтения двух соседних элементов и объединения их в запись `TransportStop`:

```csharp
pageBuilder.BindManually(async (element, modelBinder) =>
{
    var name = await element
        .QuerySelectorAsync("div[data-name=SpecialGeo] a")
        .AwaitAndModify(x => x.GetInnerTextAsync());

    // "7 минут пешком" или "5 минут на транспорте"
    var title = await subElement.GetInnerTextAsync();
    var titleParts = title?.Split(' ') ?? Array.Empty<string>();

    var minutesToMetro = titleParts[0].GetIntOrDefault();
    var distanceType = titleParts.Last() == "пешком"
        ? DistanceType.Foot
        : DistanceType.Car;

    modelBinder.BindProperty(x => x.TransportStops, new[]
    {
        transportStop with { Minutes = minutesToMetro, DistanceType = distanceType }
    });
});
```

Парсинг дат также реализован в схеме: русскоязычные относительные даты Циана («сегодня», «вчера», «24 сен») конвертируются в UTC-значения `DateTime`.

### Раннее завершение: инкрементальный краулинг

Краулер запрашивает объявления, отсортированные от новых к старым. На каждом запуске `BaseRealEstateCrawlerJob` вставляет новые записи, пока не встретит ID, уже присутствующий в базе — тогда останавливается. Не нужно обходить весь набор результатов: каждый запуск обрабатывает только дельту с момента последнего. В сочетании с расписанием раз в 4 часа это поддерживает базу актуальной без избыточных запросов.

---

## Инференс изображений: Ollama + qwen2.5 Vision

### EstimateImagesRenovationJob

`EstimateImagesRenovationJob` ([исходник](https://github.com/Laraue/Laraue.Apps.RealEstate/blob/main/src/Laraue.Apps.RealEstate.GpuWorkerHost/Jobs/EstimateImagesRenovationJob.cs)) запускается в `GpuWorkerHost` по расписанию раз в минуту. Дизайн задачи использует паттерн, заслуживающий отдельного внимания: **вложенный интерфейс `IRepository`** размещает контракт доступа к данным рядом с задачей, которой он принадлежит:

```csharp
public class EstimateImagesRenovationJob(...) : BaseJob
{
    public interface IRepository
    {
        Task<AdvertisementPredictionData?> GetNextUnpredictedAdvertisement(CancellationToken ct);
        Task UpdatePrediction(long id, PredictionResult prediction, CancellationToken ct);
    }

    public class Repository(AdvertisementsDbContext dbContext, ...) : IRepository
    {
        public Task<AdvertisementPredictionData?> GetNextUnpredictedAdvertisement(CancellationToken ct)
        {
            return dbContext.Advertisements
                .Where(x => x.PredictedAt == null)
                .Select(x => new AdvertisementPredictionData
                {
                    Id = x.Id,
                    ImageUrls = x.LinkedImages.Select(y => y.Image.Url).ToArray()
                })
                .FirstOrDefaultAsyncEF(ct);
        }

        public async Task UpdatePrediction(long id, PredictionResult prediction, CancellationToken ct)
        {
            await dbContext.Advertisements
                .Where(x => x.Id == id)
                .ExecuteUpdateAsync(upd => upd
                    .SetProperty(x => x.PredictedAt, dateTimeProvider.UtcNow)
                    .SetProperty(x => x.RenovationRating, prediction.RenovationRating)
                    .SetProperty(x => x.Advantages, prediction.Advantages)
                    .SetProperty(x => x.Problems, prediction.Problems), ct);
        }
    }
}
```

Интерфейс `IRepository` вложен внутрь класса задачи намеренно: интерфейс имеет смысл только в контексте этой задачи, и вложение делает эту зависимость явной в коде, а не только по соглашению. Реализация `Repository` тоже вложена, так что все три — задача, интерфейс и реализация — живут в одном файле. Тестирование задачи означает мокирование одного сфокусированного интерфейса, а не широкого общего репозитория.

Цикл выполнения прост: взять следующее неоценённое объявление, запустить инференс, записать результат, повторить до опустошения очереди, затем спать минуту:

```csharp
while (!stoppingToken.IsCancellationRequested)
{
    var dataToPredict = await repository.GetNextUnpredictedAdvertisement(stoppingToken);
    if (dataToPredict is null)
        return WaitUntilNextFire; // очередь пуста, спим

    var prediction = await imagesPredictor.PredictAsync(dataToPredict.ImageUrls, stoppingToken);
    await repository.UpdatePrediction(dataToPredict.Id, prediction, stoppingToken);
}
```

### OllamaRealEstatePredictor

`OllamaRealEstatePredictor` отправляет байты изображений напрямую в локально размещённую **qwen2.5** vision-модель через HTTP API Ollama. Промпт задаёт критерии оценки — качество ремонта, чистота, естественный свет, следы повреждений — и запрашивает структурированный JSON-ответ.

Каждое фото производит `PredictionResult`:

```csharp
public record PredictionResult
{
    public double RenovationRating { get; init; } // 0.0 до 1.0
    public string[] Advantages { get; init; } = [];  // ["new_windows", "clean", "bright"]
    public string[] Problems { get; init; } = [];    // ["dark", "old_wallpaper", "damage"]
}
```

`Advantages` и `Problems` не участвуют в формуле ранжирования — они хранятся для настройки промптов и отладки. Когда объявление получает неожиданно низкую или высокую оценку, сохранённые массивы позволяют увидеть, на что именно среагировала модель, без повторного запуска инференса.

### Почему не облачный API

Весь инференс выполняется на локальной машине. Изображения не покидают сервер, нет затрат на API-вызовы, а модель можно заменить изменением одного параметра конфигурации. Vision-модель `qwen2.5` показывает приемлемую производительность на потребительском GPU-железе для этой задачи.

### Почему не кастомная TensorFlow-модель

Первоначальная реализация (октябрь 2023) использовала три кастомных TensorFlow-модели с ~22M параметрами суммарно. Они работали быстро, но давали плохие результаты. Фундаментальная проблема была не в архитектуре — а в **данных**. Собрать большой, последовательно размеченный датасет фотографий квартир действительно сложно:

- Понятие «хороший ремонт» субъективно и меняется в зависимости от ценового сегмента
- Фото одной и той же квартиры, снятые по-разному, получают разные оценки
- Размечать сотни тысяч фото точно без команды непрактично

Модели рано достигли плато и так и не вышли на точность, пригодную для ранжирования. Переход на Ollama полностью устранил проблему датасета: предобученная vision-модель уже понимает, как выглядят «чистый», «светлый», «повреждённый» из своих обучающих данных. Компромисс — более медленный инференс, что компенсируется изоляцией в отдельном `GpuWorkerHost`.

---

## Ранжирование: штрафная формула идеальности

После оценки всех фото объявления `AdvertisementComputedFieldsCalculator` вычисляет итоговый **рейтинг идеальности** по штрафной модели. Счёт начинается с максимума и накапливает штрафы за негативные сигналы:

| Сигнал | Эффект |
|---|---|
| Нет ближайшей станции метро | Штраф |
| До метро слишком далеко пешком | Штраф |
| Слишком далеко от центра города | Штраф |
| Низкий средний рейтинг ремонта | Штраф |

Штрафная модель проще в понимании и настройке, чем взвешенная сумма. Каждый штраф имеет изолированный, интерпретируемый эффект: хотите, чтобы расстояние до метро имело меньшее значение — уменьшите этот штраф. Не нужно одновременно перебалансировать все остальные веса.

**Рейтинг ремонта** объявления — это среднее значение `RenovationRating` по всем его фотографиям. Объявления с меньшим минимального порога числом фото исключаются из ранжирования по ремонту — одно нерепрезентативное изображение может значительно исказить небольшое среднее.

---

## Интеграция с Telegram

Система отправляет ранжированные объявления в Telegram через `AdvertisementsTelegramSender` ([исходник](https://github.com/Laraue/Laraue.Apps.RealEstate/blob/main/src/Laraue.Apps.RealEstate.Telegram.AppServices/AdvertisementsTelegramSender.cs)). Есть два режима доставки:

**Персональные подборки.** Пользователи настраивают `Selection` с кастомными критериями — диапазон цен, количество комнат, район, минимальный ИИ-рейтинг, интервал уведомлений. Отправитель запрашивает базу по этим критериям и пушит результаты по настроенному расписанию. Пагинация реализована через inline-кнопки со stateful callback-маршрутами, так что пользователи могут листать результаты внутри одной Telegram-переписки.

**Публичный канал.** По расписанию задача постит в публичный канал с захардкоженными фильтрами: объявления с рейтингом ремонта ≥ 7, ценой 5–9 млн рублей, обновлённые за последний интервал доставки. Сообщение включает предложение использовать персонального бота для кастомной фильтрации:

```csharp
messageBuilder.AppendRow($"<i>Индивидуальная настройка подборки объявлений в боте {botUsername}</i>");
```

Отправитель использует логику edit-vs-send: если передан `messageId`, он редактирует существующее сообщение (для пагинации в рамках сессии); иначе отправляет новое (для первичной доставки и плановых уведомлений).

---

## Исходный код

- **Основной репозиторий:** [github.com/Laraue/Laraue.Apps.RealEstate](https://github.com/Laraue/Laraue.Apps.RealEstate)
- **Библиотека краулера:** [github.com/win7user10/Laraue.Crawling](https://github.com/win7user10/Laraue.Crawling)
- **Живое приложение:** [apartments.laraue.com](https://apartments.laraue.com)

---

## Часто задаваемые вопросы

**Чем PuppeteerSharp отличается от AngleSharp для парсинга сайтов на C#?**

AngleSharp парсит статический HTML — работает с сырыми байтами ответа, быстрый и лёгкий. PuppeteerSharp управляет настоящим браузером Chromium, выполняя JavaScript перед извлечением DOM. Используйте AngleSharp, когда контент страницы есть в начальном HTML-ответе; PuppeteerSharp — когда контент рендерится JavaScript после загрузки страницы. Большинство современных агрегаторов недвижимости относятся ко второму случаю.

**Как интегрировать Ollama с C# для анализа изображений?**

Ollama предоставляет локальный HTTP API. Передайте байты изображения в base64 в теле запроса вместе с промптом — модель вернёт текстовый ответ. Для структурированного вывода попросите модель ответить в JSON и распарсите ответ. `OllamaRealEstatePredictor` в этом проекте следует этому паттерну с vision-моделью `qwen2.5`.

**Зачем выносить GPU-инференс в отдельный хост-процесс?**

Инференс изображений медленный и привязан к GPU. Если бы он выполнялся в том же процессе, что и краулер, краулер останавливался бы в ожидании предсказаний перед переходом к следующему объявлению. Разделение означает, что краулер работает по собственному расписанию, предиктор независимо разбирает очередь, а GPU-хост можно перенести на отдельную машину без изменения кода краулера.

**Почему штрафная формула ранжирования работает лучше взвешенной суммы?**

При взвешенной сумме изменение одного веса сдвигает относительный вклад всех остальных факторов одновременно, делая настройку неинтуитивной. В штрафной модели каждый негативный сигнал вносит вклад независимо и аддитивно. Добавление нового фактора или изменение существующего имеет предсказуемый, изолированный эффект.