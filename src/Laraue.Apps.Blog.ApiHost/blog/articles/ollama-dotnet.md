---
title: Using Ollama in C# and .NET — Local LLM Integration With Structured Output
type: article
projects: [real-estate, learn-language]
description: How to integrate Ollama with C# and .NET — native HTTP API, structured JSON output, vision model image analysis, and a typed NuGet adapter that generates schemas from C# classes automatically. No cloud API required.
createdAt: 2025-12-26
updatedAt: 2026-09-30
---
**Integrating Ollama with C# and .NET** lets you run open-source language and vision models locally — no cloud API keys, no per-call costs, no data leaving your server. This article covers the native Ollama HTTP API, structured output with JSON Schema, vision model image analysis, and a typed .NET adapter library that generates request schemas automatically from C# classes.

Two production projects already use this approach: the [real estate aggregator](https://apartments.laraue.com) uses Ollama to score apartment photos by renovation quality; the [language learning bot](../projects/learn-language) uses it for vocabulary generation. Both run inference locally with no third-party API dependency.

---

## Why Use Ollama Instead of a Cloud API

Cloud APIs (ChatGPT, DeepSeek, Gemini) are the obvious first choice — until they aren't. Common reasons to run locally:

| Situation                          | Why cloud doesn't work                                                        |
|------------------------------------|-------------------------------------------------------------------------------|
| Processing personal data           | Sending user data to external providers may violate GDPR or other regulations |
| High availability requirements     | Your uptime can't depend on a third-party API's SLA                           |
| Export-restricted countries        | Some AI providers don't operate in certain regions                            |
| Cost-sensitive workloads           | Per-call API pricing becomes expensive at volume                              |
| Offline or air-gapped environments | No internet access by definition                                              |

For MVPs and hypothesis testing, **pre-trained open-source models via Ollama** are the fastest path: swap a model by changing one config string, no retraining required. If the MVP proves viable, you can replace the model with a fine-tuned version later — the integration code stays the same.

[Ollama](https://ollama.com) provides a universal HTTP API over different models. When you start Ollama, it runs a local service on port **11434** by default. Any HTTP client can call it — no SDK required.

---

## The Native Ollama HTTP API

### Text Generation

Send a `POST` to `/api/generate`:

```json
{
  "model": "gemma3:12b",
  "prompt": "Translate the following text to French: 'The apartment has two rooms and a large balcony.'",
  "stream": false
}
```

The response:

```json
{
  "model": "gemma3:12b",
  "response": "L'appartement a deux pièces et un grand balcon."
}
```

To include an image, add the `images` field with a base64-encoded string. Only vision-capable models use it — for text-only models the field is ignored:

```json
{
  "model": "qwen2.5vl:3b",
  "prompt": "Describe what you see in this photo.",
  "stream": false,
  "images": ["<base64EncodedImageBytes>"]
}
```

The response:

```json
{
  "model": "qwen2.5vl:3b",
  "response": "The photo shows a bright living room with white walls, laminate flooring, and large windows."
}
```

### Structured Output With JSON Schema

The `format` field enables **structured output** — the model returns a JSON object matching your schema instead of free text. This is critical for any use case where you need to parse the response programmatically:

```json
{
  "model": "qwen2.5vl:3b",
  "prompt": "Analyze this apartment photo and return your assessment.",
  "stream": false,
  "images": ["<base64EncodedImageBytes>"],
  "format": {
    "type": ["object"],
    "properties": {
      "RenovationRating": {
        "type": ["number"]
      },
      "Tags": {
        "type": ["array"],
        "items": { "type": ["string"] }
      },
      "Description": {
        "type": ["string"]
      }
    }
  }
}
```

The response matches the schema:

```json
{
  "model": "qwen2.5vl:3b",
  "data": {
    "RenovationRating": 0.82,
    "Tags": ["clean", "bright", "new_windows", "modern_kitchen"],
    "Description": "Well-maintained apartment with recent renovation, good natural light, and updated fixtures."
  }
}
```

---

## The Problem With the Native API in Practice

Writing the `format` JSON Schema by hand for every response type is tedious and error-prone. A missing `"type"` wrapper, a misspelled property name, or a wrong nesting level silently produces a response that doesn't match your C# class — and you find out at deserialization time, not at the call site.

The other pain point is boilerplate: every call needs HTTP client setup, JSON serialization, base64 encoding, error handling, and response parsing. None of that is interesting code.

---

## Laraue.Ollama.NET: A Typed .NET Adapter

The [`Laraue.Ollama.NET`](https://github.com/win7user10/Laraue.Ollama.NET) NuGet package wraps the native API with a typed interface. It generates the JSON Schema automatically from your C# class using reflection — so schema changes are picked up automatically without touching any request code.

> **Note:** this package was previously published as `Laraue.Core.Ollama`. It was renamed to `Laraue.Ollama.NET`; update your `PackageReference`/`dotnet add package` command and namespace usings if you're upgrading from the old name.

|           |                                                                      |
|-----------|----------------------------------------------------------------------|
| NuGet     | ![latest version](https://img.shields.io/nuget/v/Laraue.Ollama.NET)  |
| Downloads | ![downloads](https://img.shields.io/nuget/dt/Laraue.Ollama.NET)      |
| GitHub    | [Laraue.Ollama.NET](https://github.com/win7user10/Laraue.Ollama.NET) |

### The Interface

[`IOllamaPredictor`](https://github.com/win7user10/Laraue.Ollama.NET/blob/main/src/Laraue.Ollama.NET/IOllamaPredictor.cs) (namespace `Laraue.Ollama.NET`) exposes three overloads:

```csharp
public interface IOllamaPredictor
{
    // Vision model: structured output + image
    Task<TModel> PredictAsync<TModel>(
        string modelName,
        string prompt,
        string base64EncodedImage,
        Dictionary<string, object>? additionalParameters = null,
        CancellationToken ct = default)
        where TModel : class;

    // Text model: structured output, no image
    Task<TModel> PredictAsync<TModel>(
        string modelName,
        string prompt,
        Dictionary<string, object>? additionalParameters = null,
        CancellationToken ct = default)
        where TModel : class;

    // Raw string response — no schema, no deserialization
    Task<string> PredictAsync(
        string modelName,
        string prompt,
        Dictionary<string, object>? additionalParameters = null,
        CancellationToken ct = default);
}
```

Use the generic overloads when you need structured output parsed into a C# type. Use the raw overload when you want the model's text response directly — useful for freeform generation or when you handle parsing yourself. `additionalParameters` is a new addition: pass through options like `temperature` or `top_p` straight to the underlying Ollama request without dropping to the native HTTP API.

### Installation

```
dotnet add package Laraue.Ollama.NET
```

### Setup

Register the predictor in your DI container:

```csharp
services.AddHttpClient<IOllamaPredictor, OllamaPredictor>((serviceProvider, client) =>
{
    client.BaseAddress = new Uri("http://localhost:11434/");
    // Replace with your Ollama host if running on a separate machine
});
```

### Define Your Response Contract

Any C# class or record works. Properties map to the generated JSON Schema:

```csharp
public record PredictionResult
{
    public required double RenovationRating { get; set; }  // maps to "number"
    public required string[] Tags { get; set; }            // maps to "array" of "string"
    public required string Description { get; set; }       // maps to "string"
}
```

The adapter reflects over `PredictionResult` at call time, builds the `format` JSON Schema, sends the request, and deserializes the response back into `PredictionResult`. Adding a new property to the record immediately affects the next request — no manual schema editing.

### Text Analysis

```csharp
var result = await ollamaPredictor.PredictAsync<PredictionResult>(
    model: "gemma3:12b",
    prompt: "Classify the following text and return structured output.",
    ct: ct);

Console.WriteLine(result.RenovationRating); // 0.74
Console.WriteLine(string.Join(", ", result.Tags)); // "clean, bright, good_location"
```

### Image Analysis

```csharp
var imageBytes = File.ReadAllBytes("apartment.jpg");
var base64Image = Convert.ToBase64String(imageBytes);

var result = await ollamaPredictor.PredictAsync<PredictionResult>(
    model: "qwen2.5vl:3b",
    prompt: "Rate the renovation quality visible in this apartment photo.",
    base64EncodedImage: base64Image,
    ct: ct);
```

---

## Model Selection Notes

- **For text tasks:** `gemma3:12b` and `qwen2.5:7b` are good starting points. Larger parameter counts produce better reasoning; smaller ones run faster.
- **For vision tasks:** `qwen2.5vl:3b` handles image analysis well at modest hardware requirements. Check the model's page on [ollama.com/library](https://ollama.com/library) to confirm vision support before downloading.
- **First call latency:** Ollama downloads the model on first use if it's not already cached locally. Subsequent calls within the default 5-minute idle window load the model from memory and are significantly faster.
- **Model switching:** Change the `model` parameter string — nothing else in your code changes. This is the main reason to use Ollama over fine-tuned model hosting: zero-cost model comparison.

---

## Real-World Usage

The [real estate aggregator](https://github.com/Laraue/Laraue.Apps.RealEstate/blob/main/src/Laraue.Apps.RealEstate.Prediction.AppServices/OllamaRealEstatePredictor.cs) uses `IOllamaPredictor` with `qwen2.5vl` to score apartment photos for renovation quality. Every listing photo gets a `RenovationRating` between 0 and 1, plus arrays of `Advantages` and `Problems` tags that are stored for prompt debugging. The per-listing average across all photos feeds into the final ideality ranking. [How the ranking formula works](../projects/real-estate).

---

## Frequently Asked Questions

**What port does Ollama use by default?**

Ollama listens on port `11434` by default. The base URL for all API calls is `http://localhost:11434`. This can be changed via the `OLLAMA_HOST` environment variable if you're running Ollama on a separate machine or container.

**How does Ollama structured output work in C#?**

Ollama's `format` field accepts a JSON Schema object. The model constrains its output to match the schema before returning. The `Laraue.Ollama.NET` adapter generates this schema automatically from your C# class using reflection — you define the response shape as a C# record, and the adapter handles the rest.

**Can I use Ollama with vision models in C#?**

Yes. Pass base64-encoded image bytes in the `images` field of the request, or use the `PredictAsync<TModel>(modelName, prompt, base64EncodedImage, ct)` overload in the adapter. Only models with vision capability process images — confirm support on the model's page at [ollama.com/library](https://ollama.com/library) before downloading.

**How do I switch between models in Ollama?**

Change the `model` parameter in the API call or `PredictAsync` invocation. No other code changes are required. Ollama handles downloading, loading, and unloading models automatically. This makes it straightforward to compare model quality for your specific task without infrastructure changes.

**Is Ollama suitable for production use?**

Ollama works well in production for latency-tolerant, GPU-bound workloads where data privacy or cost constraints rule out cloud APIs. For latency-sensitive or high-concurrency production systems, evaluate throughput carefully on your target hardware before committing. The architecture pattern used in the [real estate project](building-ai-real-estate-system) — an isolated `GpuWorkerHost` draining a queue — is a practical way to decouple inference throughput from the rest of the application.