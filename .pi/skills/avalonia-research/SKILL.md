---
name: avalonia-research
description: "Mandate up-to-date web research for any Avalonia UI work because the project uses Avalonia v12+, which is brand-new in 2026 and has breaking changes, new bugs, and workarounds not present in training data."
---

# avalonia-research

Use this skill **every time** you are asked to work on, investigate, or answer questions about Avalonia UI in this repository — including controls, views, viewmodels, styling, rendering, windowing, input, threading, application lifetime, packaging, dependencies, build issues, or third-party Avalonia libraries.

## Why this skill exists

- This project targets **Avalonia UI v12+**.
- v12 is brand-new in 2026 and contains breaking changes, renamed APIs, new platform behavior, and unresolved bugs that are not reliably reflected in offline training data.
- Avalonia v11 and older documentation, blog posts, Stack Overflow answers, GitHub issues, and NuGet packages may be **outdated or actively wrong** for v12.
- Real fixes and workarounds are often only documented in recent GitHub issues, pull requests, release notes, or the official docs.

## Mandatory research rule

**Before using any Avalonia API, package, pattern, or workaround, you must verify it against current v12 sources.**

Always perform at least one of the following:

1. **Official docs** — search `https://docs.avaloniaui.net` for the topic and confirm the page applies to v12.
2. **GitHub issues / PRs** — search `https://github.com/AvaloniaUI/Avalonia/issues` and pull requests for the exact bug, exception, or behavior.
3. **Release notes** — check `https://github.com/AvaloniaUI/Avalonia/releases` for v12-specific breaking changes and fixes.
4. **NuGet / package docs** — verify any third-party Avalonia package explicitly supports v12 before adding it.

Use the `WebSearch` tool with queries that include `Avalonia 12` or `Avalonia v12` so results are time-biased toward current information.

## What to reject

Do **not** use or recommend any of the following without re-verifying against v12:

- Avalonia v11, v10, or earlier documentation, samples, or migration guides as authoritative.
- Stack Overflow answers, blog posts, or tutorials from 2025 or earlier unless they explicitly mention v12.
- NuGet packages whose latest stable version only targets Avalonia v11 or lower.
- Old WPF-isms, workarounds, or P/Invoke patterns copied from older Avalonia code without checking whether v12 has a built-in replacement.

If you find conflicting guidance, prefer the **newer source** and the **official Avalonia docs / GitHub repo** over third-party content.

## Workflow

1. **Identify the Avalonia topic** (control, API, lifecycle event, renderer, theme, package, etc.).
2. **Search the web** for the current v12 behavior, known issues, and recommended fixes.
3. **Check the local codebase** for how v12 is already being used in `CCP.Avalonia*/` and `CCP.Core/`.
4. **Only then** propose or implement a change.
5. **Cite** the sources you used in your response (URLs or short descriptions) so the result is auditable.

## Good search queries

- `Avalonia v12 <topic>`
- `Avalonia 12 <control> example`
- `Avalonia 12 <exception>`
- `site:docs.avaloniaui.net <topic>`
- `site:github.com/AvaloniaUI/Avalonia <topic>`

## When to escalate

If the web research contradicts the existing project code, or if no reliable v12 information exists, stop and tell the user before making a change. Do not guess based on v11 experience.

## Example invocation

> "Why is this Avalonia Window not showing?"
> "How do I render video transparently in Avalonia?"
> "Which Avalonia dialog library should I use?"

Start with a web search for the v12-specific answer, not with offline assumptions.
