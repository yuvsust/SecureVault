# SecureVault API

A secure file storage API built with .NET 8.

## Architecture

- **Framework:** .NET 8 Web API
- **Pattern:** Controller -> Service -> Model
- **Storage:** Local file system

## Getting Started

### Prerequisites
- .NET 8 SDK or higher

### Build and Run

```bash
cd SecureVault.Api
dotnet run
```

The API will be available at `https://localhost:7200`.

### Running Tests

```bash
cd SecureVault.Tests
dotnet test
```

## Project Structure

- `SecureVault.Api/` - Main Web API project
- `SecureVault.Tests/` - Unit tests using xUnit and Moq
- `SecureVault.Api/Controllers/` - API endpoints
- `SecureVault.Api/Services/` - Business logic layer
- `SecureVault.Api/Models/` - Data models

## License

MIT
