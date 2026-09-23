---
title: Интеграции — подключение Laraue Boards к вашим инструментам
description: Как Laraue Boards интегрируется с Telegram в виде Mini App и бота, а также с AI-агентами вроде Claude через MCP. Нативный вход, привязка групповых чатов, режимы сохранения, inline-поиск, API-ключи и работа с приложением, не выходя из Telegram.
keywords: [интеграция telegram mini app, telegram бот управление проектами, telegram веб-приложение инструменты, интеграция telegram kanban, telegram бот групповой чат, mcp сервер boards, claude mcp коннектор]
type: sectionDefinition
order: 6
createdAt: 2026-04-22
updatedAt: 2026-09-23
---
Laraue Boards построен вокруг Telegram, а также подключается к AI-агентам вроде Claude. Этот раздел описывает, как устроена каждая интеграция и как получить от неё максимум.

## В этом разделе

- [Telegram Mini App](/ru/blog/documentation/laraue-boards/integrations/telegram-mini-app) — использование Laraue Boards нативно внутри Telegram. Автоматический вход, сохранение сообщений из любого чата и mobile-first дизайн.

- [Привязка чата Telegram к Boards](/ru/blog/documentation/laraue-boards/integrations/telegram-linking) — направьте любой чат, личный или групповой, на организацию, спейс, эпик и статус через `/link`.

- [Авто- и ручной режим сохранения](/ru/blog/documentation/laraue-boards/integrations/telegram-save-modes) — выберите, становятся ли сообщения в привязанном чате issues автоматически или только через `/save`.

- [Поиск issues из любого чата Telegram](/ru/blog/documentation/laraue-boards/integrations/telegram-inline-search) — находите issues откуда угодно через `@msgboard_bot запрос`, с фильтрами или произвольным текстом.

- [API-ключи](/ru/blog/documentation/laraue-boards/integrations/api-keys) — создайте долгоживущий ключ, чтобы скрипты, CI или AI-агенты могли авторизоваться от вашего имени.

- [Подключение Claude и других AI-агентов через MCP](/ru/blog/documentation/laraue-boards/integrations/mcp) — просматривайте, создавайте, редактируйте, удаляйте issues и комментируйте их прямо из Claude.