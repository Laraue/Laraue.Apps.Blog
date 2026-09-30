---
title: Laraue Boards — an open source task tracker with Telegram integration and a lightweight Jira alternative
type: project
tags: [telegram, task-tracker, kanban, project-management, open-source, saved-messages, jira-alternative, trello-alternative, asana-alternative, clickup-alternative, monday-alternative, linear-alternative]
description: An open source task tracker that turns Telegram messages into cards on a kanban board. Send a message to the bot, get a card you can work with in the web app or the Mini App. Free, and the code is open.
createdAt: 2026-04-16
updatedAt: 2026-09-30
---

Telegram users often use the messenger as a place to keep thoughts, links, and photos — in Saved Messages or in separate chats. The problem is organising them afterwards. A message can be tagged, which helps with search and filtering in simple cases, but the more chats and messages there are, the harder it gets to tell what is still relevant and to find your way between them.

There is also a group of users who use Telegram as the chat for running a small business. This is common in the CIS countries. Why? It is very fast and free. In such cases a company starts with one chat, and later there are more and more of them. The chats become the place both for talking and for assigning tasks and checking they get done. What was convenient at the start becomes harder and harder to keep under control. And if the company does decide to move its tasks into some task tracker, that tracker will not be synchronised with the original chats in any way.

Laraue Boards tries to close these gaps, keeping the familiar chats and adding a visualisation of them on kanban boards. The main flow: sending a message to the bot [@msgboard_bot](https://t.me/msgboard_bot) creates a card on a board, which you can reach through the Telegram Mini App or the separate [web app](https://boards.laraue.com), using Telegram authentication. The main flow is free, and the code is fully open.

## The idea behind it

Most task trackers assume that tasks are created by a project manager and handed to someone. Laraue Boards is based on the idea that tasks appear during a conversation. Marking the right message in a chat is enough to turn it into a card.

## How it works

**Sending a message = a new card.** Forward something to the bot, or write it yourself. The bot saves the message and reacts with a 👍 emoji. We tell the story of [how we arrived at this flow](../articles/telegram-saved-messages-bot-lesson) in a separate article.

**Photos, videos, and albums are saved too.** An album of images or videos sent to the bot becomes a single card on the board. We do not store the original files — when you open them, they are requested from Telegram, in the original quality. How the storage works is in the article [about media and file storage](../articles/telegram-bot-file-storage-stream), and how saving an album is implemented is in the article [about handling media groups](../articles/telegram-media-group-album-bot).

**Editing a message updates the card.** Made a typo in a Telegram message, or attached the wrong photo? Edit the message in the chat and the card on the board changes with it. The bot's reaction changes from 👍 to ❤, which means the edit was handled successfully.

**The chat with the bot stays a chat.** The bot does not spam it with questions, buttons, and confirmations — which means the built-in search still works over it. To simply find last week's note, you do not have to open the full version of the app.

**Organise the tasks when you have time for it.** Open the Mini App from the bot or the web version in a browser, and work with a full kanban board: sort out the backlog, drag cards between columns, group them into epics. Every card holds a link to the message in the chat, so you can see the context it was created in.

**The bot is a full participant in any chat.** [Link a chat](https://boards.laraue.com/en/documentation/integrations/telegram-linking) — private or group — to a specific board with `/link`, choosing whether to [save messages automatically or only on the `/save` command](https://boards.laraue.com/en/documentation/integrations/telegram-save-modes). In any Telegram chat you can also [find an issue with inline search](https://boards.laraue.com/en/documentation/integrations/telegram-inline-search) — type `@msgboard_bot` and a query, without opening the app.

## Spaces, issue keys, and permissions

Boards are grouped into **spaces**. A space is a separate project that brings a group of epics together. In practice this means the boards belonging to one project do not get mixed up with the boards of another.

**Every issue has a key**, sequential within a single space: an issue is `WRK-42`, and it can be referenced in chats or in documentation.

**Organizations** let several people work on the same project. **Permissions** are configured per operation and per access level: creating, updating, and deleting are configured separately for spaces, epics, and issues. Admin rights are granted separately. We tried to combine the functionality while still avoiding a huge configuration matrix.

**Custom attributes** are created by an admin at the organization level and are then used on issues by ordinary users. These can be "task type", "due by", or whatever your particular case needs. Text and list attributes are supported today; "date" and "number" will appear in the coming versions.

## Works inside Telegram and outside the messenger

Laraue Boards can be opened inside Telegram as a Mini App, with no extra authorization needed. For those who want to work with the app in a browser, it is available at [boards.laraue.com](https://boards.laraue.com), with login through the Telegram Login Widget. Both versions work absolutely identically; more on how that is implemented is in the article [Telegram Login Widget vs Mini App auth](../articles/telegram-login-widget-dotnet-auth).

## Open source and public development

The backend and frontend repositories are open to study:

- [Laraue.Apps.Boards](https://github.com/Laraue/Laraue.Apps.Boards) — .NET 10 / C#, PostgreSQL 18
- [laraue-boards](https://github.com/Laraue/laraue-boards) — Nuxt 4, Vue 3, TypeScript

Users wrote to us that the app interested them, but that they were not ready to trust sensitive information to a product they did not know. That is a fair point, so we decided to keep the code open — you can always check what happens to a message after it is sent.

We also tell the story of how the product is being built, in the [Architecture First](../articles/building-jira-alternative-solo-why-and-repositories) series — we try to be honest and to describe even the things that went wrong. The bot that asked too many questions, for instance, and [the rollback of that feature](../articles/telegram-saved-messages-bot-lesson). Or [the database decision that turned out to be wrong](../articles/issue-board-telegram-mini-app). Every article links to the actual code.

People do not usually talk about their mistakes, but we believe that admitting them builds trust more strongly than an ad bought from a well-known blogger.

## Who the product suits

**People who keep notes in Saved Messages** and have started noticing it is getting harder and harder to find their way around them.

**Small teams living in Telegram** — where the discussion happens in the chats, and a separate task tracker is inconvenient because of the constant context switching.

**Small businesses in the CIS**, where tasks are written as text in chats and they have not grown into a CRM yet.

**Anyone who finds Jira too complex** for the amount of work they currently have.

## Laraue Boards vs Jira

|                       | Laraue Boards                         | Jira                                          |
|-----------------------|---------------------------------------|-----------------------------------------------|
| Setup time            | Minutes                               | Days                                          |
| Learning curve        | Near zero                             | Steep                                         |
| Telegram integration  | Native                                | None                                          |
| Task creation         | From messages already sent / manually | Manual entry                                  |
| Pricing               | Free                                  | Per seat, once the team reaches 10 people     |
| Mobile experience     | Full-featured                         | Usable, but heavy                             |
| Customization         | Boards, columns, attributes           | Extremely deep (often excessive)              |
| Source code           | Open                                  | Closed                                        |
| Best for              | Small teams and solo users            | Enterprise with dedicated managers            |

Jira is an excellent tool when there is a dedicated project manager, a complex organization with strict audit requirements, and a budget to support all of it. For everyone else it is often excessive.

Laraue Boards is free. Paid plans will appear later, together with the AI features — but everything described above will stay free.

## Alternatives to Laraue Boards: how it compares to other trackers

Beyond Jira and Trello, there are a few widely used task trackers Laraue Boards naturally gets compared to. Here is an honest comparison on the points that matter most for a small team or a solo user.

### Laraue Boards vs Asana

Asana is a mature work management platform with timelines, dependencies, portfolios, and reporting aimed at cross-functional teams and larger organizations.

- **Task source.** In Asana a task is created manually through a form or a template. In Laraue Boards a card appears from a message you already sent in Telegram — there is nothing to re-enter.
- **Learning curve.** Asana's power comes with a real onboarding cost: projects, custom fields, and rules take time to set up properly. Laraue Boards is ready to use right after the first message to the bot.
- **Best for.** Asana suits organizations with a dedicated admin who owns the workspace setup. Laraue Boards suits teams and solo users who already talk in Telegram and do not want a separate task-entry process.

### Laraue Boards vs ClickUp

ClickUp bundles tasks, docs, goals, chat, and dashboards into one highly configurable all-in-one workspace.

- **Scope.** ClickUp tries to cover most of a team's tools at once (tasks, docs, chat, time tracking). Laraue Boards focuses on a single scenario — turning Telegram messages into manageable cards.
- **Task creation.** In ClickUp a task is created manually in the app, same as in most trackers. In Laraue Boards the source can be your Telegram conversation, which removes an extra step for teams that already discuss tasks in chat.
- **Configuration overhead.** ClickUp's flexibility means there are a lot of settings to get right before the team can use it comfortably. Laraue Boards works out of the box, with customization kept to boards, columns, and attributes.

### Laraue Boards vs Monday.com

Monday.com is a visual work OS built around customizable boards, automations, and dashboards, popular with marketing and ops teams.

- **Automation model.** Monday.com connects to Telegram through third-party integrations (Zapier, Make) that need to be configured. Laraue Boards has native Telegram integration — a card is created the moment a message reaches the bot.
- **Source of truth.** With Monday.com plus an automation, the conversation and the tasks live in two separate systems. In Laraue Boards the chat stays the source of truth: a card is a link back to the original message.
- **Pricing.** Monday.com is priced per seat and scales with team size. The core functionality of Laraue Boards is free.

### Laraue Boards vs Linear

Linear is a fast, keyboard-driven issue tracker built for software teams, with a strong focus on cycles, triage, and a clean workflow.

- **Audience.** Linear is purpose-built for engineering teams shipping software with sprints and a structured triage process. Laraue Boards is aimed more broadly — at anyone whose work starts as a message in a chat, engineering or not.
- **Issue creation.** Linear issues are created manually in its app or through integrations like GitHub. In Laraue Boards forwarding a message to the bot is enough — no separate tool to switch into.
- **Source code.** Linear is closed source. Laraue Boards is fully open, so you can check exactly what happens to a message after you send it.

## What is coming

Planned — AI that uses the issues as context. You will be able to select the issues in a space for January to March and ask it to sum up the quarter, produce a report, or find the tasks that have not moved in a long time. This is where the paid plans will come in, but the core functionality will stay free.

The attribute types will keep expanding. "Date" and "number" are coming soon. In later versions, more exotic ones are not out of the question — geographic coordinates, say, which could then be marked on a map.

We are also planning to let people sign in with a Google account. This is for teammates who do not have Telegram but still need to be participants in a shared organization or space — they would get access without having to install the messenger.

## Common questions

**Is it free?**
Yes. Everything described on this page is free. Paid plans will appear later, together with the AI features; the basic functionality will stay free.

**Is the project really open source?**
Yes — both the [backend](https://github.com/Laraue/Laraue.Apps.Boards) and the [frontend](https://github.com/Laraue/laraue-boards) are open. You can find out what happens to a message after it is sent.

**How is this different from plain Saved Messages?**
Saved Messages is a great place to save something quickly, but not always a good place to find it later. Telegram offers few ways to organise notes, and Laraue Boards tries to fix that by adding a visual interface for working with your saved messages.

**How is this different from connecting Telegram to Todoist or Trello with an automation?**
The depth of the integration with the messenger. You keep working through your Telegram account, with no extra registrations. The chat stays the source of truth for the boards.

**Do I have to use it inside Telegram?**
No. You can work both in the Telegram Mini App and in the ordinary web app at [boards.laraue.com](https://boards.laraue.com), using the same Telegram account.

**Do I need a password or an account?**
No, the login is through Telegram. In the Mini App you are authorized based on your messenger account, and the web version has a Telegram login button.

**Can it be used by a team?**
Yes. Create an organization, share the invite link, and configure permissions for the operations on spaces, epics, and issues.

## How to try it

Open [@msgboard_bot](https://t.me/msgboard_bot) in Telegram, or the web app at [boards.laraue.com](https://boards.laraue.com). A short overview of the product is on the [Laraue Boards page](https://boards.laraue.com), and the [documentation](https://boards.laraue.com/en/documentation) describes all the functionality and how to use it.
