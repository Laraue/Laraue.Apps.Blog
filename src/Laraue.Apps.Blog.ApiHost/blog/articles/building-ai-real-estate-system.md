---
title: Building an AI-Powered Real Estate Ranking System with C#, Ollama, and a Custom Crawler
type: article
projects: [real-estate]
description: A technical deep-dive into an open-source real estate aggregator for Saint Petersburg — covering the C# / .NET 9 architecture, Ollama vision model integration, custom crawler design, and the ideality scoring formula.
createdAt: 2026-04-16
updatedAt: 2026-09-30
---
**Scraping JavaScript-rendered real estate listings in C#, scoring every photo with a local vision model, and ranking results by renovation quality** sounds like a weekend project until you hit the real problems: anti-bot redirects, GPU-bound inference blocking your crawler, and TensorFlow models that plateau at useless accuracy. This article walks through how [Laraue.Apps.RealEstate](https://github.com/Laraue/Laraue.Apps.RealEstate) solves each of these — with real code from the repo.

The live app is at [apartments.laraue.com](https://apartments.laraue.com). If you want to understand what it does from a user perspective rather than how it was built, see the [product overview](../projects/real-estate).

---

## Architecture: Three Hosts, One Purpose

```
WorkerHost       → crawls listings + computes ranking scores
GpuWorkerHost    → runs Ollama image inference jobs  
ApiHost          → serves frontend and Telegram bot requests
```

The split between `WorkerHost` and `GpuWorkerHost` is the most important architectural decision. Image inference is GPU-bound and slow — on consumer hardware, scoring a single listing's photos can take several seconds. Running inference in the same process as the crawler would mean the crawler stalls waiting for predictions. Separating them means each can run at its own pace: the crawler collects listings every 4 hours, the predictor continuously drains the unscored queue at one listing per minute.

The `ApiHost` is standard ASP.NET Core with no interesting architecture — the complexity lives in the other two hosts.

---

## The Crawler: PuppeteerSharp + Schema-Based Extraction

Cian (the primary Russian real estate aggregator) renders its listing pages with JavaScript. AngleSharp, which works well for static HTML, can't see the rendered DOM. The crawler uses **PuppeteerSharp** — a headless Chromium wrapper — to navigate pages and extract data after JavaScript execution.

### BaseCrawlingSchemaParser: Retry, Randomization, Anti-Bot

`BaseCrawlingSchemaParser` ([source](https://github.com/Laraue/Laraue.Apps.RealEstate/blob/main/src/Laraue.Apps.RealEstate.Crawling.AppServices/BaseCrawlingSchemaParser.cs)) handles the browser lifecycle and page navigation:

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

Three things worth noting:

**Polly retry with exponential backoff.** If a page fails to open — network error, bot detection, rate limit — the parser waits `i * 100` seconds and tries again, up to 10 times. This handles transient failures without human intervention.

**Randomized delay between pages.** Before extracting each page, the parser sleeps for a random interval between `MinTimeoutBeforeSwitchToNextPage` and `MaxTimeoutBeforeSwitchToNextPage` (configured per source). This mimics human browsing patterns and reduces the fingerprint that bot-detection systems target.

**Redirect detection as termination signal.** Cian redirects to a different URL when there are no more results to show. The parser detects this and throws `SessionInterruptedException` — the job catches it and stops crawling cleanly:

```csharp
if (result?.Url != link)
{
    throw new SessionInterruptedException($"Redirect to {result?.Url} received. All pages have been parsed.");
}
```

### CianCrawlingSchema: Declarative DOM Extraction

`CianCrawlingSchema` ([source](https://github.com/Laraue/Laraue.Apps.RealEstate/blob/main/src/Laraue.Apps.RealEstate.Crawling.AppServices/Cian/CianCrawlingSchema.cs)) defines the extraction logic declaratively using the `PuppeterSharpSchemaBuilder` fluent API from the [Laraue.Crawling](https://github.com/win7user10/Laraue.Crawling) library:

```csharp
return new PuppeterSharpSchemaBuilder<CrawlingResult>()
    .HasArrayProperty(x => x.Advertisements, "article", pageBuilder =>
    {
        // Simple CSS selector → property binding
        pageBuilder.HasProperty(
            x => x.ShortDescription,
            "div[data-name=Description]");

        // Selector + transform: extract digits from price string
        pageBuilder.HasProperty(
            x => x.TotalPrice,
            builder => builder
                .UseSelector("span[data-mark=MainPrice]")
                .Map(s => long.Parse(s.GetOnlyDigits())));

        // Manual binding: resolve href, extract listing ID from URL path
        pageBuilder.BindManually(async (e, b) =>
        {
            var linkElement = await e.QuerySelectorAsync("div[data-name=LinkArea] a");
            var href = await linkElement.GetAttributeValueAsync("href");
            if (href is null || !Uri.TryCreate(href, UriKind.Absolute, out var url))
                return;

            b.BindProperty(x => x.Id, url.AbsolutePath.GetIntOrDefault().ToString());
            b.BindProperty(x => x.Link, new Uri(href).LocalPath);
        });

        // Array property: all gallery image src attributes
        pageBuilder.HasArrayProperty(
            x => x.ImageLinks,
            "div[data-name=Gallery] img",
            el => el!.GetAttributeValueAsync("src"));
    })
    .Build()
    .BindingExpression;
```

The schema handles three levels of complexity:

**Simple bindings** — a CSS selector maps directly to a typed property. The library handles null safety and type coercion.

**Mapped bindings** — a selector plus a `.Map()` transform. The price field uses `GetOnlyDigits()` to strip the currency symbol before parsing to `long`.

**Manual bindings** — `BindManually` gives raw access to the `IElementHandle` for cases that don't fit a selector pattern. The metro station block, for example, requires reading two sibling elements and combining them into a `TransportStop` record:

```csharp
pageBuilder.BindManually(async (element, modelBinder) =>
{
    var name = await element
        .QuerySelectorAsync("div[data-name=SpecialGeo] a")
        .AwaitAndModify(x => x.GetInnerTextAsync());

    // "7 минут пешком" or "5 минут на транспорте"
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

Date parsing is also handled in the schema, converting Cian's Russian-language relative dates ("сегодня", "вчера", "24 сен") into UTC `DateTime` values.

### Early Termination: Delta Crawling

The crawler requests listings sorted by newest first. On each run, `BaseRealEstateCrawlerJob` inserts new records until it encounters a listing ID that already exists in the database — at which point it stops. No need to crawl the full result set: each run processes only the delta since the last run. Combined with the 4-hour schedule, this keeps the database current without excessive requests.

---

## Image Inference: Ollama + qwen2.5 Vision

### EstimateImagesRenovationJob

`EstimateImagesRenovationJob` ([source](https://github.com/Laraue/Laraue.Apps.RealEstate/blob/main/src/Laraue.Apps.RealEstate.GpuWorkerHost/Jobs/EstimateImagesRenovationJob.cs)) runs in `GpuWorkerHost` on a 1-minute schedule. The job design follows a pattern worth highlighting: the **inner `IRepository` interface** co-locates the data access contract with the job that owns it:

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

The `IRepository` interface is nested inside the job class. This is intentional: the interface is only meaningful in the context of this job, and nesting it makes that dependency relationship explicit in code rather than just by convention. The `Repository` implementation is also nested, so all three — job, interface, and implementation — live in the same file. Testing the job means mocking one focused interface rather than a broad shared repository.

The execution loop is simple: pull the next unscored listing, run inference, write back the result, repeat until the queue is empty, then sleep for 1 minute:

```csharp
while (!stoppingToken.IsCancellationRequested)
{
    var dataToPredict = await repository.GetNextUnpredictedAdvertisement(stoppingToken);
    if (dataToPredict is null)
        return WaitUntilNextFire; // queue empty, sleep

    var prediction = await imagesPredictor.PredictAsync(dataToPredict.ImageUrls, stoppingToken);
    await repository.UpdatePrediction(dataToPredict.Id, prediction, stoppingToken);
}
```

### OllamaRealEstatePredictor

`OllamaRealEstatePredictor` sends image bytes directly to a locally-hosted **qwen2.5** vision model via Ollama's HTTP API. The prompt specifies the evaluation criteria — renovation quality, cleanliness, natural light, signs of damage — and asks for a structured JSON response.

Each photo produces a `PredictionResult`:

```csharp
public record PredictionResult
{
    public double RenovationRating { get; init; } // 0.0 to 1.0
    public string[] Advantages { get; init; } = [];  // ["new_windows", "clean", "bright"]
    public string[] Problems { get; init; } = [];    // ["dark", "old_wallpaper", "damage"]
}
```

`Advantages` and `Problems` don't feed into the ranking formula — they're stored for prompt tuning and debugging. When a listing gets a surprisingly low or high score, the stored arrays let you see exactly what the model reacted to without re-running inference.

### Why Not a Cloud API

All inference runs on the local machine. No images leave the server, no per-call API costs, and the model can be swapped by changing one configuration value. The `qwen2.5` vision model runs at acceptable throughput on consumer GPU hardware for this use case.

### Why Not a Custom-Trained TensorFlow Model

The original implementation (October 2023) used three custom-trained TensorFlow models with ~22M parameters total. It was fast but produced poor results. The fundamental problem wasn't model architecture — it was **data**. Collecting a large, consistently annotated dataset of apartment photos is genuinely hard:

- What counts as "good renovation" is subjective and varies by price bracket
- Photos of the same apartment taken differently score differently
- Labelling hundreds of thousands of photos accurately is impractical without a team

The models plateaued early and never reached accuracy useful for ranking. Switching to Ollama eliminated the dataset problem entirely: the pre-trained vision model already understands what "clean", "bright", "damaged" look like from its training data. The tradeoff is slower inference — offset by isolating it in the dedicated `GpuWorkerHost`.

---

## Ranking: Penalty-Based Ideality Score

Once all photos for a listing are scored, `AdvertisementComputedFieldsCalculator` computes the final **ideality score** using a penalty model. The score starts at a maximum and accumulates fines for negative signals:

| Signal | Effect |
|---|---|
| No nearby metro station | Penalty applied |
| Metro station too far to walk | Penalty applied |
| Distance from city centre too large | Penalty applied |
| Low average renovation rating | Penalty applied |

A penalty model is easier to reason about and tune than a weighted sum. Each penalty has an isolated, interpretable effect: if you want metro distance to matter less, reduce that penalty. You don't have to rebalance all other weights simultaneously.

The **renovation rating** for a listing is the average `RenovationRating` across all its photos. Listings with fewer than a minimum photo count are excluded from renovation ranking — a single unrepresentative image can skew a small average significantly.

---

## Telegram Integration

The system sends ranked apartment listings to Telegram via `AdvertisementsTelegramSender` ([source](https://github.com/Laraue/Laraue.Apps.RealEstate/blob/main/src/Laraue.Apps.RealEstate.Telegram.AppServices/AdvertisementsTelegramSender.cs)). There are two delivery modes:

**Personal selections.** Users configure a `Selection` with custom criteria — price range, number of rooms, district, minimum AI score, notification interval. The sender queries the database using those criteria and pushes results on the configured schedule. Pagination is handled via inline keyboard buttons with stateful callback routes, so users can navigate through results inside the same Telegram message thread.

**Public channel.** A scheduled job posts to a public channel with hardcoded filters: listings scored ≥ 7 renovation rating, price 5–9M rubles, updated in the last delivery interval. The message includes a prompt to use the personal bot for custom filtering:

```csharp
messageBuilder.AppendRow($"<i>Индивидуальная настройка подборки объявлений в боте {botUsername}</i>");
```

The sender uses edit-vs-send logic: if a `messageId` is provided, it edits the existing message (for paginated navigation within a session); otherwise it sends a new message (for initial delivery and scheduled notifications).

---

## Source Code

- **Main repo:** [github.com/Laraue/Laraue.Apps.RealEstate](https://github.com/Laraue/Laraue.Apps.RealEstate)
- **Crawler library:** [github.com/win7user10/Laraue.Crawling](https://github.com/win7user10/Laraue.Crawling)
- **Live app:** [apartments.laraue.com](https://apartments.laraue.com)

---

## Frequently Asked Questions

**How does PuppeteerSharp differ from AngleSharp for web scraping in C#?**

AngleSharp parses static HTML — it works on the raw response bytes and is fast and lightweight. PuppeteerSharp controls a real Chromium browser, executing JavaScript before extracting the DOM. Use AngleSharp when the page content is in the initial HTML response; use PuppeteerSharp when content is rendered by JavaScript after page load. Most modern real estate aggregators fall into the second category.

**How do you integrate Ollama with C# for image analysis?**

Ollama exposes a local HTTP API. Pass image bytes as base64 in the request body along with a prompt, and the model returns a text response. For structured output, prompt the model to respond in JSON and parse the response. The `OllamaRealEstatePredictor` in this project follows this pattern with the `qwen2.5` vision model.

**Why separate the GPU inference into its own host process?**

Image inference is slow and GPU-bound. If it ran in the same process as the crawler, the crawler would stall waiting for predictions to complete before moving to the next listing. Separating them means the crawler runs on its own schedule, the predictor drains the queue independently, and the GPU host can be moved to a dedicated machine without changing the crawling code.

**Why does a penalty-based ranking formula work better than a weighted sum here?**

In a weighted sum, changing one weight shifts the relative contribution of all other factors simultaneously, making tuning non-intuitive. In a penalty model, each negative signal contributes independently and additively. Adding a new factor or adjusting an existing one has a predictable, isolated effect.
