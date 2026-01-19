# Secure Vault API - Project Context

## Architecture

- **Framework:** .NET 8 Web API.
- **Pattern:** Controller -> Service -> Model (Repository pattern simulation).
- **Storage:** Local file system (VaultStorage folder). NO Database yet.

## Coding Standards

- **Best Quality** Always maintain high code quality, readability, and performance.
- **Naming Conventions:** Follow C# conventions (PascalCase for classes/methods, camelCase for variables).
- **Async/Await:** All I/O operations must be asynchronous. Use `Task.WhenAll` for parallelism where possible.
- **Security:** - Validate all file paths to prevent Path Traversal.
  - Use `Guid` for filenames to prevent collisions.
- **Error Handling:** Global Exception Handling middleware is preferred over try-catch in every controller.
- **Testing:** xUnit with Moq. Focus on edge cases (nulls, empty streams, concurrency).

## Constraints

- Do not suggest Entity Framework (SQL) code; we are using a static `List<T>` for now.
- Do not suggest 3rd party libraries unless absolutely necessary; stick to `System.IO` and standard libraries.
