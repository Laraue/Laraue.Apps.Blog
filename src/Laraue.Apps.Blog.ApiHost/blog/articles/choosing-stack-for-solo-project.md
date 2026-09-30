---
title: Choosing a pet project stack for solo development — .NET, PostgreSQL, Nuxt, and why we prefer boring technologies
description: Part 4 of building a Telegram task tracker solo. The reasons behind .NET 10, PostgreSQL 18, Nuxt 4 and Vue 3. A bit about the MongoDB-to-Postgres migration in a past project that taught us to prefer boring, stable technologies.
type: article
createdAt: 2026-06-20 22:35
updatedAt: 2026-09-30 07:44
projects: [boards]
tags: [dotnet, nuxt, vue, postgres, mongodb, database, devlog, architecture]
previousLink: telegram-saved-messages-to-task-tracker
nextLink: clean-dotnet-telegram-bot-architecture
---

> **Architecture First: Building a Jira Alternative Solo, AI-Assisted** — Part 4.
> The [previous article](telegram-saved-messages-to-task-tracker) defined the app's first user path. Time to pick a stack for it.

Stack decisions are made after prototyping the app's interface and defining the user's possible actions. Picking a database or a framework before the typical usage scenarios are clear is a road to potential problems. By this point we had AI-generated HTML prototypes, so the choices could be made deliberately.

In short: .NET 10 and C# on the backend, PostgreSQL 18 for data storage, Nuxt 4 and Vue 3 with TypeScript on the frontend. The stack was chosen by the rule of [preferring stable technologies and keeping dependencies to a minimum](how-we-build-engineering-principles). In this article we will tell how that rule applies to each chosen technology.

## Backend: .NET 10 and C#

.NET is our main professional stack, and we use it for backends by default. Exceptions appear when the question "What problems will we run into if we use .NET in this project?" gets weighty answers.

There are exceptions to this rule — sometimes we want to get to know a technology closely, and the best way to do that is to build a real project on it. But in that case it is important to understand that the road may turn out long: learning and development happen at the same time, and the code has to be rewritten as you get deeper into the technology.

The goal of this project is to ship a corporate-grade product with a small team, do it fast and cheap, and document the whole journey as a series of articles. So we tried to pick the technologies we know best.

## Database: PostgreSQL 18

PostgreSQL was chosen for the same reason as .NET. We know its performance is enough for everything this project will run into. The extension ecosystem makes it possible to solve almost any task efficiently — full-text search, partitioning, time series.

That trust did not appear out of nowhere. Many of us have worked in big tech, and moving from SQL Server to PostgreSQL in a highly loaded system was a typical case. There were nuances, but overall the system ran stably. Postgres is free — and that matters for ending up with a project that pays for itself. The task tracker domain maps well onto relational database logic, so exotics like `MongoDB` or `Neo4j` were not even considered. About non-standard databases, we even have a small story.

### The mistake: choosing MongoDB and migrating to PostgreSQL

In 2020 MongoDB was at the peak of its hype; it seemed everyone around had started using it. A wish appeared to use MongoDB for the next project: not because the project needed it — rather, we wanted to be on trend. The next project was a web crawler — a SaaS helping to scrape websites through a visual interface. We convinced ourselves that a document store would be a perfect fit for storing the JSONs of collected data, and that the schemaless model would save time on migrations.

In reality the project needed an ordinary relational database: almost all the service's tables (users, projects, crawl histories) had relations that were awkward to query through MongoDB. And those JSONs with crawl results were only used to return them to the user on request — they could have been kept in file storage altogether.

As the project grew, queries that would have been trivial in a relational database kept getting harder. Writing an optimal query could easily take a working day. In the end we got tired of spending time on that, spent a week or two, and rewrote everything to PostgreSQL.

For the future we settled on this: by default we use a relational database; if we have a non-standard case, we start researching what fits that case best.

### High-level database design before choosing one

At the start of a project we think about the data at a high level: which tables the domain might in theory end up with and how they would relate. The point is to imagine the cases we are most likely to face and think through how we would handle them with a specific database.

For Boards, the following situations looked like the hard ones.

**Full-text search.** Searching issues is standard task tracker functionality. PostgreSQL's built-in FTS should be enough for a long time; in the future, the search-related functionality can be moved out to a specialized database.

**Custom attributes.** Users should be able to create their own attributes for issues (for example, a task type, a due date, and so on). The plan is to implement them as a table per attribute type, not as a JSON blob. The attribute definitions live in one table — name, colour, type — with type-specific details in tables like `text_attributes` or `date_attributes`. The attribute values will live in typed tables like `text_attribute_values` with `varchar` values, `date_attribute_values` with `datetime` values, and so on. This storage shape should work correctly with indexes and support user-defined validation for attributes — for example, a text attribute of at most 10 characters, or a number attribute with a 24–42 range.

**Access permissions.** Our people have experience implementing user permission management in a relational database, so there was no reason to expect Boards to be an exception.

All the cases looked implementable on a relational database. Peeking a little ahead — the resulting models can be seen in the [DataAccess/Models folder](https://github.com/Laraue/Laraue.Apps.Boards/tree/main/src/Laraue.Apps.Boards.DataAccess/Models) of the backend repository.

## Frontend: Nuxt 4 and Vue 3

It so happens that none of us is a professional frontend developer. Long ago we tried Vue in our projects (back then it was still version 2) — and we liked it. Since then we simply use it always — we have never noticed problems with it. The main thing is not to forget to add TypeScript to the project — otherwise developing the client side turns into walking through a minefield, where you never know where the next error is waiting for you.

The choice of Nuxt 4 comes down to the framework's rich ecosystem and its stability. We had recent experience with it: this blog is built on Nuxt 4, and it was noticeable how capable the framework is — everything you might need is already implemented, in the core or as a plugin.

In the first iterations the app will run as an SPA, but further on, the following framework features may come in handy.

- **i18n.** The app will ship with Russian and English support. The Russian-speaking audience is one of the largest on Telegram, and it is our native language; English is the default for everyone else.
- **SSR.** In the future we may let users make boards public: for example, to share progress on something. Boards contain text — issue titles, descriptions. That content can be indexed by search engines. Nuxt will let us turn SSR on with a couple of lines.
- **SEO.** The same logic as for public boards: when needed, proper SEO is configured out of the box very quickly.

In the coming iterations we definitely plan to move from an SPA to an MPA. A task tracker is much more convenient when any link inside it can be shared — a board, a specific issue, a filtered view. That implies separate URLs for pages, and Nuxt supports that. But while the vision of the app is not final, we will start the development as an SPA.

## Conclusions

The stack turned out to be familiar to everyone and even boring. On the backend — C# and .NET, which we have known for almost 10 years. The database — PostgreSQL, proven by time and by load. The frontend — Vue + TypeScript + Nuxt, a combination already used in the blog. The novelty should be in the product, not in the tools.

## What comes next

The stack is settled — time to write some code. The next article is about the first backend iteration: the database schema, the Telegram host that saves the user's messages, and the CI pipeline for building the backend and shipping the artifacts to the server.