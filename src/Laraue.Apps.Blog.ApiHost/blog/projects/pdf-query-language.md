---
title: How to Build a Query Language Interpreter in C# — A Worked Example
type: project
githubLink: https://github.com/win7user10/Laraue.PdfQL
tags: [csharp, dotnet, interpreter, query-language, lexer, parser, ast, transpiler, dsl, mongodb, pdf-extraction]
description: A step-by-step walkthrough of building a query language interpreter in C# — lexer, parser, AST, and pipeline executor — using PdfQL as a real worked example. Open source.
createdAt: 2025-11-01
updatedAt: 2026-09-30
---

Building a **query language interpreter in C#** is one of those projects that sounds intimidating until you break it into parts. This article walks through the architecture and key decisions behind [PdfQL](https://github.com/win7user10/Laraue.PdfQL) — an open source C# library that implements a pipeline-style query language for extracting structured data from PDF documents.

PdfQL is a concept project, not a production library. But the implementation contains a working **scanner, parser, AST, and pipeline executor**, all written in C# and targeting .NET 10 — making it a useful reference for anyone building a domain-specific language or interpreter on .NET.

|              |                                                                  |
|--------------|------------------------------------------------------------------|
| Language     | C#                                                               |
| Framework    | .NET 10                                                          |
| Project type | Library                                                          |
| Status       | Concept                                                          |
| License      | AGPL-3.0                                                         |
| NuGet        | ![latest version](https://img.shields.io/nuget/v/Laraue.PdfQL)  |
| Downloads    | ![downloads](https://img.shields.io/nuget/dt/Laraue.PdfQL)      |
| GitHub       | [Laraue.PdfQL](https://github.com/win7user10/Laraue.PdfQL)       |
| Demo app     | [Laraue.Apps.PdfQL](https://github.com/Laraue/Laraue.Apps.PdfQL) |
| Live demo    | [PDF Extractor](https://laraue.com/pdf-extractor)                |

---

## Why Build a Query Language at All?

Extracting structured data from PDFs in C# — tables, rows, specific cells — typically means writing repetitive, brittle imperative code. You open the document, locate elements by position, loop through rows, apply conditions manually. Every new extraction task is more boilerplate.

The idea behind PdfQL was to express these operations **declaratively**, the same way SQL expresses database queries. Instead of writing imperative C# to find tables where column 4 equals "Name" and return column 1, you write:

```pdfql
select(tables)
    ->filter((item) => item.GetCell(4).Text() == 'Name')
    ->selectMany(tableRows)
    ->map((item) => item.GetCell(1))
```

The library parses this string, builds an execution plan, and runs it against a PDF document. This is the core loop of any interpreter — and building it is the subject of this article.

---

## Why SQL Syntax Wasn't the Right Fit

The first instinct when designing a query language is to reach for SQL-like syntax. It's familiar, widely understood, and tooling support is good.

But SQL is optimized for **relational data with joins**. Documents don't have that shape. You rarely join two PDFs. The primary operations are: select a type of element, filter by condition, transform the result. That's not a `SELECT ... FROM ... WHERE` — it's a **pipeline of stages**.

MongoDB's aggregation pipeline is a closer model: each stage receives the output of the previous one, applies a transformation, and passes results forward. PdfQL adopts this model directly, using `->` as the pipe operator between stages. The syntax is intentionally closer to functional method chaining than to SQL.

If the concept proves out beyond PDFs, the language would be renamed DocQL — the same pipeline model applied to any document format.

---

## The Architecture: Three Layers

A query language interpreter has three distinct jobs:

1. **Scanner** — turn the raw query string into a flat list of tokens
2. **Parser** — turn the token list into a structured tree (the AST)
3. **Executor** — walk the AST and run each operation against the data

PdfQL adds a fourth layer specific to its domain: the **DocumentObjectsExtractor**, which converts raw PDF bytes into a typed sequence of document objects (tables, paragraphs, images, forms) before the query executor runs.

---

## Layer 1: The Scanner

The scanner's job is **tokenization** — breaking the raw query string into meaningful units, discarding whitespace and newlines.

The full token vocabulary is defined in [`TokenType.cs`](https://github.com/win7user10/Laraue.PdfQL/blob/main/src/Laraue.PdfQL/Interpreter/Scanning/TokenType.cs):

```csharp
public enum TokenType
{
    Identifier, String, Integer,
    Comma, Dot,
    LessThan, GreaterThan, LessOrEqualThan, GreaterOrEqualThan,
    Assign, Equal, NotEqual, Not,
    Minus, Plus, Divide, Multiply,
    LeftParentheses, RightParentheses,
    LeftBracket, RightBracket,
    Lambda,       // =>
    NextPipeline, // ->
    False, True, Null,
    New, Eof,
}
```

The [`Scanner`](https://github.com/win7user10/Laraue.PdfQL/blob/main/src/Laraue.PdfQL/Interpreter/Scanning/Scanner.cs) processes the input character by character inside a private `ScannerImplementation` class. The key design pattern: it tracks both an **absolute position** (offset into the raw string for slicing lexemes) and a **relative position** (column within the current line for error reporting). Two-character operators like `->`, `=>`, `==`, and `!=` use a `PopNextCharIf` lookahead helper that conditionally consumes the next character only if a predicate matches:

```csharp
case '-':
    AddToken(PopNextCharIf(c => c == '>') ? TokenType.NextPipeline : TokenType.Minus);
    break;
case '=':
    AddToken(PopNextCharIf(c => c == '>')
        ? TokenType.Lambda
        : PopNextCharIf(c => c == '=')
            ? TokenType.Equal
            : TokenType.Assign);
    break;
```

The scanner returns a `ScanResult` containing both the token array and any `ScanError` objects — so the caller gets structured error information rather than exceptions for bad input.

---

## Layer 2: The Parser and AST

The parser takes the flat token stream and builds an **Abstract Syntax Tree (AST)** — a hierarchical structure that captures the grammar of the query.

The base AST node is minimal by design — [`Expr.cs`](https://github.com/win7user10/Laraue.PdfQL/blob/main/src/Laraue.PdfQL/Interpreter/Parsing/Expressions/Expr.cs) is just an empty record base class:

```csharp
public record Expr
{
}
```

All expression types inherit from it. Using C# `record` types gives structural equality for free, which makes unit-testing the parser clean — expected and actual trees compare correctly without custom equality logic.

A concrete example is [`BinaryExpr.cs`](https://github.com/win7user10/Laraue.PdfQL/blob/main/src/Laraue.PdfQL/Interpreter/Parsing/Expressions/BinaryExpr.cs), which represents any two-operand expression (`==`, `!=`, `<`, `>`, `+`, etc.):

```csharp
public record BinaryExpr : Expr
{
    public BinaryExpr(Expr left, Token @operator, Expr right)
    {
        Left = left;
        Operator = @operator;
        Right = right;
    }

    public Expr Left { get; init; }
    public Token Operator { get; init; }  // carries the actual TokenType and lexeme
    public Expr Right { get; init; }

    public override string ToString() => $"{Left} {Operator.Lexeme} {Right}";
}
```

The `Operator` field stores the full `Token` — not just the operator type — so error messages can report the exact source text and position. The recursive `ToString()` override makes the whole tree printable for debugging, which is invaluable when testing the parser in isolation.

The [`Parser`](https://github.com/win7user10/Laraue.PdfQL/blob/main/src/Laraue.PdfQL/Interpreter/Parsing/Parser.cs) uses **recursive descent** — each grammar rule becomes a method that calls other methods for sub-rules. It's the most readable approach for hand-written parsers and maps cleanly to C# without requiring external parser generator tools like ANTLR.

---

## Layer 3: The Executor

The executor **walks the AST and runs it**. For PdfQL's pipeline model, this means:

1. Start with the full sequence of document objects extracted from the PDF
2. For each stage in the pipeline, apply the stage's operation to the current sequence
3. Return the final sequence

Each stage type maps to a well-known sequence operation:

| Stage | Operation | Equivalent LINQ |
|---|---|---|
| `select(tables)` | Filter by type | `OfType<Table>()` |
| `filter((x) => ...)` | Apply predicate | `Where(x => ...)` |
| `selectMany(tableRows)` | Flatten nested collection | `SelectMany(x => x.Rows)` |
| `map((x) => ...)` | Project to new value | `Select(x => ...)` |

Lambda expression nodes in the AST are compiled into C# `Func<>` delegates at execution time. The parameter name maps to the delegate's argument, and the body expression — which may include `BinaryExpr`, method calls, property access, and literals — is evaluated recursively.

---

## Generating Anonymous Types at Runtime With Reflection.Emit

One of the more unusual parts of the implementation is [`AnonymousTypeRegistry.cs`](https://github.com/win7user10/Laraue.PdfQL/blob/main/src/Laraue.PdfQL/Interpreter/DelegateCompiling/AnonymousTypeRegistry.cs). When the `map` stage projects document objects into new shapes — for example, returning only the text content of a cell rather than the full cell object — the executor needs to construct result types **whose structure isn't known until the query is parsed at runtime**.

The standard C# anonymous type (`new { Name = "x" }`) doesn't work here because it requires the shape to be known at compile time. Instead, `AnonymousTypeRegistry` generates real CLR types dynamically using `System.Reflection.Emit`:

```csharp
public class AnonymousTypeRegistry
{
    private readonly string _moduleName;
    private readonly HashSet<AnonymousTypeProperties> _declaredProperties = new();
    private readonly Dictionary<AnonymousTypeProperties, Type> _anonymousClassNames = new();

    public Type GetAnonymousType(AnonymousTypeProperties anonymousTypeProperties)
    {
        if (_declaredProperties.Add(anonymousTypeProperties))
        {
            var typeName = $"PdfQLAnonymousType_{_declaredProperties.Count}";
            var type = AnonymousTypeBuilder.CreateType(_moduleName, typeName, anonymousTypeProperties);
            _anonymousClassNames.Add(anonymousTypeProperties, type);
        }

        return _anonymousClassNames[anonymousTypeProperties];
    }
}
```

`AnonymousTypeProperties` extends `ReadOnlyDictionary<string, Type>` — the keys are property names, the values are their CLR types. Its `GetHashCode` and `Equals` implementations use XOR-based hashing across all property name/type pairs, so the registry can correctly deduplicate requests for identical shapes: two queries that both project to `{ Text: string }` will reuse the same generated type rather than emitting it twice.

This pattern — generating types at runtime and caching them by structural equality — is **not well documented** in the .NET ecosystem. Most articles on `Reflection.Emit` cover method generation; generating complete named types with properties, registering them in a module, and deduplicating by shape is a much rarer topic. The PdfQL implementation is a working, readable reference for this technique.

---

## The Demo App

The live demo at [laraue.com/pdf-extractor](https://laraue.com/pdf-extractor) lets you test PdfQL queries in the browser — upload a PDF, write a query, and see JSON output. Preset options (extract all tables, extract all images) compile to PdfQL internally.

The web API that backs the demo is in a separate repository: [github.com/Laraue/Laraue.Apps.PdfQL](https://github.com/Laraue/Laraue.Apps.PdfQL). It's a thin ASP.NET Core wrapper around the library — useful as a reference for how to host PdfQL in a web context.

---

## Exploring the Source Code

The full implementation is at [github.com/win7user10/Laraue.PdfQL](https://github.com/win7user10/Laraue.PdfQL). Key files:

- [`Interpreter/Scanning/TokenType.cs`](https://github.com/win7user10/Laraue.PdfQL/blob/main/src/Laraue.PdfQL/Interpreter/Scanning/TokenType.cs) — the complete token vocabulary
- [`Interpreter/Scanning/Scanner.cs`](https://github.com/win7user10/Laraue.PdfQL/blob/main/src/Laraue.PdfQL/Interpreter/Scanning/Scanner.cs) — the scanner implementation
- [`Interpreter/Parsing/Expressions/Expr.cs`](https://github.com/win7user10/Laraue.PdfQL/blob/main/src/Laraue.PdfQL/Interpreter/Parsing/Expressions/Expr.cs) — AST base node
- [`Interpreter/Parsing/Expressions/BinaryExpr.cs`](https://github.com/win7user10/Laraue.PdfQL/blob/main/src/Laraue.PdfQL/Interpreter/Parsing/Expressions/BinaryExpr.cs) — binary expression node
- [`Interpreter/Parsing/Parser.cs`](https://github.com/win7user10/Laraue.PdfQL/blob/main/src/Laraue.PdfQL/Interpreter/Parsing/Parser.cs) — recursive descent parser
- [`Interpreter/DelegateCompiling/AnonymousTypeRegistry.cs`](https://github.com/win7user10/Laraue.PdfQL/blob/main/src/Laraue.PdfQL/Interpreter/DelegateCompiling/AnonymousTypeRegistry.cs) — runtime type generation

The project is under the AGPL-3.0 license.

---

## What's Next

Current implementation covers table extraction with filtering. Planned extensions:

- **Plain text support** — `select(textRows)`, `select(words)`, `select(sentences)`
- **Image support** — return images matching conditions; apply functions like `resize(600, 400)`
- **Refactoring** — decouple the executor from PDF-specific types to fully support the DocQL vision
- **Custom functions** — allow users to register their own functions callable from query lambdas

---

## Frequently Asked Questions

**How does a recursive descent parser work in C#?**

A recursive descent parser maps each rule of the grammar to a C# method. To parse a binary expression like `a == b`, the parser calls `ParseExpression()`, which calls `ParseEquality()`, which calls `ParseComparison()`, and so on down the precedence hierarchy. Each method consumes tokens it recognises and delegates to lower-precedence rules for sub-expressions. The result is a call stack that naturally mirrors the structure of the AST being built — making the code easy to read and the grammar easy to extend.

**Is PdfQL ready for production use?**

No — it's a concept project. The table extraction pipeline works and is covered by tests, but the library is not maintained with production reliability in mind. For production PDF extraction in C#, consider PdfPig or iTextSharp. PdfQL is most useful as a **reference implementation** for building interpreters and DSLs in C#.

**Why not use ANTLR or another parser generator?**

Parser generators are a good choice for complex grammars. PdfQL's grammar is simple enough that a hand-written recursive descent parser is easier to read, debug, and modify — and writing it by hand is itself the educational point. The full parser fits in a few hundred lines of C#.

**What's the difference between a transpiler and an interpreter?**

An interpreter executes the AST directly at runtime, as PdfQL does. A transpiler translates the AST into source code of another language (e.g. compiling PdfQL to LINQ expressions or C# code). The scanner and parser layers are identical for both — only the backend differs. PdfQL's architecture could be extended into a transpiler by replacing the executor with a code emitter.

**Why generate anonymous types dynamically instead of using `Dictionary<string, object>`?**

A dictionary would work, but it loses type information — you can't use reflection or expression trees against it in a meaningful way, and serialization produces less useful output. Generating a real CLR type with named, typed properties means the result behaves like any other .NET object: it serializes cleanly to JSON, supports property access via reflection, and can participate in further LINQ operations.

**Can this approach work for formats other than PDF?**

Yes — and that's the long-term design intent. The pipeline executor is already decoupled from the PDF extractor. Adding support for a new document format means implementing a new `DocumentObjectsExtractor` for that format; the query language and executor are reused unchanged.
