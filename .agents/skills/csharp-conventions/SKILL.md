---
name: csharp-conventions
description: >-
  Kern project C# conventions, namespace rules, DI/VContainer patterns, and assembly layer boundaries.
  Use when writing, editing, or reviewing any C# file — MonoBehaviours, ScriptableObjects, services,
  contracts, factories, or editor scripts. Triggers on: namespace, nullable, asmdef, VContainer,
  RegisterComponent, IObjectResolver, composition root, Kern.Contracts, primary constructor,
  record struct, SA1513, _camelCase, PascalCase, asmref, MonoScript.GetClass.
---

# C# and structure

## Language and nullable

- `#nullable enable` is enabled; annotate reference types explicitly as nullable or non-null.
- Use C# 12 features (`primary constructors`, `readonly record struct`, collection expressions) where appropriate.

## Namespaces

- Regular types use **file-scoped namespaces**.
- Types deriving from `MonoBehaviour`, `ScriptableObject`, `ScriptableRendererFeature`, or `VolumeComponent` use **block namespaces** — otherwise `MonoScript.GetClass()` may return `null`.

## Style

- Allman braces, mandatory `{}`, SA1513/SA1508.
- Trailing comma in multi-line initializers.
- Private fields — `_camelCase`; public members and types — `PascalCase`.
- Unity script file name must match the class name.

## DI / VContainer

- Do not create managers via `AddComponent` in `Configure`.
- Register scene components via `RegisterComponent`; create prefabs/entities via `ISceneObjectFactory`.
- `IObjectResolver` is permitted only in composition roots and factories.
- `RegisterInstance` does not inject manually created objects.
- Do not resolve the container in `Awake`, `OnEnable`, or `Start`.

## Assembly layers

- `Kern.Contracts` is the bottom layer: it does not reference any `Kern.*` assembly.
- Module contracts live next to the module in a `Contracts/` folder with `Kern.Contracts.asmref`; without it the type lands in the module assembly and breaks everyone below.
- Do not place types that depend on implementation details in `Contracts/`.
- Guarded by `ContractsAssemblyBoundaryTests`.

## Documents

- Files in `docs/` must be self-contained HTML with inline `<style>`, no Markdown, no external dependencies.
