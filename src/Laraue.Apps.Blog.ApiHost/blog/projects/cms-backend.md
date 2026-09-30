---
title: Serve Markdown Files as an API in .NET — Laraue.CmsBackend
type: project
tags: [dotnet, csharp, cms, markdown, api, headless-cms, static-site, nuget]
description: A lightweight .NET 10 library that turns Markdown files with frontmatter into a filterable, sortable REST API. Strongly typed content schemas, no database, no CMS overhead. Open source, MIT license.
createdAt: 2025-11-01
updatedAt: 2026-09-30
---
Building a blog or documentation site in .NET and don't want to drag in a full CMS? **Laraue.CmsBackend** is a lightweight .NET 10 library that turns **Markdown files stored in Git into a queryable REST API** — with filtering, sorting, frontmatter support, and strongly typed content schemas. No database required.

|              |                                                                       |
|--------------|-----------------------------------------------------------------------|
| Language     | C#                                                                    |
| Framework    | .NET 10                                                               |
| Project type | Library                                                               |
| Status       | Active Development                                                    |
| License      | MIT                                                                   |
| NuGet        | ![latest version](https://img.shields.io/nuget/v/Laraue.CmsBackend)  |
| Downloads    | ![downloads](https://img.shields.io/nuget/dt/Laraue.CmsBackend)      |
| GitHub       | [Laraue.CmsBackend](https://github.com/win7user10/Laraue.CmsBackend) |

---

## Why This Library Exists

The goal was simple: store all blog content as Markdown files in a Git repository, separate from the frontend, and query it via API — with filtering by tags, sorting by date, and no database to maintain.

The available options didn't fit. Storing Markdown inside the frontend loses the separation of concerns. Full CMS platforms like WordPress or Contentful introduce databases, admin panels, and hosting complexity that a developer blog doesn't need.

**The result is a third path:** Markdown files in Git, served through a typed .NET API with frontmatter attribute support.

> This blog itself is built on Laraue.CmsBackend. The full source code for the blog backend is publicly available at [Laraue.Apps.Blog on GitHub](https://github.com/Laraue/Laraue.Apps.Blog) — a working reference implementation you can inspect or fork.

---

## The Problem With Traditional CMS Platforms

### What Is a CMS?

A Content Management System (CMS) lets users create and manage website content without specialized technical knowledge. Popular options include WordPress, Drupal, and Joomla.

### Why a CMS Isn't Always the Right Tool

CMS platforms reduce time-to-production by handling architecture, templates, SEO defaults, and admin interfaces. But they come with real drawbacks:

- **Architectural lock-in:** They impose specific structure that's hard to customize or escape.
- **Stack mismatch:** A PHP-based CMS bolted onto a .NET infrastructure adds support and security overhead.
- **Security exposure:** The widespread popularity of major CMS platforms makes them high-value targets. Known vulnerabilities are actively exploited to steal databases and deface sites.

For developer-focused sites, documentation, and technical blogs, a lightweight file-based approach is often the better fit.

---

## SSR, SSG, and the SEO Problem With Reactive Frameworks

### The Core Issue

Sites built with reactive frameworks (React, Vue, Angular) may not render complete HTML until after the page begins loading. Search engine crawlers have to execute JavaScript, wait for async requests, and process substantial data before indexable content is available. Slow responses cause crawlers to abandon the page before full indexing completes.

### The Two Standard Fixes

**Server-Side Rendering (SSR)** has the server fetch content and pre-render the full HTML page before sending it to the client. Crawlers receive complete, immediately indexable HTML — no JavaScript execution required. The tradeoff is increased server load per request.

**Static Site Generation (SSG)** pre-builds pages at deploy time and delivers static HTML with JavaScript layered on top. It's well-suited to truly static content, but **displaying dynamic or filtered content — like a paginated, tag-filtered blog** — is awkward or impossible.

### A Lesson Learned: Don't Skip SSR for Content Sites

The first version of this blog launched without SSR. The reasoning seemed sound at the time: modern search engines are capable of rendering JavaScript, the API responses were fast, and skipping SSR meant simpler infrastructure and lower server load.

**That turned out to be a mistake.** In practice, pages took far too long to get indexed — even with fast API responses. Google's crawler does render JavaScript, but not on the same schedule as static HTML. New posts sat unindexed for weeks.

The fix was straightforward: **enable SSR on the frontend**. On each request, the server calls the CMS backend API, receives the pre-rendered HTML content, and sends a fully assembled page to the client. Crawlers index it immediately, just like any static page.

The lesson: if your content needs to be discovered via search, don't assume crawlers will handle client-side rendering in a reasonable timeframe. SSR is worth the added server load.

---

## What Laraue.CmsBackend Does

The library provides a backend API layer for Markdown-based content. The core capabilities:

- **Strongly typed content schemas** — define C# classes with `required` properties that map directly to frontmatter fields; any Markdown file missing a required property throws at startup, not silently at query time
- **Frontmatter parsing** — YAML frontmatter fields (title, tags, date, custom attributes) are parsed into typed .NET objects
- **Filtering and sorting API** — query content by any frontmatter attribute; get a list of all unique tags sorted alphabetically; paginate results
- **Markdown-to-HTML rendering** — built-in transformer converts Markdown body to HTML, with support for internal link generation
- **Git-friendly storage** — content lives as `.md` files in your repository; no database, no migrations, no backups to manage

---

## Strongly Typed Content Schemas

One of the library's core design decisions is **enforcing content schemas through C# types** rather than relying on convention or runtime nulls.

Each content category — blog posts, documentation pages, project pages — gets its own class that inherits `BaseContentType`. Properties marked `required` must be present in the Markdown frontmatter. If any file is missing a required field, **the application throws at startup**, making the problem immediately visible rather than manifesting as a broken page in production.

Here's the real `Documentation` content type used on this blog ([source on GitHub](https://github.com/Laraue/Laraue.Apps.Blog/blob/main/src/Laraue.Apps.Blog.ApiHost/docTypes/Documentation.cs)):

```csharp
using Laraue.CmsBackend;

namespace Laraue.Apps.Blog.ApiHost.docTypes;

public class Documentation : BaseContentType
{
    public required string Project { get; set; }
    public string? Description { get; set; }
    public string[]? Keywords { get; set; }
    public int Order { get; set; }
}
```

`Project` is `required` — every documentation page must declare which project it belongs to, or the application won't start. `Description`, `Keywords`, and `Order` are optional; the library handles missing optional fields gracefully.

This approach brings the same compile-time safety mindset to content authoring that C# developers already apply to application code. Authors get fast, unambiguous feedback rather than silent gaps in the API response.



### 1. Define Your Content Type

```csharp
public class Article : BaseContentType
{
    public required string[] Projects { get; init; }
    public required string Description { get; init; }
}
```

### 2. Write Your Markdown Content

```markdown
---
title: About my project
projects: [Project1, Project2]
description: My short description
---
The markdown content goes here.
```

Organize files to mirror your frontend URL structure:

```
blog/
└── articles/
    ├── article1.md
    └── article2.md
```

### 3. Build the Host

```csharp
var cmsBackend = new CmsBackendBuilder(
        new MarkdownParser(
            new MarkdownToHtmlTransformer(),
            new ArticleInnerLinksGenerator()),
        new MarkdownProcessor())
    .AddContentType<Article>()
    .AddContentFolder("blog")
    .Build();
```

### 4. Add Controller Endpoints With Typed DTOs

Rather than returning a raw `Dictionary<string, object>`, you can define explicit response DTOs and pass them as a generic type parameter. The library maps only the fields listed in `Properties` into your DTO — nothing more is serialized and sent to the client.

Here's how the real blog controller ([source on GitHub](https://github.com/Laraue/Laraue.Apps.Blog/blob/main/src/Laraue.Apps.Blog.ApiHost/Controllers/BlogController.cs)) exposes a card list endpoint and a detail endpoint, each backed by its own DTO:

```csharp
// Lightweight DTO for list views — only the fields the frontend card component needs
public class CardItem
{
    public required string FileName { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string ContentType { get; init; }
    public required string[] Path { get; init; }
    public required int Length { get; init; }
    public required string?[] Tags { get; init; }
}

// Richer DTO for detail pages — includes rendered content and navigation neighbours
public class CardDetail
{
    public required string Title { get; init; }
    public required string Content { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }
    public required string[] Tags { get; init; }
    public NeighborCard? Previous { get; set; }
    public NeighborCard? Next { get; set; }
}

[ApiController]
[Route("api/blog")]
public class BlogController(ICmsBackend cmsBackend) : ControllerBase
{
    [HttpPost("list")]
    public IShortPaginatedResult<CardItem> GetList([FromBody] GetCardsRequest request)
    {
        return cmsBackend.GetEntities<CardItem>(new GetEntitiesRequest
        {
            FromPath = request.Path,
            LanguageCode = request.LanguageCode,
            Properties = ["fileName", "title", "description", "contentType", "path", "length(content)", "tags"],
            Pagination = request.Pagination,
            Filters = [/* tag and content type filters */]
        });
    }

    [HttpPost("details")]
    public CardDetail GetDetail([FromBody] GetCardRequest request)
    {
        return cmsBackend.GetEntity<CardDetail>(new GetEntityRequest
        {
            Path = request.Path,
            LanguageCode = request.LanguageCode,
            Properties = [
                "title",
                "content",
                "format(createdAt, \"dd MMM yyyy\") as createdAt",
                "format(updatedAt, \"dd MMM yyyy\") as updatedAt",
                "tags",
                "next",
                "previous",
            ]
        });
    }
}
```

A few things worth noting in this pattern:

- **Property projection** — the `Properties` array controls exactly which frontmatter fields and computed values are fetched. Only listed fields are included in the response, so list endpoints stay lightweight even when the underlying content type has many fields.
- **Expression support** — `Properties` accepts expressions beyond plain field names: `"length(content)"` returns the character count of the rendered HTML body, and `"format(createdAt, \"dd MMM yyyy\") as createdAt"` formats the date server-side before it reaches the client.
- **Typed generics** — `GetEntities<CardItem>` and `GetEntity<CardDetail>` return fully typed objects. The controller signatures are clean, Swagger documentation is accurate, and the frontend gets a stable contract.

The library doesn't prescribe the API shape — you decide which endpoints to expose, which fields each one returns, and how the DTOs are structured.

---

## Real-World Usage: This Blog

Laraue.CmsBackend isn't just a demo project — it powers the blog you're reading right now. The full backend source is available at [github.com/Laraue/Laraue.Apps.Blog](https://github.com/Laraue/Laraue.Apps.Blog), including the content folder structure, controller setup, and CI/CD workflow. If you're evaluating the library, this is the most direct reference for how it works in production.

---

## Install via NuGet

```
dotnet add package Laraue.CmsBackend
```

Or search `Laraue.CmsBackend` in the NuGet Package Manager. The package targets **.NET 10** and is published under the **MIT license**.

Source code and issue tracker: [github.com/win7user10/Laraue.CmsBackend](https://github.com/win7user10/Laraue.CmsBackend)

---

## Roadmap

The library's current scope is serving Markdown content via API. A planned extension — `Laraue.CmsBackend.Telegram` — would add tooling to run a bot that automatically posts new content to Telegram channels based on configurable criteria, with per-post attributes controlling which distribution channels receive each post.

---

## Frequently Asked Questions

**What happens if a Markdown file is missing a required frontmatter field?**

The application throws at startup. Content types use C#'s `required` keyword on properties that must be present in frontmatter. This means schema violations are caught immediately when the app starts, not silently at query time or as a null reference in production.

**Do I need a database to use Laraue.CmsBackend?**

No. Content is stored as `.md` files in your repository. The library reads and parses them at runtime — no database setup, migrations, or connection strings required.

**What .NET version does the library target?**

The library targets .NET 10. It is published as a NuGet package (`Laraue.CmsBackend`) under the MIT license.

**Can I filter and sort content by frontmatter fields?**

Yes. The `GetEntitiesRequest` API supports filtering by any frontmatter attribute and returns paginated, sortable results. You can also query for aggregated values like a sorted list of all unique tags across all content files.

**Is there a working example I can reference?**

Yes — the blog at [laraue.com/blog](https://laraue.com/blog) runs on Laraue.CmsBackend, and the full backend source is open at [github.com/Laraue/Laraue.Apps.Blog](https://github.com/Laraue/Laraue.Apps.Blog).