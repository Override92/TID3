using System;
using System.IO;
using System.Threading;
using System.Text.Json;
using System.Collections.Generic;

namespace TID3.Utils
{
    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Warning = 3,
        Error = 4,
        Fatal = 5
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public Dictionary<string, object>? Properties { get; set; }
        public string? Exception { get; set; }
        public string? StackTrace { get; set; }
        public int ThreadId { get; set; }
        public string Component { get; set; } = string.Empty;
    }

    public static class TID3Logger
    {
        private static readonly string LogDirectory = null!;
        private static readonly string CurrentLogFile = null!;
        private static readonly object LogLock = new object();
        private static LogLevel _minimumLevel = LogLevel.Info;
        private static readonly JsonSerializerOptions JsonOptions = null!;

        static TID3Logger()
        {
            // Create logs directory in AppData\Local\TID3\Logs
            var appDataLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            LogDirectory = Path.Combine(appDataLocal, "TID3", "Logs");
            
            try
            {
                Directory.CreateDirectory(LogDirectory);
                
                // Create current log file with timestamp
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                CurrentLogFile = Path.Combine(LogDirectory, $"TID3_{timestamp}.log");
                
                // Clean up old log files (keep last 10)
                CleanupOldLogFiles();
                
                JsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = false,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                
                // Write session header
                WriteLogEntry(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = LogLevel.Info,
                    Category = "System",
                    Component = "Logger",
                    Message = "TID3 Application Started",
                    ThreadId = Thread.CurrentThread.ManagedThreadId,
                    Properties = new Dictionary<string, object>
                    {
                        ["Version"] = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown",
                        ["LogFile"] = CurrentLogFile,
                        ["Framework"] = Environment.Version.ToString(),
                        ["OS"] = Environment.OSVersion.ToString()
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize TID3Logger: {ex.Message}");
            }
        }

        public static void SetMinimumLevel(LogLevel level)
        {
            _minimumLevel = level;
            Info("Logger", "Minimum log level changed", new { NewLevel = level.ToString() });
        }

        public static void Trace(string category, string message, object? properties = null, string component = "")
            => Log(LogLevel.Trace, category, message, properties, component);

        public static void Debug(string category, string message, object? properties = null, string component = "")
            => Log(LogLevel.Debug, category, message, properties, component);

        public static void Info(string category, string message, object? properties = null, string component = "")
            => Log(LogLevel.Info, category, message, properties, component);

        public static void Warning(string category, string message, object? properties = null, string component = "")
            => Log(LogLevel.Warning, category, message, properties, component);

        public static void Error(string category, string message, Exception? exception = null, object? properties = null, string component = "")
            => Log(LogLevel.Error, category, message, properties, component, exception);

        public static void Fatal(string category, string message, Exception? exception = null, object? properties = null, string component = "")
            => Log(LogLevel.Fatal, category, message, properties, component, exception);

        private static void Log(LogLevel level, string category, string message, object? properties, string component, Exception? exception = null)
        {
            if (level < _minimumLevel)
                return;

            var logEntry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Category = category,
                Message = message,
                Component = component,
                ThreadId = Thread.CurrentThread.ManagedThreadId,
                Exception = exception?.Message,
                StackTrace = exception?.StackTrace
            };

            if (properties != null)
            {
                logEntry.Properties = ConvertToStringObjectDictionary(properties);
            }

            WriteLogEntry(logEntry);

            // Also write to debug output for immediate visibility during development
            if (level >= LogLevel.Warning)
            {
                System.Diagnostics.Debug.WriteLine($"[{level}] [{category}] {message}");
                if (exception != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Exception: {exception}");
                }
            }
        }

        private static Dictionary<string, object> ConvertToStringObjectDictionary(object properties)
        {
            var result = new Dictionary<string, object>();
            
            if (properties is Dictionary<string, object> dict)
            {
                return dict;
            }

            // Use reflection to convert anonymous objects to dictionary
            var type = properties.GetType();
            foreach (var prop in type.GetProperties())
            {
                var value = prop.GetValue(properties);
                if (value != null)
                {
                    result[prop.Name] = value;
                }
            }

            return result;
        }

        private static void WriteLogEntry(LogEntry entry)
        {
            try
            {
                lock (LogLock)
                {
                    var jsonLine = JsonSerializer.Serialize(entry, JsonOptions);
                    File.AppendAllText(CurrentLogFile, jsonLine + Environment.NewLine);
                }
            }
            catch (Exception)
            {
                // Silently fail to avoid breaking the application
            }
        }

        private static void CleanupOldLogFiles()
        {
            try
            {
                var logFiles = Directory.GetFiles(LogDirectory, "TID3_*.log");
                if (logFiles.Length <= 10) return;

                // Sort by creation time and delete oldest files
                Array.Sort(logFiles, (x, y) => File.GetCreationTime(x).CompareTo(File.GetCreationTime(y)));
                
                for (int i = 0; i < logFiles.Length - 10; i++)
                {
                    File.Delete(logFiles[i]);
                }
            }
            catch (Exception)
            {
                // Ignore cleanup errors
            }
        }

        public static string GetLogDirectory() => LogDirectory;
        public static string GetCurrentLogFile() => CurrentLogFile;

        // Performance logging helpers
        public static IDisposable BeginScope(string category, string operation, object? properties = null, string component = "")
        {
            return new LogScope(category, operation, properties, component);
        }

        private class LogScope : IDisposable
        {
            private readonly string _category;
            private readonly string _operation;
            private readonly string _component;
            private readonly DateTime _startTime;
            private bool _disposed = false;

            public LogScope(string category, string operation, object? properties, string component)
            {
                _category = category;
                _operation = operation;
                _component = component;
                _startTime = DateTime.Now;

                var startProps = new Dictionary<string, object> { ["Operation"] = operation };
                if (properties != null)
                {
                    foreach (var kvp in ConvertToStringObjectDictionary(properties))
                    {
                        startProps[kvp.Key] = kvp.Value;
                    }
                }

                Debug(category, $"Started: {operation}", startProps, component);
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    var duration = DateTime.Now - _startTime;
                    Debug(_category, $"Completed: {_operation}", new { 
                        Operation = _operation,
                        DurationMs = duration.TotalMilliseconds,
                        Duration = duration.ToString()
                    }, _component);
                    _disposed = true;
                }
            }
        }

        // Image-specific logging helpers (to replace CoverArtLogger)
        public static class Images
        {
            public static void LogImageDetails(System.Windows.Media.Imaging.BitmapImage? image, string context, string component = "ImageHelper")
            {
                if (image == null)
                {
                    Debug("Images", $"{context}: Image is NULL", component: component);
                    return;
                }

                try
                {
                    var originalWidth = image.GetValue(TID3.Services.TagService.OriginalWidthProperty) as int?;
                    var originalHeight = image.GetValue(TID3.Services.TagService.OriginalHeightProperty) as int?;

                    Debug("Images", $"{context}: Image loaded successfully", new
                    {
                        PixelWidth = image.PixelWidth,
                        PixelHeight = image.PixelHeight,
                        OriginalWidth = originalWidth ?? 0,
                        OriginalHeight = originalHeight ?? 0,
                        DecodePixelWidth = image.DecodePixelWidth,
                        CanFreeze = image.CanFreeze,
                        IsFrozen = image.IsFrozen,
                        IsDownloading = image.IsDownloading
                    }, component);
                }
                catch (Exception ex)
                {
                    Error("Images", $"{context}: Failed to get image details", ex, component: component);
                }
            }

            public static void LogFileInfo(string filePath, string component = "ImageHelper")
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    Debug("Images", "File info retrieved", new
                    {
                        FileName = Path.GetFileName(filePath),
                        FullPath = filePath,
                        Exists = fileInfo.Exists,
                        SizeBytes = fileInfo.Length,
                        Extension = fileInfo.Extension,
                        Directory = fileInfo.DirectoryName
                    }, component);
                }
                catch (Exception ex)
                {
                    Error("Images", $"Failed to get file info for {filePath}", ex, component: component);
                }
            }
        }

        // HTTP-specific logging helpers
        public static class Http
        {
            public static void LogRequest(string url, string client, string component = "HttpClient")
            {
                Debug("HTTP", "Request started", new { Url = url, Client = client }, component);
            }

            public static void LogResponse(string url, int statusCode, long? contentLength, TimeSpan duration, string component = "HttpClient")
            {
                var level = statusCode >= 400 ? LogLevel.Warning : LogLevel.Debug;
                Log(level, "HTTP", "Request completed", new
                {
                    Url = url,
                    StatusCode = statusCode,
                    ContentLengthBytes = contentLength,
                    DurationMs = duration.TotalMilliseconds
                }, component);
            }

            public static void LogError(string url, Exception exception, string component = "HttpClient")
            {
                Error("HTTP", $"Request failed: {url}", exception, component: component);
            }
        }

        // File processing helpers
        public static class Files
        {
            public static void LogProcessingStart(string filePath, string operation, string component = "FileProcessor")
            {
                Info("Files", $"Started {operation}", new { FilePath = filePath, Operation = operation }, component);
            }

            public static void LogProcessingComplete(string filePath, string operation, TimeSpan duration, object? results = null, string component = "FileProcessor")
            {
                var props = new Dictionary<string, object>
                {
                    ["FilePath"] = filePath,
                    ["Operation"] = operation,
                    ["DurationMs"] = duration.TotalMilliseconds
                };

                if (results != null)
                {
                    foreach (var kvp in ConvertToStringObjectDictionary(results))
                    {
                        props[$"Result_{kvp.Key}"] = kvp.Value;
                    }
                }

                Info("Files", $"Completed {operation}", props, component);
            }

            public static void LogProcessingError(string filePath, string operation, Exception exception, string component = "FileProcessor")
            {
                Error("Files", $"Failed {operation}", exception, new { FilePath = filePath, Operation = operation }, component);
            }
        }
    }
}