---
title: C# Web Scraping Library — Strongly Typed Crawling for .NET
type: project
tags: [csharp, dotnet, web-scraping, crawler, anglesharp, puppeteersharp, html-parsing]
description: Laraue.Crawling is a strongly typed C# web scraping library for .NET that supports static HTML, JavaScript-rendered pages, and XML. Define maintainable crawling schemas in code — no spaghetti selectors.
createdAt: 2025-11-01
updatedAt: 2026-09-30
---
Most C# web scraping code works until it doesn't. A site changes its layout, a selector breaks, and you're
staring at a tangle of string selectors with no types, no tests, and no clear place to make the fix.
**Laraue.Crawling** is a strongly typed .NET web scraping library that lets you define crawling schemas
as clean, testable C# code — for static HTML, JavaScript-rendered pages, and XML alike.

[![NuGet](https://img.shields.io/nuget/v/Laraue.Crawling.Common)](https://www.nuget.org/packages/Laraue.Crawling.Common)
[![Downloads](https://img.shields.io/nuget/dt/Laraue.Crawling.Common)](https://www.nuget.org/packages/Laraue.Crawling.Common)
[![MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/win7user10/Laraue.Crawling)

---

## Why a Schema-Based Approach?

The typical C# web scraping workflow looks like this: pick a parser (AngleSharp or HtmlAgilityPack for
static pages, PuppeteerSharp or Playwright for JavaScript-heavy ones), write selector logic inline,
and move on. It works for one-off scripts. It falls apart the moment you need to maintain it.

Laraue.Crawling wraps those same battle-tested parsers in a schema layer that gives you:

- **Strong typing** — models are plain C# records; type errors surface at compile time, not runtime
- **Maintainability** — when a site changes, you update one schema definition, not scattered selector strings
- **Testability** — schemas are regular C# objects; test them with xUnit or NUnit like any other class
- **Unified API** — swap between static and dynamic parsers without rewriting your extraction logic

---

## Quickstart

Install the core package and a parser backend:

```bash
dotnet add package Laraue.Crawling.Static.AngleSharp
```

Define a model and build a schema:

```csharp
public record ProductPage(string Title, string Price) : ICrawlingModel;

var schema = new AngleSharpSchemaBuilder<ProductPage>()
    .HasProperty(x => x.Title, "h1.title")
    .HasProperty(x => x.Price, ".price")
    .Build();

var parser = new AngleSharpParser(new NullLoggerFactory());
var model = await parser.RunAsync(schema, html);

Console.WriteLine(model.Title);  // strongly typed, no casting
```

That's the full loop: define a model, map CSS selectors, run the parser, get a typed result.

---

## Static vs Dynamic Pages

### Static HTML — AngleSharp

Best for pages that don't require JavaScript execution. Fast, lightweight, no browser overhead.

```bash
dotnet add package Laraue.Crawling.Static.AngleSharp
```

```csharp
var schema = new AngleSharpSchemaBuilder<MyModel>()
    .HasProperty(x => x.Title, "h1")
    .HasProperty(x => x.Price, ".price-box span")
    .Build();
```

Use this when HtmlAgilityPack or raw AngleSharp feels like too much boilerplate for structured extraction.

### JavaScript-Rendered Pages — PuppeteerSharp

For sites that load content dynamically. Uses a real headless browser under the hood — the same
approach as PuppeteerSharp directly, but with the schema layer on top.

```bash
dotnet add package Laraue.Crawling.Dynamic.PuppeteerSharp
```

```csharp
var schema = new PuppeteerSharpSchemaBuilder<MyModel>()
    .HasProperty(x => x.Title, "h1")
    .HasProperty(x => x.Price, ".price")
    .Build();
```

Switch from `AngleSharpSchemaBuilder` to `PuppeteerSharpSchemaBuilder` — your model and property
mappings stay exactly the same.

### XML

```bash
dotnet add package Laraue.Crawling.Static.Xml
```

Same API, works on XML tree structures. Useful for RSS feeds, sitemaps, or API responses in XML format.

---

## How It Compares to Using Parsers Directly

| | Raw AngleSharp / HAP | PuppeteerSharp directly | Laraue.Crawling |
|---|---|---|---|
| Strongly typed models | ❌ | ❌ | ✅ |
| Unified API across parsers | ❌ | ❌ | ✅ |
| Schema testable as C# class | ❌ | ❌ | ✅ |
| JS-rendered page support | ❌ | ✅ | ✅ |
| Static page support | ✅ | ❌ | ✅ |
| Scheduled job support | ❌ | ❌ | ✅ |

Laraue.Crawling is not a replacement for AngleSharp or PuppeteerSharp — it builds on top of them.
If you need a quick one-file script, use the parsers directly. If you're building something you'll
maintain for months, a schema layer pays off quickly.

---

## Scheduled Crawling Jobs

The library includes a base class for running crawlers as scheduled ASP.NET hosted services.
Define your schema, extend the base job class, and the host handles scheduling, logging, and lifecycle:

```csharp
public class ProductCrawlerJob : BaseCrawlerJob<ProductPage>
{
    protected override CrawlingSchema<ProductPage> BuildSchema() => /* your schema */;
}
```

Register it in your DI container and it runs on your schedule automatically.

---

## FAQ

**Can I scrape JavaScript-rendered pages with this library?**
Yes — use the `Laraue.Crawling.Dynamic.PuppeteerSharp` package. It drives a real headless Chromium
browser, so it handles lazy loading, client-side rendering, and dynamic content the same way
PuppeteerSharp does directly.

**How is this different from using AngleSharp or HtmlAgilityPack directly?**
Those libraries parse HTML — they give you a DOM to query. Laraue.Crawling adds a schema layer on top:
you define *what* to extract into *which model property*, and the library handles the selector execution.
The result is typed, testable, and consistent across parser backends.

**Can I add support for a custom tree structure?**
Yes. Implement the parser interface for your node type (see the XML parser as a reference), add the
related schema builder, and the rest of the API works unchanged.

**Does it work with .NET 9 and .NET 10?**
Yes, the library targets modern .NET versions.

---

## Real-World Usage

Laraue.Crawling runs in production as part of [SPB Real Estate](https://github.com/Laraue/Laraue.Apps.RealEstate),
a property monitoring service that continuously crawls two of Russia's largest listing platforms —
[Avito](https://github.com/Laraue/Laraue.Apps.RealEstate/blob/main/src/Laraue.Apps.RealEstate.Crawling.Impl/Avito/AvitoCrawlingSchema.cs)
and
[Cian](https://github.com/Laraue/Laraue.Apps.RealEstate/blob/main/src/Laraue.Apps.RealEstate.Crawling.Impl/Cian/CianCrawlingSchema.cs)
— extracting listings as scheduled jobs.

---

## Packages

| Package | Purpose |
|---|---|
| `Laraue.Crawling.Common` | Core abstractions and interfaces |
| `Laraue.Crawling.Static.AngleSharp` | Static HTML parsing via AngleSharp |
| `Laraue.Crawling.Dynamic.PuppeteerSharp` | JS-rendered pages via PuppeteerSharp |
| `Laraue.Crawling.Static.Xml` | XML tree parsing |

**Source:** [github.com/win7user10/Laraue.Crawling](https://github.com/win7user10/Laraue.Crawling)