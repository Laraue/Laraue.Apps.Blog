---
title: Markdown файлы как REST API на .NET — библиотека Laraue.CmsBackend
type: project
tags: [dotnet, csharp, cms, markdown, api, headless-cms, static-site, nuget]
description: Лёгкая .NET 10 библиотека, которая превращает Markdown файлы с frontmatter в фильтруемый REST API. Строгая типизация, без базы данных, без CMS. Open source, лицензия MIT
createdAt: 2025-11-01
updatedAt: 2026-09-30
---
Строите блог или документацию на .NET и не хотите тащить полноценную CMS? **Laraue.CmsBackend** — это лёгкая .NET 10 библиотека, которая превращает **Markdown файлы из Git-репозитория в запрашиваемый REST API** — с фильтрацией, сортировкой, поддержкой frontmatter и строго типизированными схемами контента. База данных не нужна.

|              |                                                                       |
|--------------|-----------------------------------------------------------------------|
| Язык         | C#                                                                    |
| Фреймворк    | .NET 10                                                               |
| Тип проекта  | Библиотека                                                            |
| Статус       | Активная разработка                                                   |
| Лицензия     | MIT                                                                   |
| NuGet        | ![latest version](https://img.shields.io/nuget/v/Laraue.CmsBackend)  |
| Загрузки     | ![downloads](https://img.shields.io/nuget/dt/Laraue.CmsBackend)      |
| GitHub       | [Laraue.CmsBackend](https://github.com/win7user10/Laraue.CmsBackend) |

---

## Как появилась эта библиотека

Задача была простой: хранить весь контент блога в Markdown файлах в Git-репозитории, отдельно от фронтенда, и запрашивать его через API — с фильтрацией по тегам, сортировкой по дате и без базы данных.

Готовые решения не подошли. Хранить Markdown внутри фронтенда — значит терять разделение ответственности. Полноценные CMS вроде WordPress или Contentful тащат за собой базу данных, админку и хостинговую инфраструктуру, которые блогу разработчика не нужны.

**Результат — третий путь:** Markdown файлы в Git, отдаваемые через типизированный .NET API с поддержкой frontmatter атрибутов.

> Этот блог сам построен на Laraue.CmsBackend. Полный исходный код бэкенда открыт на GitHub: [Laraue.Apps.Blog](https://github.com/Laraue/Laraue.Apps.Blog) — рабочая референсная реализация, которую можно изучить или форкнуть.

---

## Проблемы классических CMS

### Что такое CMS?

CMS (система управления контентом) позволяет создавать и редактировать контент сайта без специальных технических знаний. Популярные примеры — WordPress, Drupal, Joomla.

### Когда CMS — не лучший выбор

CMS ускоряет выход в продакшн: берёт на себя архитектуру, шаблоны, SEO-настройки и админ-панель. Но у неё есть реальные недостатки:

- **Архитектурная привязка:** навязывает конкретную структуру, из которой сложно выйти при нестандартных требованиях.
- **Несовместимость стека:** PHP-based CMS, встроенная в .NET-инфраструктуру, создаёт дополнительные расходы на поддержку и безопасность.
- **Уязвимости безопасности:** популярность крупных CMS делает их приоритетными целями для атак. Известные уязвимости активно эксплуатируются для кражи баз данных и дефейса сайтов.

Для блогов разработчиков, технической документации и проектных сайтов лёгкий файловый подход часто предпочтительнее.

---

## SSR, SSG и проблема SEO в реактивных фреймворках

### В чём проблема

Сайты на реактивных фреймворках (React, Vue, Angular) могут не отдавать полный HTML до начала загрузки страницы. Поисковые краулеры вынуждены выполнять JavaScript, ждать асинхронных запросов и обрабатывать большой объём данных, прежде чем контент станет доступен для индексации. При медленных ответах краулер уходит со страницы раньше, чем успевает проиндексировать весь текст.

### Два стандартных решения

**Server-Side Rendering (SSR)** — сервер получает контент и рендерит полную HTML страницу до отправки клиенту. Краулеры получают готовый HTML, который можно индексировать немедленно — без выполнения JavaScript. Цена — повышенная нагрузка на сервер при каждом запросе.

**Static Site Generation (SSG)** — страницы собираются при деплое и отдаются как статический HTML с JavaScript сверху. Подходит для по-настоящему статичного контента, но **отображение динамических или фильтруемых данных — например, блога с пагинацией и фильтрацией по тегам** — реализуется с трудом или невозможно совсем.

### Урок, который пришлось выучить на практике

Первая версия этого блога запустилась без SSR. Логика казалась разумной: современные поисковые системы умеют рендерить JavaScript, ответы API были быстрыми, а отказ от SSR упрощал инфраструктуру и снижал нагрузку на сервер.

**Это оказалось ошибкой.** На практике страницы индексировались слишком долго — даже при быстрых ответах API. Googlebot действительно умеет рендерить JavaScript, но не в том же темпе, что статический HTML. Новые посты оставались неиндексированными неделями.

Решение оказалось простым: **включить SSR на фронтенде**. При каждом запросе сервер обращается к CMS backend API, получает отрендеренный HTML контент и отдаёт клиенту полностью собранную страницу. Краулеры индексируют её сразу — как обычную статическую страницу.

Вывод: если контент должен находиться через поиск, не стоит рассчитывать на то, что краулеры справятся с клиентским рендерингом в разумные сроки. SSR стоит дополнительной нагрузки на сервер.

---

## Что умеет Laraue.CmsBackend

Библиотека предоставляет API-слой бэкенда для контента на основе Markdown. Основные возможности:

- **Строго типизированные схемы контента** — определяете C# классы с `required` свойствами, которые напрямую映射ся на поля frontmatter; если в Markdown файле отсутствует обязательное поле, приложение падает при запуске, а не в рантайме при запросе
- **Парсинг frontmatter** — YAML поля (title, tags, date, кастомные атрибуты) парсятся в типизированные .NET объекты
- **API фильтрации и сортировки** — запрашивайте контент по любому frontmatter атрибуту, получайте список уникальных тегов в алфавитном порядке, используйте пагинацию
- **Рендеринг Markdown в HTML** — встроенный трансформер конвертирует тело Markdown в HTML с поддержкой генерации внутренних ссылок
- **Git-совместимое хранилище** — контент живёт в `.md` файлах прямо в репозитории; никаких баз данных, миграций и бэкапов

---

## Строгая типизация контента

Одно из ключевых архитектурных решений библиотеки — **валидация схем контента через типы C#**, а не через соглашения или проверки в рантайме.

Каждая категория контента — посты блога, страницы документации, страницы проектов — получает собственный класс, наследующий `BaseContentType`. Свойства с `required` должны присутствовать в frontmatter Markdown файла. Если в каком-либо файле отсутствует обязательное поле, **приложение падает при запуске** — проблема проявляется сразу, а не в виде сломанной страницы в продакшне.

Вот реальный тип контента `Documentation`, используемый в этом блоге ([исходник на GitHub](https://github.com/Laraue/Laraue.Apps.Blog/blob/main/src/Laraue.Apps.Blog.ApiHost/docTypes/Documentation.cs)):

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

`Project` — `required`: каждая страница документации обязана указать, к какому проекту она относится, иначе приложение не запустится. `Description`, `Keywords` и `Order` — опциональные; отсутствующие опциональные поля библиотека обрабатывает корректно.

Такой подход переносит привычное для C# разработчиков мышление о безопасности типов на процесс создания контента. Авторы получают быструю и однозначную обратную связь вместо молчаливых пробелов в ответе API.

---

## Быстрый старт

### 1. Определите тип контента

```csharp
public class Article : BaseContentType
{
    public required string[] Projects { get; init; }
    public required string Description { get; init; }
}
```

### 2. Напишите Markdown контент

```markdown
---
title: О моём проекте
projects: [Project1, Project2]
description: Краткое описание
---
Содержимое статьи в Markdown.
```

Организуйте файлы так, чтобы структура папок соответствовала URL структуре фронтенда:

```
blog/
└── articles/
    ├── article1.md
    └── article2.md
```

### 3. Соберите хост

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

### 4. Добавьте контроллер с типизированными DTO

Вместо того чтобы возвращать сырой `Dictionary<string, object>`, определите явные DTO и передайте их как generic-параметр. Библиотека замапит в ваш DTO только поля из массива `Properties` — ничего лишнего не сериализуется и не отправляется клиенту.

Вот как реальный контроллер блога ([исходник на GitHub](https://github.com/Laraue/Laraue.Apps.Blog/blob/main/src/Laraue.Apps.Blog.ApiHost/Controllers/BlogController.cs)) отдаёт список карточек и детальную страницу, каждый endpoint со своим DTO:

```csharp
// Лёгкий DTO для списков — только поля, нужные карточке на фронтенде
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

// Расширенный DTO для страниц деталей — включает отрендеренный контент и навигацию
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
            Filters = [/* фильтры по тегам и типу контента */]
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

На что стоит обратить внимание:

- **Проекция полей** — массив `Properties` определяет, какие frontmatter поля и вычисляемые значения попадут в ответ. В сериализацию включаются только перечисленные поля, поэтому list-эндпоинты остаются лёгкими даже при богатом типе контента.
- **Поддержка выражений** — `Properties` принимает не только имена полей: `"length(content)"` возвращает количество символов в отрендеренном HTML, а `"format(createdAt, \"dd MMM yyyy\") as createdAt"` форматирует дату на стороне сервера до отправки клиенту.
- **Типизированные generics** — `GetEntities<CardItem>` и `GetEntity<CardDetail>` возвращают полностью типизированные объекты. Сигнатуры контроллеров чистые, документация в Swagger корректная, фронтенд получает стабильный контракт.

Библиотека не диктует форму API — вы сами решаете, какие эндпоинты открывать, какие поля в каждом отдавать и как структурировать DTO.

---

## Реальное применение: этот блог

Laraue.CmsBackend — не демо-проект. На нём работает блог, который вы сейчас читаете. Полный исходный код бэкенда доступен на [github.com/Laraue/Laraue.Apps.Blog](https://github.com/Laraue/Laraue.Apps.Blog) — со структурой папок контента, настройкой контроллеров и CI/CD. Если вы оцениваете библиотеку, это самая прямая ссылка на то, как она работает в продакшне.

---

## Установка через NuGet

```
dotnet add package Laraue.CmsBackend
```

Или найдите `Laraue.CmsBackend` в NuGet Package Manager. Пакет нацелен на **.NET 10**, лицензия — **MIT**.

Исходный код и трекер задач: [github.com/win7user10/Laraue.CmsBackend](https://github.com/win7user10/Laraue.CmsBackend)

---

## Планы развития

Текущая область библиотеки — отдача Markdown контента через API. В планах расширение `Laraue.CmsBackend.Telegram` — инструмент для запуска бота, который будет автоматически публиковать новый контент в Telegram-каналы по настраиваемым критериям, с атрибутами на уровне поста для управления каналами распространения.

---

## Часто задаваемые вопросы

**Что будет, если в Markdown файле отсутствует обязательное frontmatter поле?**

Приложение упадёт при запуске. Типы контента используют ключевое слово `required` C# для полей, которые обязаны присутствовать в frontmatter. Нарушения схемы обнаруживаются сразу при старте, а не молча в рантайме или как `NullReferenceException` в продакшне.

**Нужна ли база данных для использования Laraue.CmsBackend?**

Нет. Контент хранится в `.md` файлах прямо в репозитории. Библиотека читает и парсит их в рантайме — никаких настроек базы данных, миграций и строк подключения не требуется.

**Какую версию .NET поддерживает библиотека?**

Библиотека нацелена на .NET 10. Публикуется как NuGet пакет (`Laraue.CmsBackend`) под лицензией MIT.

**Можно ли фильтровать и сортировать контент по frontmatter полям?**

Да. API `GetEntitiesRequest` поддерживает фильтрацию по любому frontmatter атрибуту и возвращает результаты с пагинацией и сортировкой. Также можно запрашивать агрегированные значения — например, отсортированный список всех уникальных тегов по всем файлам контента.

**Есть ли рабочий пример для изучения?**

Да — блог на [laraue.com/blog](https://laraue.com/blog) работает на Laraue.CmsBackend, полный исходный код бэкенда открыт на [github.com/Laraue/Laraue.Apps.Blog](https://github.com/Laraue/Laraue.Apps.Blog).
