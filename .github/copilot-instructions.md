# Copilot Instructions

## General Guidelines
- Prefer SOLID-oriented code: use clear, intention-revealing names; single-responsibility classes; open/closed design; Liskov Substitution Principle; small, focused interfaces; dependency inversion; avoid multi-hop indirection.

## Project Guidelines
- Use the complete decompiled game source in /BLSource for code eligibility checks; make no assumptions about code beyond that source.
- Avoid using reflection on controlled code; use reflection only on vanilla private members when no better alternative exists.
- Prefer clear intention-revealing names, single-responsibility classes, open/closed design, LSP, small focused interfaces, dependency inversion, and no multi-hop indirection.

## Suggestion Format
- When suggesting code changes, provide only the relevant class- or method-level chunks with minimal surrounding context; avoid generating whole classes.
- Include a concise before/after comparison for each suggested change.
- Provide a precise explanation of the change in 3–5 sentences minimum.
- Show only necessary diffs or snippets; keep examples focused and limited to what is required to understand and apply the change.