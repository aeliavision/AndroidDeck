# VCF Contact Editor - Documentation

## Overview

The VCF Contact Editor is a professional-grade Windows application for managing VCF (vCard) contact files. Built with modern C# and WPF, it provides comprehensive contact management capabilities with a focus on reliability, performance, and user experience.

## Architecture

### Core Architecture
- **Technology Stack**: .NET 8.0, WPF, C# 10.0
- **Architecture Pattern**: MVVM (Model-View-ViewModel)
- **Dependency Injection**: Microsoft.Extensions.DI
- **Logging**: Microsoft.Extensions.Logging with file providers
- **Validation**: FluentValidation for comprehensive input validation

### Project Structure
```
📁 VcfEditor/
├── 📁 Models/              # Data models with INotifyPropertyChanged
├── 📁 Services/            # Business logic and service layer
│   ├── Interfaces/        # Service interfaces for DI and testing
│   ├── Validation/        # FluentValidation validators
│   ├── Commands/          # Command pattern implementations
│   ├── ExceptionHandling/ # Custom exception handling
│   ├── Configuration/     # Application configuration management
│   ├── Monitoring/        # Performance monitoring and metrics
│   ├── Api/               # API client for web services
│   └── Localization/      # Internationalization support
├── 📁 ViewModels/         # MVVM ViewModels with CommunityToolkit.Mvvm
├── 📁 Views/              # WPF XAML views and code-behind
├── 📁 Tests/              # Unit and integration tests
└── 📁 Documentation/      # This documentation
```

## Key Features

### Contact Management
- ✅ Full CRUD operations for contacts
- ✅ Advanced search and filtering
- ✅ Bulk operations support
- ✅ Undo/Redo functionality
- ✅ Data validation with real-time feedback

### File Operations
- ✅ VCF v2.1 format support
- ✅ Async file operations for large files
- ✅ Drag and drop file handling
- ✅ Recent files management
- ✅ Backup and recovery system

### User Experience
- ✅ Professional modern UI
- ✅ Keyboard shortcuts
- ✅ Accessibility support
- ✅ Internationalization ready
- ✅ Theme support

## API Reference

### Core Services

#### IVcfParser
```csharp
public interface IVcfParser
{
    Task<List<Contact>> ParseVcfFileAsync(string filePath);
    List<Contact> ParseVcfFile(string filePath);
    Task<string> ExportToVcfAsync(List<Contact> contacts);
    string ExportToVcf(List<Contact> contacts);
}
```

#### ILoggingService
```csharp
public interface ILoggingService
{
    void LogInformation(string message);
    void LogWarning(string message);
    void LogError(Exception exception, string message);
    void LogDebug(string message);
    string GetLogFilePath();
}
```

### Data Models

#### Contact
```csharp
public class Contact : INotifyPropertyChanged
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string FullName { get; set; }
    public string Organization { get; set; }
    public string Title { get; set; }
    public string Email { get; set; }
    public ObservableCollection<PhoneNumber> PhoneNumbers { get; set; }
    public string PrimaryPhoneNumber { get; }
}
```

#### PhoneNumber
```csharp
public class PhoneNumber : INotifyPropertyChanged
{
    public string Number { get; set; }
    public PhoneNumberType Type { get; set; }
    public string TypeDisplayName { get; }
}
```

## Configuration

### AppSettings
```json
{
  "AppSettings": {
    "AppName": "VCF Contact Editor",
    "Version": "1.0.0",
    "MaxRecentFiles": 10,
    "AutoSaveIntervalMinutes": 5,
    "EnableLogging": true,
    "MinimumLogLevel": "Information",
    "EnableAutoSave": false,
    "EnableBackup": true,
    "Theme": "Light",
    "Language": "en-US",
    "MaxFileSizeMB": 50,
    "ShowTooltips": true,
    "ConfirmOnDelete": true
  }
}
```

## Usage Examples

### Basic Contact Operations
```csharp
// Create and validate a contact
var contact = new Contact
{
    FirstName = "John",
    LastName = "Doe",
    Email = "john.doe@example.com",
    PhoneNumbers = { new PhoneNumber("+1234567890", PhoneNumberType.CELL) }
};

var validator = new ContactValidator();
var result = validator.Validate(contact);
if (result.IsValid)
{
    // Contact is valid, proceed with saving
}
```

### File Operations with Error Handling
```csharp
var parser = new VcfParser();
var exceptionHandler = new ExceptionHandler(logger);

try
{
    var contacts = await exceptionHandler.ExecuteWithExceptionHandling(
        () => parser.ParseVcfFileAsync(filePath),
        "Parse VCF File");
}
catch (UserFriendlyException ex)
{
    // Show user-friendly error message
    MessageBox.Show(ex.Message);
}
```

### Dependency Injection Setup
```csharp
// In application startup
ServiceContainer.Initialize();

// Get services
var logger = ServiceContainer.GetRequiredService<ILoggingService>();
var parser = ServiceContainer.GetRequiredService<IVcfParser>();
```

## Testing

### Running Tests
```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test VcfEditor.Tests.csproj

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Test Categories
- **Unit Tests**: Individual service and model testing
- **Integration Tests**: Full workflow testing
- **Performance Tests**: Load and stress testing

## Deployment

### Prerequisites
- .NET 8.0 Runtime
- Windows 10/11
- 100 MB free disk space

### Installation
1. Extract the application files
2. Run `VcfEditor.exe`
3. Optional: Create desktop shortcut

### Configuration
- Settings are stored in `settings.json` in the application directory
- Logs are stored in `%LOCALAPPDATA%/VcfEditor/Logs/`
- Recent files list is maintained automatically

## Troubleshooting

### Common Issues

#### Application Won't Start
- Check .NET 8.0 Runtime installation
- Verify Windows version compatibility
- Check application logs in `%LOCALAPPDATA%/VcfEditor/Logs/`

#### File Import Errors
- Verify VCF file format (v2.1 supported)
- Check file encoding (UTF-8 recommended)
- Ensure file is not corrupted or password-protected

#### Performance Issues
- Check available system memory
- Verify disk space
- Review application logs for errors

### Getting Help
- Check the application logs
- Review this documentation
- Contact support with log files

## Contributing

### Development Setup
1. Clone the repository
2. Open `VcfEditor.sln` in Visual Studio 2022+
3. Restore NuGet packages
4. Build and run

### Code Style Guidelines
- Use C# 10.0 features
- Follow MVVM pattern
- Write comprehensive unit tests
- Use dependency injection
- Document public APIs with XML comments

## License

This project is provided as-is for educational and development purposes.

## Version History

### v1.0.0
- Initial release with core VCF editing functionality
- Modern WPF UI with professional styling
- Comprehensive error handling and logging
- Async file operations for performance
- MVVM architecture with dependency injection
- Unit and integration testing framework