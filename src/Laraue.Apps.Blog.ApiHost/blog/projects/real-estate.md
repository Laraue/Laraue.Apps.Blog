---
title: AI Apartment Search for Saint Petersburg — Ranked by Photo Quality
type: project
projectType: application
githubLink: https://github.com/Laraue/Laraue.Apps.RealEstate
applicationLink: https://apartments.laraue.com
tags: [real-estate, apartment-search, saint-petersburg, ai-ranking, cian-alternative, renovation-quality, telegram, notifications]
description: Stop scrolling bad listings. This free tool crawls Saint Petersburg real estate and ranks every apartment by renovation quality using AI photo analysis. Filter by district, price, rooms, and AI score. Get Telegram notifications for new matches.
createdAt: 2025-11-01
updatedAt: 2026-09-30
---
Scrolling through hundreds of Saint Petersburg apartment listings is exhausting. Half the photos are dark, blurry, or staged to hide problems. Prices vary wildly for what looks like similar flats. And unless you've spent years in the market, it's nearly impossible to tell a genuinely good deal from a bad one just from the listing page.

**[AI Apartments Aggregator](https://apartments.laraue.com)** is a free tool that automatically collects Saint Petersburg real estate listings and ranks them by renovation quality — using a local AI model that analyzes every photo. The best-condition apartments appear first. You skip the rest.

> If you're a developer and want to understand how this was built — the PuppeteerSharp crawler, Ollama vision integration, and ranking formula — see the [technical deep-dive](../articles/building-ai-real-estate-system)

---

## The Problem With Apartment Hunting in Saint Petersburg

Platforms like Cian and Avito sort listings by recency or price. That's useful for sellers. It's useless for buyers and renters who care about condition.

A flat listed yesterday at 7M rubles could have peeling wallpaper, a broken bathroom, and photos taken in the dark. A flat listed three weeks ago at the same price could be freshly renovated, bright, and worth visiting immediately. The standard feed shows them in the same position — or puts the newer, worse one first.

Real estate professionals develop an eye for this over years. They glance at a set of photos and immediately estimate renovation quality, spot red flags, and gauge whether a price is fair for the district and condition. Regular buyers and renters don't have that experience. **AI photo analysis now makes it accessible.**

---

## How It Works

The system runs a four-step pipeline automatically, around the clock:

**Crawl listings every 4 hours.** The app scrapes Saint Petersburg apartment listings from real estate aggregators, collecting price, district, number of rooms, floor, square footage, and all available metadata. Only new listings since the last run are added — the database stays current without re-processing the full catalogue.

**Download and store photos.** For each listing, all gallery photos are fetched and stored locally for analysis.

**AI photo scoring.** A local AI vision model (Ollama with qwen2.5) analyzes each photo and rates it for renovation quality — new finishes vs. worn surfaces, cleanliness, natural light, visible damage or neglect. The listing's final renovation score is the average across all its photos. Listings with too few photos are excluded from the ranking to avoid noise from a single unrepresentative image.

**Compute ideality score.** Photo quality alone doesn't make a good deal. The final **ideality score** combines renovation rating with location factors: proximity to a metro station and distance from the city centre. The further an apartment falls from "ideal" on any of these axes, the more penalties accumulate and the lower its score.

No cloud APIs. No data sent to third parties. Everything runs locally.

---

## Browse and Filter

The ranked results are available at [apartments.laraue.com](https://apartments.laraue.com). Available filters:

- **Rooms** — 1-room, 2-room, 3-room, studio
- **Price range** — set min and max
- **District** — filter by Saint Petersburg district (Петроградский, Московский, Василеостровский, Центральный, and others)
- **AI score threshold** — show only listings above a minimum renovation quality floor
- **Sort** — by ideality score, AI score, price, or listing date

---

## Telegram Notifications

Beyond the web UI, the app integrates with Telegram for two types of delivery:

**Personal selections.** Configure a custom filter — your price range, preferred rooms, minimum AI score, chosen districts — and the bot sends you matching listings on your chosen schedule. Results paginate inside Telegram using inline buttons, so you can browse through matches without leaving the app.

**Public channel.** A curated feed of high-scoring listings (renovation rating ≥ 7, price 5–9M rubles) posts automatically at regular intervals. Subscribe to see the best value apartments in Saint Petersburg as they appear.

---

## Who This Is For

**Apartment hunters in Saint Petersburg** who are tired of manually sifting through low-quality listings and want to focus their viewing time on apartments actually worth the trip.

**Renters** comparing renovation quality across listings at a similar price point — the AI score makes that comparison instant instead of subjective.

**Investors** looking for underpriced properties in good condition relative to their asking price, especially in districts with strong rental demand.

---

## Limitations Worth Knowing

This is a working prototype used informally, not a commercial product:

- **Prediction errors happen.** Photo scoring works well on average, but individual photos — especially unusual angles, very dark shots, or staged furniture-heavy interiors — can produce wrong scores. The per-listing average smooths this out across multiple photos.
- **Saint Petersburg only.** The crawler schema is written for Cian's SPb listings. Other cities would need separate implementations.
- **Not a replacement for a viewing.** The AI score reflects photo quality, not the actual apartment condition. It filters out clearly bad listings efficiently, but the decision to buy or rent still requires an in-person visit.

---

## Try It

Real data, real AI scores, updated every 4 hours from live Saint Petersburg listings.

**[Open the app at apartments.laraue.com](https://apartments.laraue.com)**

The project is open source (MIT license) at [github.com/Laraue/Laraue.Apps.RealEstate](https://github.com/Laraue/Laraue.Apps.RealEstate). The most useful contributions are new crawler schemas for additional real estate sources, or improvements to the Ollama prompt for more accurate renovation scoring.

---

## Frequently Asked Questions

**Is the app free?**

Yes. There are no paid tiers, no sign-up requirements, and no limits on browsing or filtering.

**How often are listings updated?**

The crawler runs every 4 hours. New listings appear in the ranked results after the next crawl and scoring cycle completes.

**How accurate is the AI photo scoring?**

The model performs well on average across a listing's full photo set. Individual photo predictions can be wrong, particularly for dark, ambiguous, or heavily staged photos. The per-listing average across multiple photos is significantly more reliable than any single prediction.

**Can I get notified about new listings matching my criteria?**

Yes — via the Telegram bot. Configure a personal selection with your price range, rooms, preferred districts, and minimum AI score, and the bot will send you matching new listings on your chosen schedule.

**Does the app cover all Saint Petersburg districts?**

The crawler collects listings across all Saint Petersburg districts available on the source platform. You can filter by specific district in the web UI.