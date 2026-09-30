---
title: Building a Vocabulary Learning Telegram Bot with C# and .NET 9
description: A technical deep-dive into the architecture of an open-source Telegram vocabulary bot — covering the C# / .NET 9 stack, AI-powered auto-translation pipeline, data model, and local development setup.
type: article
createdAt: 2025-04-17
updatedAt: 2026-09-30
projects: [learn-language]
---
**[Laraue.Apps.LearnLanguage](https://github.com/Laraue/Laraue.Apps.LearnLanguage)** is an open-source Telegram bot for learning vocabulary in multiple languages. This article covers the architecture, design decisions, and technical details behind the project — useful reading if you're building a Telegram bot in C#, designing a data pipeline with AI translation, or just curious how a production language-learning app is structured.

The deployed bot is [@learn_lang_bot](https://t.me/learn_lang_bot).

---

## Tech Stack

| Layer | Technology |
|---|---|
| Language | C# |
| Framework | .NET 9 |
| Bot framework | Telegram.NET |
| AI translation | Ollama (local LLM inference) |
| Data format | JSON (word/translation definitions) + EF Core migrations |
| License | MIT |

The project is a monorepo containing two runnable applications and a shared data access layer.

---

## Repository Structure

```
src/
  Laraue.Apps.LearnLanguage.Host/           # Telegram bot (web API host)
  Laraue.Apps.LearnLanguage.AutoTranslator/ # Console app for AI translation
  Laraue.Apps.LearnLanguage.DataAccess/     # Shared EF Core context, migrations, seed data
tests/
  Laraue.Apps.LearnEnglish.IntegrationTests/
```

The two apps share the `DataAccess` project, which owns the database schema, migrations, and the canonical word/translation data files.

---

## Application 1: TelegramApiHost

The main application is a .NET 9 web API that handles incoming Telegram updates via [Telegram.NET](https://github.com/TelegramBots/Telegram.Bot). It uses the **long-polling** mode for local development and webhook mode in production.

### Bot Commands & Access Control

Commands are split into two groups:

- **Public commands** — available to all users (start quiz, view words, settings, progress stats)
- **Admin commands** — restricted to configured admin user IDs (e.g. triggering a re-seed, inspecting state)

This is a common Telegram bot pattern: a middleware layer checks `update.Message.From.Id` against an admin list before routing to admin handlers.

### Quiz Logic

The quiz session pulls 20 words per round. The word selection algorithm tries to maintain a balance across three buckets:

- Words the user hasn't encountered yet
- Words seen recently (short-term reinforcement)
- Words seen a while ago (long-term recall check)

This approximates a lightweight spaced repetition system without the overhead of a full SRS scheduler. The wrong-answer pool is fed back into subsequent rounds.

### Language Pair Selection

On first use, the bot asks users to choose a language pair. The preferred pair can be saved in settings to skip the prompt in future sessions. This is stored per-user in the database.

---

## Application 2: AutoTranslator

The `AutoTranslatorApp` is a standalone console application that scans `translations.json` for words that are missing translations in one or more target languages, then fills them using a locally running **Ollama** instance.

### Translation Pipeline

```
translations.json
       ↓
Find words where translation[language] == null
       ↓
Send to Ollama (local LLM)
       ↓
Write result back to translations.json
       ↓
Create EF Core migration
       ↓
Applied automatically on next app startup
```

Using a local LLM (via Ollama) rather than a paid API keeps the translation cost at zero and avoids network dependencies during batch runs. Translations can also be corrected manually by editing `translations.json` directly.

**Earlier versions used the Google Translate API** (added June 2024). The switch to Ollama-based translation happened in August 2025, enabling higher-quality contextual translations and eliminating the API key dependency.

---

## Data Model

### Word & Translation Files

All word data lives as JSON files inside the `DataAccess` project. This is an intentional design decision — it makes the data:

- **Version-controlled** alongside the code
- **Pull-request friendly** — anyone can submit new words or correct translations via GitHub
- **Auditable** — the full history of every word change is in git

The two key files are:

**`translations.json`** — the master word list. Each entry includes the English word, its CEFR level, associated topics, and a translations map keyed by language code:

```json
{
  "word": "resilient",
  "cefr": "B2",
  "topics": ["personality", "general"],
  "translations": {
    "ru": "устойчивый",
    "de": "belastbar",
    "fr": null
  }
}
```

A `null` value signals to `AutoTranslatorApp` that the translation is missing and should be generated.

**`languages.json`** — defines the supported language pairs. Adding a new language means adding an entry here and running an EF Core migration.

### Database

The word data is seeded into a relational database via EF Core. On each startup, the host checks for new entries in the JSON files and applies them. This means deploying new words is as simple as shipping a new build — no manual database scripts.

---

## Adding New Words or Languages

The repo README documents the contributor workflow:

### Adding words

1. Edit `translations.json`
2. Create a migration:
```bash
cd src && dotnet ef migrations add MigrationName \
  -p Laraue.Apps.LearnLanguage.DataAccess \
  -s Laraue.Apps.LearnLanguage.Host -v
```
3. New translations are applied automatically on the next app run.

### Adding a language

1. Edit `languages.json`
2. Run the same migration command above.

No other code changes are needed — the translation pipeline, quiz mode, and language selector all pick up new languages dynamically.

---

## Local Development

Running the bot locally uses Telegram's long-polling mode (no webhook or public URL required):

1. Create a bot with [@BotFather](https://t.me/BotFather) and copy the token.
2. Create `appsettings.Development.json` in `Laraue.Apps.LearnLanguage.Host`:

```json
{
  "Telegram": {
    "Token": "your_bot_token_here"
  }
}
```

3. Run `Laraue.Apps.LearnLanguage.Host`.
4. Send `/start` to your bot in Telegram.

For the AutoTranslator, you'll also need Ollama running locally with a supported model pulled.

---

## CI/CD

The repository includes GitHub Actions workflows (`.github/workflows/`) for automated build and test runs. Integration tests live in `tests/Laraue.Apps.LearnEnglish.IntegrationTests/`.

---

## Project Timeline

Understanding how the architecture evolved helps explain some of the current design choices:

| Date | Change |
|---|---|
| Jan 2023 | First version: word list view + manual "mark as learned" buttons |
| Jan 2024 | CEFR level browsing added |
| Feb 2024 | Architecture refactored to support multiple language pairs |
| Jun 2024 | AutoTranslatorApp added (Google Translate API) |
| Aug 2025 | Switched to Ollama for local AI translation |
| Sep 2025 | Quiz mode shipped |
| Feb 2026 | v1.0.0 release — quizzes by CEFR level |

---

## What's Coming

The planned roadmap includes:

- **Flexible quiz filtering** — narrow the quiz word pool by topic or CEFR level
- **AI-generated context sentences** — combine recently learned words into short texts to reinforce long-term memory
- **Curated topic packs** — travel-focused word sets (airport, restaurant, transport) for practical pre-trip learning

---

## Contributing

The project is MIT-licensed and open to contributions. The most common contribution is editing `translations.json` to add missing translations or correct existing ones — no C# knowledge required. For feature contributions, the architecture is clean and well-separated, making it straightforward to add new bot commands or extend the quiz engine.

- **Repo:** [github.com/Laraue/Laraue.Apps.LearnLanguage](https://github.com/Laraue/Laraue.Apps.LearnLanguage)
- **Live bot:** [@learn_lang_bot](https://t.me/learn_lang_bot)
- **Landing page:** [laraue.com/learn-language-bot](https://laraue.com/learn-language-bot)
