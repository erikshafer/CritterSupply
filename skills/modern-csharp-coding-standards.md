---
name: modern-csharp-coding-standards
description: Write modern C# code using records, pattern matching, value objects, async/await, and best-practice API design patterns. Emphasizes functional-style programming with C# 14+ features.
---

# Modern C# Coding Standards

## When to Use This Skill

Use this skill when:
- Writing new C# code or refactoring existing code
- Designing public APIs for libraries or services
- Optimizing performance-critical code paths
- Implementing domain models with strong typing
- Building async/await-heavy applications

## Core Principles

1. **Immutability by Default** - Use `record` types and `init`-only properties
2. **Type Safety** - Leverage nullable reference types and value objects
3. **Modern Pattern Matching** - Use `switch` expressions and patterns extensively
4. **Async Everywhere** - Prefer async APIs with proper cancellation support
5. **API Design** - Accept abstractions, return appropriately specific types
6. **Composition Over Inheritance** - Avoid abstract base classes, prefer composition

---

## Best Practices Summary

### DO's ✅
- Use `record` for DTOs, messages, and domain entities
- Use `readonly record struct` for value objects
- Leverage pattern matching with `switch` expressions
- Enable and respect nullable reference types
- Use async/await for all I/O operations
- Accept `CancellationToken` in all async methods
- Use `Span<T>` and `Memory<T>` for high-performance scenarios
- Accept abstractions (`IEnumerable<T>`, `IReadOnlyList<T>`)
- Return appropriate interfaces or concrete types
- Use `Result<T, TError>` for expected errors
- Use `ConfigureAwait(false)` in library code
- Pool buffers with `ArrayPool<T>` for large allocations
- Prefer composition over inheritance
- Avoid abstract base classes in application code

### DON'Ts ❌
- Don't use mutable classes when records work
- Don't use classes for value objects (use `readonly record struct`)
- Don't create deep inheritance hierarchies
- Don't ignore nullable reference type warnings
- Don't block on async code (`.Result`, `.Wait()`)
- Don't use `byte[]` when `Span<byte>` suffices
- Don't forget `CancellationToken` parameters
- Don't return mutable collections from APIs
- Don't throw exceptions for expected business errors
- Don't use `string` concatenation in loops
- Don't allocate large arrays repeatedly (use `ArrayPool`)

---

## Additional Resources

- **C# Language Specification**: https://learn.microsoft.com/en-us/dotnet/csharp/
- **Pattern Matching**: https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/functional/pattern-matching
- **Async Best Practices**: https://learn.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming
- **.NET Performance Tips**: https://learn.microsoft.com/en-us/dotnet/framework/performance/
