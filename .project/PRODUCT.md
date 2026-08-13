# Product

## Register

product

## Users

PandA operators, controls engineers, QA engineers, and porting reviewers use the app while validating print-and-apply line behavior, diagnosing carton state, and confirming that UI decisions match the ported Core engine.

## Product Purpose

PandA is a Blazor operator and validation suite for the ported print-and-apply engine. It surfaces line status, configuration, lookup, reject, MandA, and simulation workflows so users can trust the C# port before attaching real hardware.

## Brand Personality

Industrial, precise, calm. The interface should feel like a dependable operations console rather than a marketing surface.

## Anti-references

Avoid decorative dashboards, game-like simulator controls, gratuitous animation, and visuals that obscure whether Core or JavaScript made a decision.

## Design Principles

- Make engine decisions observable before making them pretty.
- Keep operator actions conventional and low-friction.
- Preserve configuration truth: screens should reflect the active project data.
- Use motion only to explain physical line state.

## Accessibility & Inclusion

Target accessible product UI patterns: readable contrast, keyboard-usable controls, clear labels, and reduced-motion-safe interaction. Color state must be backed by text because reject/pass meaning cannot rely on color alone.
