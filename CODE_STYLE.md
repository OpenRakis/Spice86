The code style is enforced by a .editorconfig file, inside src/

Notably, code blocks should be opened Java style:

```csharp
if(blabla) {
}
```

NOT C# style:

```csharp
if (blabla) 
{
}
```

Also, namespaces must be file-scoped:

```csharp
namespace Spice86.Core.Emulator.blabla;
```

## Test Style

All test methods must use explicit Arrange/Act/Assert sections.

- Include `// Arrange`, `// Act`, and `// Assert` comments in this order.
- Keep setup, execution, and verification clearly separated.
- Apply this rule to new tests and when modifying existing tests.
