# TID3 Structured Logging System

## Overview

The TID3 application now includes a comprehensive structured logging system that stores logs in JSON format in your AppData directory for easy analysis and debugging.

## Log Storage Location

Logs are stored in: `%LOCALAPPDATA%\TID3\Logs\`

Each application session creates a new log file named: `TID3_YYYY-MM-DD_HH-mm-ss.log`

The system automatically keeps the last 10 log files and deletes older ones.

## Log Levels

- **Trace** (0): Very detailed information, typically only of interest when diagnosing problems
- **Debug** (1): Information useful for debugging (default in development)
- **Info** (2): General information about application flow (default in production)
- **Warning** (3): Something unexpected happened, but the application can continue
- **Error** (4): A serious problem occurred, functionality may be affected
- **Fatal** (5): A critical error that may cause the application to terminate

## Log Format

Logs are stored in structured JSON format for easy analysis:

```json
{
  "timestamp": "2024-01-15T14:30:25.123",
  "level": "Info",
  "category": "Images",
  "message": "Bitmap created successfully",
  "component": "ImageHelper",
  "threadId": 1,
  "properties": {
    "originalWidth": 500,
    "originalHeight": 500,
    "decodedWidth": 200,
    "decodedHeight": 200
  }
}
```

## Key Categories

### Application
- Application startup, shutdown, window initialization
- Settings loading and changes
- General application lifecycle events

### Files
- File loading operations
- File processing results
- File system operations

### Images  
- Cover art loading and processing
- Image dimension handling
- Image format conversions
- Bitmap creation and management

### HTTP
- All HTTP requests and responses
- API calls to MusicBrainz, CoverArt services
- Network timeout and retry attempts
- Response status codes and timing

### Tags
- Audio file metadata reading and writing
- TagLib operations
- Metadata comparison operations

### AcoustId
- Audio fingerprinting operations
- AcoustID API interactions
- Fingerprint extraction and matching

## Specialized Logging Helpers

### Performance Logging
Use `BeginScope` for automatic timing of operations:

```csharp
using var scope = TID3Logger.BeginScope("Images", "LoadCoverArt", new { FileName = "cover.jpg" }, "ImageService");
// Your operation here
// Automatically logs start time, completion time, and duration
```

### Image-Specific Logging
```csharp
TID3Logger.Images.LogImageDetails(bitmap, "After processing", "MyComponent");
TID3Logger.Images.LogFileInfo(filePath, "MyComponent");
```

### HTTP-Specific Logging
```csharp
TID3Logger.Http.LogRequest(url, clientName, "MyComponent");
TID3Logger.Http.LogResponse(url, statusCode, contentLength, duration, "MyComponent");
TID3Logger.Http.LogError(url, exception, "MyComponent");
```

### File Processing Logging
```csharp
TID3Logger.Files.LogProcessingStart(filePath, "LoadAudio", "AudioProcessor");
TID3Logger.Files.LogProcessingComplete(filePath, "LoadAudio", duration, results, "AudioProcessor");
TID3Logger.Files.LogProcessingError(filePath, "LoadAudio", exception, "AudioProcessor");
```

## Configuration

### Changing Log Level
```csharp
TID3Logger.SetMinimumLevel(LogLevel.Debug);
```

### Getting Log Information
```csharp
var logDirectory = TID3Logger.GetLogDirectory();
var currentLogFile = TID3Logger.GetCurrentLogFile();
```

## Benefits

1. **Structured Data**: JSON format allows for easy parsing and analysis
2. **Rich Context**: Each log entry includes component, thread ID, and custom properties
3. **Performance Tracking**: Automatic timing with scoped operations
4. **Centralized**: All logging goes through a single, consistent system
5. **Thread-Safe**: Safe for use in multi-threaded scenarios
6. **AppData Storage**: Logs stored in user's AppData for easy access
7. **Automatic Cleanup**: Old log files are automatically removed

## Replacement of Previous Systems

This new structured logging system has completely replaced:
- CoverArtLogger (old desktop-based logging)
- Various Debug.WriteLine calls throughout the application
- Scattered logging approaches

All logging now flows through the centralized TID3Logger system with consistent formatting and storage.

## Usage Examples

The logging system is already integrated throughout the application:

- **MainWindow**: Application lifecycle, file loading operations
- **TagService**: File processing, tag reading/writing, cover art loading
- **ImageHelper**: All image processing operations with detailed metrics
- **HttpClientManager**: Network requests with timing and error handling
- **AcoustIdService**: Fingerprinting operations and API calls
- **AudioFileInfo Models**: Property changes and cover art updates

## Log Analysis

Since logs are in JSON format, you can easily:
- Parse with any JSON tool
- Filter by component, category, or log level
- Analyze performance patterns
- Track error frequencies
- Monitor application usage patterns