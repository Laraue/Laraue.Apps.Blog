---
title: Библиотека для парсинга сайтов на C# — строго типизированный краулер для .NET
type: project
tags: [csharp, dotnet, web-scraping, crawler, anglesharp, puppeteersharp, html-parsing]
description: Laraue.Crawling — библиотека для веб-скрапинга на C#, поддерживающая статический HTML, JavaScript-рендеринг и XML. Опишите схему парсинга как типизированный C# код — без разбросанных селекторов и сложной поддержки.
createdAt: 2025-03-04
updatedAt: 2026-09-30
---
Большинство решений для парсинга сайтов на C# работают — до первого изменения структуры сайта.
Один сломанный селектор, и вы снова разбираетесь в коде, который писали полгода назад.
**Laraue.Crawling** — библиотека для веб-скрапинга на .NET, которая позволяет описывать схемы
извлечения данных как чистый, типизированный C# код — для статического HTML,
JavaScript-страниц и XML.

[![NuGet](https://img.shields.io/nuget/v/Laraue.Crawling.Common)](https://www.nuget.org/packages/Laraue.Crawling.Common)
[![Загрузки](https://img.shields.io/nuget/dt/Laraue.Crawling.Common)](https://www.nuget.org/packages/Laraue.Crawling.Common)
[![MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/win7user10/Laraue.Crawling)

---

## Почему схемный подход?

Стандартный подход к парсингу сайтов на C# выглядит так: выбрать парсер (AngleSharp или
HtmlAgilityPack для статических страниц, PuppeteerSharp или Selenium для JavaScript-сайтов),
написать логику выбора элементов и двигаться дальше. Для одноразового скрипта — нормально.
Для долгосрочного проекта — проблема.

Laraue.Crawling оборачивает те же проверенные парсеры в слой схем, который даёт:

- **Строгую типизацию** — модели — это обычные C# record'ы; ошибки типов выявляются на этапе
  компиляции, а не в рантайме
- **Удобство поддержки** — когда сайт меняет структуру, вы обновляете одну схему,
  а не разрозненные строки с селекторами
- **Тестируемость** — схемы — обычные C# объекты; тестируйте их через xUnit или NUnit
  как любой другой класс
- **Единый API** — переключайтесь между статическим и динамическим парсерами,
  не переписывая логику извлечения данных

---

## Быстрый старт

Установите core-пакет и нужный бэкенд парсера:

```bash
dotnet add package Laraue.Crawling.Static.AngleSharp
```

Определите модель и опишите схему:

```csharp
public record ProductPage(string Title, string Price) : ICrawlingModel;

var schema = new AngleSharpSchemaBuilder<ProductPage>()
    .HasProperty(x => x.Title, "h1.title")
    .HasProperty(x => x.Price, ".price")
    .Build();

var parser = new AngleSharpParser(new NullLoggerFactory());
var model = await parser.RunAsync(schema, html);

Console.WriteLine(model.Title);  // строго типизированный результат, без кастов
```

Это полный цикл: определить модель, привязать CSS-селекторы, запустить парсер, получить типизированный результат.

---

## Статические и динамические страницы

### Статический HTML — AngleSharp

Лучший выбор для страниц, не требующих выполнения JavaScript. Быстро, без запуска браузера.

```bash
dotnet add package Laraue.Crawling.Static.AngleSharp
```

```csharp
var schema = new AngleSharpSchemaBuilder<MyModel>()
    .HasProperty(x => x.Title, "h1")
    .HasProperty(x => x.Price, ".price-box span")
    .Build();
```

Подходит, когда HtmlAgilityPack или прямое использование AngleSharp превращается
в слишком много шаблонного кода для структурированного извлечения данных.

### JavaScript-страницы — PuppeteerSharp

Для сайтов с динамически подгружаемым контентом. Под капотом используется настоящий headless-браузер —
тот же подход, что и при прямом использовании PuppeteerSharp, но со слоем схем поверх.

```bash
dotnet add package Laraue.Crawling.Dynamic.PuppeteerSharp
```

```csharp
var schema = new PuppeteerSharpSchemaBuilder<MyModel>()
    .HasProperty(x => x.Title, "h1")
    .HasProperty(x => x.Price, ".price")
    .Build();
```

Замените `AngleSharpSchemaBuilder` на `PuppeteerSharpSchemaBuilder` — модель и привязки
свойств остаются без изменений.

### XML

```bash
dotnet add package Laraue.Crawling.Static.Xml
```

Тот же API, работает с XML-структурами. Подходит для RSS-лент, сайтмапов или API-ответов в формате XML.

---

## Сравнение с прямым использованием парсеров

| | AngleSharp / HtmlAgilityPack напрямую | PuppeteerSharp напрямую | Laraue.Crawling |
|---|---|---|---|
| Строго типизированные модели | ❌ | ❌ | ✅ |
| Единый API для всех парсеров | ❌ | ❌ | ✅ |
| Схема тестируется как C# класс | ❌ | ❌ | ✅ |
| Поддержка JavaScript-страниц | ❌ | ✅ | ✅ |
| Поддержка статических страниц | ✅ | ❌ | ✅ |
| Запуск по расписанию | ❌ | ❌ | ✅ |

Laraue.Crawling не заменяет AngleSharp или PuppeteerSharp — она надстраивается над ними.
Для быстрого одноразового скрипта используйте парсеры напрямую. Если вы строите то,
что придётся поддерживать месяцами, слой схем окупается очень быстро.

---

## Запуск краулеров по расписанию

Библиотека включает базовый класс для запуска краулеров как планируемых ASP.NET-сервисов.
Опишите схему, унаследуйтесь от базового класса — хост берёт на себя расписание, логирование
и управление жизненным циклом:

```csharp
public class ProductCrawlerJob : BaseCrawlerJob<ProductPage>
{
    protected override CrawlingSchema<ProductPage> BuildSchema() => /* ваша схема */;
}
```

Зарегистрируйте в DI-контейнере — и краулер будет запускаться автоматически по расписанию.

---

## Частые вопросы

**Можно ли парсить сайты с JavaScript-рендерингом?**
Да — используйте пакет `Laraue.Crawling.Dynamic.PuppeteerSharp`. Он управляет настоящим
headless Chromium, поэтому обрабатывает ленивую загрузку, SPA и динамический контент так же,
как это делает PuppeteerSharp напрямую.

**Чем это отличается от прямого использования AngleSharp или HtmlAgilityPack?**
Эти библиотеки парсят HTML — они возвращают DOM для навигации. Laraue.Crawling добавляет
слой схем: вы описываете *что* извлечь и *в какое свойство модели*, а библиотека сама
выполняет выборку. Результат типизирован, тестируем и одинаков для всех бэкендов парсинга.

**Можно ли добавить поддержку произвольной древовидной структуры?**
Да. Реализуйте интерфейс парсера для вашего типа узла (XML-парсер можно взять как пример),
добавьте соответствующий schema builder — остальной API работает без изменений.

**Совместима ли библиотека с .NET 9 и .NET 10?**
Да, библиотека ориентирована на современные версии .NET.

---

## Использование в реальных проектах

Laraue.Crawling работает в продакшне в составе проекта [SPB Real Estate](https://github.com/Laraue/Laraue.Apps.RealEstate) —
сервиса мониторинга объявлений о недвижимости, который регулярно обходит два крупнейших
российских сайта с объявлениями:
[Avito](https://github.com/Laraue/Laraue.Apps.RealEstate/blob/main/src/Laraue.Apps.RealEstate.Crawling.Impl/Avito/AvitoCrawlingSchema.cs)
и
[Cian](https://github.com/Laraue/Laraue.Apps.RealEstate/blob/main/src/Laraue.Apps.RealEstate.Crawling.Impl/Cian/CianCrawlingSchema.cs),
извлекая объявления как планируемые задачи.

---

## Пакеты

| Пакет | Назначение |
|---|---|
| `Laraue.Crawling.Common` | Базовые абстракции и интерфейсы |
| `Laraue.Crawling.Static.AngleSharp` | Парсинг статического HTML через AngleSharp |
| `Laraue.Crawling.Dynamic.PuppeteerSharp` | Парсинг JavaScript-страниц через PuppeteerSharp |
| `Laraue.Crawling.Static.Xml` | Парсинг XML-структур |

**Исходный код:** [github.com/win7user10/Laraue.Crawling](https://github.com/win7user10/Laraue.Crawling)
