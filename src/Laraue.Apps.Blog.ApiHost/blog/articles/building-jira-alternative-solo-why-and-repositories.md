---
title: Building a Jira alternative solo — why we are doing it and the repository links
description: Part 1 of building a Telegram task tracker solo with AI. What problem we are solving, why the world needs another task tracker, and which two repositories the article series is built on.
type: article
createdAt: 2026-06-19 13:00
updatedAt: 2026-09-30 07:44
projects: [boards]
tags: [dotnet, nuxt, telegram, task-tracker, devlog, architecture]
nextLink: prototyping-ui-with-ai-before-code
---

> **Architecture First: Building a Jira Alternative Solo, AI-Assisted** — Part 1.
> This is the introductory article of a series about how Laraue Boards is being built: the decisions, the trade-offs, and the mistakes. Development is iterative; each article solves a problem or produces a new version of the app. If you are interested in the product itself rather than how it is made, you can open the [project](https://boards.laraue.com), the page with [detailed description](../projects/boards) or [documentation](https://boards.laraue.com/en/documentation).

The first article is about why we decided to write yet another task tracker, what the product is meant to be, and where to see its source. The engineering part is described in the following articles.

## Why the world needs another task tracker

There are so many task trackers that developers have a running joke — "want to waste your time for nothing? write another task tracker." The niche is extremely crowded, and a new product will not successfully compete with Jira or Linear on features, the number of integrations, or recognition. But a crowded market usually has upsides too — it most likely has users, and they are being monetized successfully, they can be won over under the right conditions, and you can borrow ideas from competitors. We wrote separately, in more detail, about [how we decide what to build](how-we-decide-what-to-build) and why entering a crowded market can be acceptable.

We are going to try to make a task tracker focused on deep integration with Telegram. Here is how it might work: small businesses in the CIS often live in Telegram — all the chats are there, and tasks can be assigned there. Sometimes such companies do not even grow into a CRM: the chat is their CRM. And if a company already has a product for managing tasks, creating those tasks makes employees switch their attention: go to the browser and enter the tasks there. On top of that, the task is not tied to its context — to the place in the chat the task came from.

With Laraue Boards we want to close this gap: the bot will be a participant in the chat, turning its messages into tasks on a Kanban board. There is no tool for those who so far run their business only in Telegram — that is the space Laraue Boards will try to take.

The product may have another direction besides small business — solo users. Telegram is known for its loyal audience, and people often use it to save their own notes. The flexibility of chats is not always enough to manage such notes, and Laraue Boards can be useful in this niche. If notes in a chat become cards on a board, the process of organising them becomes much clearer for the user.

## Where the product will go

The first version of the product will work for solo users. That is the simplest thing to implement and will let us get the first feedback. It should introduce a bot the user creates issues on the boards with.

Next — an organizations mode. Users will be able to work on shared boards within their companies / chats. Some role model should appear, along with the ability to invite people into an organization.

The next idea is for the bot to become a full participant in any chat. A team member mentions the bot in a conversation, the bot creates an issue from that message, and a link to the issue arrives in the chat, while the issue links back to the original message.

After the core functionality stabilizes — embedding AI into the workflow. The user selects a set of issues as context (for example, all the issues in a space for January–March) and can ask a question: sum up the quarter, produce a report in a specific format, find the blocked tasks. Issues become the context for a conversation.

## Where to see the current version

Laraue Boards is available at [boards.laraue.com](https://boards.laraue.com). The current [documentation](https://boards.laraue.com/en/documentation) describes the implemented functionality and how to use it.

### Repositories

The two repositories the article series is built on:

- **Backend** — [Laraue.Apps.Boards](https://github.com/Laraue/Laraue.Apps.Boards) — .NET 10 / C#, PostgreSQL 18
- **Frontend** — [laraue-boards](https://github.com/Laraue/laraue-boards) — Nuxt 4, Vue 3, TypeScript

Further in the series, the articles link to specific files in these repositories.

## What comes next

The next article is about what happens before the first line of code is written: prototyping the interface. There is no point choosing a stack, designing a schema, or defining a data model without knowing how the product looks and what the user will do with it.