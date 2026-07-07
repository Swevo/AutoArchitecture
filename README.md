# AutoArchitecture

[![NuGet](https://img.shields.io/nuget/v/AutoArchitecture.svg)](https://www.nuget.org/packages/AutoArchitecture/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/AutoArchitecture.svg)](https://www.nuget.org/packages/AutoArchitecture/)
[![CI](https://github.com/Swevo/AutoArchitecture/actions/workflows/build.yml/badge.svg)](https://github.com/Swevo/AutoArchitecture/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**Free, MIT-licensed compile-time architecture rule enforcement for .NET.** No commercial
license, no separate desktop tool, no CI-only step — violations show up as build
warnings/errors the moment they're introduced, right in your IDE.

## Why AutoArchitecture?

Tools like NDepend are excellent for deep codebase analysis but require a paid commercial
license. If all you need is "the UI layer must never reference the data-access layer
directly," you shouldn't need a separate license and a separate analysis pass for that.
AutoArchitecture is a Roslyn source generator: declare your rules once as assembly
attributes, and every build enforces them for free.

```csharp
[assembly: AutoArchitecture.ForbidDependency("MyApp.UI", "MyApp.DataAccess",
    Because = "UI must go through the service layer, not the repositories directly")]
```

The moment any type in `MyApp.UI` (or a sub-namespace like `MyApp.UI.Views`) references a
type in `MyApp.DataAccess` (or a sub-namespace), you get:

```
warning AA001: 'OrderController' in namespace 'MyApp.UI' references 'OrderRepository' in
namespace 'MyApp.DataAccess', which is forbidden by
[assembly: ForbidDependency("MyApp.UI", "MyApp.DataAccess")]
(UI must go through the service layer, not the repositories directly)
```

## Install

```bash
dotnet add package AutoArchitecture
```

## Usage

Add one `[assembly: ForbidDependency(from, to)]` attribute per rule anywhere in your
project (e.g. in `AssemblyInfo.cs` or alongside your `Program.cs`):

```csharp
[assembly: AutoArchitecture.ForbidDependency("MyApp.Presentation", "MyApp.Infrastructure")]
[assembly: AutoArchitecture.ForbidDependency("MyApp.Domain", "MyApp.Infrastructure")]
```

- Namespace matching includes sub-namespaces: a rule for `MyApp.DataAccess` also covers
  `MyApp.DataAccess.Sql`, `MyApp.DataAccess.Migrations`, etc.
- Rules only match on exact/dot-boundary prefixes, so `MyApp.DataAccess` does **not**
  accidentally match an unrelated `MyApp.DataAccessLegacy` namespace.
- The `Because` named argument is optional and is included verbatim in the diagnostic
  message — handy for explaining *why* the rule exists to whoever hits it next.
- AA001 is a `Warning` by default. Promote it to a build-breaking error per-project with
  a standard `.editorconfig` severity override:
  ```ini
  dotnet_diagnostic.AA001.severity = error
  ```

## Design goals

- **MIT licensed, forever.** No commercial tier, no per-seat fees, no desktop app to buy.
- **Zero runtime cost** — this is a source generator/analyzer; nothing ships in your
  compiled output.
- **IDE-first feedback** — violations appear as squiggles while you type, not just in a
  nightly CI report.
- **Simple, declarative rules** — no XML rule files or query languages to learn.

## Roadmap

- Allow-list exceptions for specific types within an otherwise-forbidden namespace pair
  are planned for a future release, to support gradual migrations.

## 💼 Need .NET consulting?

I'm the author of AutoArchitecture and a suite of compile-time source generators
([AutoWire](https://github.com/Swevo/AutoWire), [AutoMap.Generator](https://github.com/Swevo/AutoMap.Generator))
and 28+ Polly v8 resilience packages. I'm available for consulting on **Polly v8 resilience**,
**Azure cloud architecture**, and **clean .NET design**.

**[→ solidqualitysolutions.com](https://www.solidqualitysolutions.com/)** · **[LinkedIn](https://www.linkedin.com/in/justbannister/)**

## Also by the same author

> 🌐 Full suite overview: **[swevo.github.io](https://swevo.github.io/)**

| Package | Description |
|---|---|
| [**FluentPdf**](https://github.com/Swevo/FluentPdf) | Free, MIT-licensed fluent PDF generation — alternative to QuestPDF's commercial license. |
| [**AutoBus**](https://github.com/Swevo/AutoBus) | Free, MIT-licensed message bus — alternative to MassTransit's commercial license. |
| [**AutoAssert**](https://github.com/Swevo/AutoAssert) | Free, MIT-licensed fluent assertions — alternative to FluentAssertions' commercial license. |
| [**EFCore.BulkOperations**](https://github.com/Swevo/EFCore.BulkOperations) | Free, MIT-licensed bulk insert/update/delete for EF Core. |
| [**AutoWire**](https://github.com/Swevo/AutoWire) | Compile-time DI auto-registration — `[Scoped]`/`[Singleton]`/`[Transient]` generates `IServiceCollection` registration code. |
| [**AutoMap.Generator**](https://github.com/Swevo/AutoMap.Generator) | Compile-time object mapping — `[Map(typeof(Dto))]` generates `ToDto()` extension methods. |
| [**AutoDispatch.Generator**](https://github.com/Swevo/AutoDispatch.Generator) | Compile-time CQRS dispatcher — free alternative to MediatR's commercial license. |
| [**PollyAnalyzers**](https://github.com/Swevo/PollyAnalyzers) | Free Roslyn analyzers for async/resilience anti-patterns — blocking calls, async void, fire-and-forget tasks, swallowed exceptions. |

## License

MIT © Justin Bannister
