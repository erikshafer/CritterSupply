---
name: critterstack-testing-patterns
description: Write unit and integration tests for applications using the Critter Stack, which tools include Wolverine and Marten. Covers message handling, message validation, event persistence, endpoint interaction validation. Includes guidance on using the Alba integration testing library. 
---

# Critter Stack Testing Patterns

## When to Use This Skill

Use this skill when:
- Writing unit tests for Wolverine message handlers
- Testing Marten event sourcing and document storage
- Validating message routing and endpoint interactions
- Mocking external dependencies in handler tests

### ✅ Use Alba for integration tests

Easy integration testing for ASP.NET Core applications, especially those using Wolverine and Marten.
- Declarative Syntax
- Classic & Minimal API Support
- Authorization Stubbing

### ✅ Use Shouldly for validation

Shouldly is an assertion framework which focuses on giving great error messages when the assertion fails while being simple and terse. Shouldly uses the code before the ShouldBe statement to report on errors, which makes diagnosing easier.

## Required NuGet Packages

```xml
<ItemGroup>
  <!-- xUnit (or your preferred test framework) -->
  <PackageReference Include="xunit" Version="*" />
  <PackageReference Include="xunit.runner.visualstudio" Version="*" />
  <PackageReference Include="Microsoft.NET.Test.Sdk" Version="*" />

  <!-- Integration testing framework -->
  <PackageReference Include="Alba" Version="*" />
    
  <!-- Assertions (recommended) -->
  <PackageReference Include="Shouldly" Version="*" />
</ItemGroup>
```
---

## Additional Resources

- **Alba (documentation)**: https://jasperfx.github.io/alba/
- **Alba (github)**: https://github.com/JasperFx/alba
- **Shouldly (documentation)**: https://docs.shouldly.org/
- **Shouldly (github)**: https://github.com/shouldly/shouldly
