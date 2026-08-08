namespace AtomPix.Infrastructure.Diagnostics;

using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

public sealed record LocalJsonLoggerOptions
{
    public LocalJsonLoggerOptions(
        string logDirectory,
        long maxFileSizeBytes = 10L * 1024 * 1024,
        long maxTotalSizeBytes = 50L * 1024 * 1024,
        int retentionDays = 7)
    {
        if (string.IsNullOrWhiteSpace(logDirectory)) throw new ArgumentException("Log directory cannot be empty.", nameof(logDirectory));
        if (maxFileSizeBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxFileSizeBytes));
        if (maxTotalSizeBytes < maxFileSizeBytes) throw new ArgumentOutOfRangeException(nameof(maxTotalSizeBytes));
        if (retentionDays <= 0) throw new ArgumentOutOfRangeException(nameof(retentionDays));

        LogDirectory = Path.GetFullPath(logDirectory);
        MaxFileSizeBytes = maxFileSizeBytes;
        MaxTotalSizeBytes = maxTotalSizeBytes;
        RetentionDays = retentionDays;
    }

    public string LogDirectory { get; }
    public long MaxFileSizeBytes { get; }
    public long MaxTotalSizeBytes { get; }
    public int RetentionDays { get; }
}

public sealed class LocalJsonLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly object _syncRoot = new();
    private readonly LocalJsonLoggerOptions _options;
    private readonly byte[] _pathTokenKey = RandomNumberGenerator.GetBytes(32);
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();
    private bool _disposed;

    public LocalJsonLoggerProvider(LocalJsonLoggerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        SessionId = Guid.NewGuid().ToString("N");
        TryPrepareStorage();
    }

    public string SessionId { get; }

    public ILogger CreateLogger(string categoryName) =>
        new LocalJsonLogger(this, categoryName ?? string.Empty);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) =>
        _scopeProvider = scopeProvider ?? throw new ArgumentNullException(nameof(scopeProvider));

    public void Dispose()
    {
        lock (_syncRoot)
        {
            _disposed = true;
        }
    }

    internal void Write<TState>(
        string categoryName,
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.None) return;

        try
        {
            var fields = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["TimestampUtc"] = DateTimeOffset.UtcNow,
                ["Level"] = logLevel.ToString(),
                ["EventId"] = eventId.Id,
                ["EventName"] = string.IsNullOrWhiteSpace(eventId.Name) ? "Log" : eventId.Name,
                ["SessionId"] = SessionId,
                ["Category"] = categoryName,
                ["Message"] = SanitizeText(formatter(state, exception)),
                ["AppVersion"] = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(),
                ["OperatingSystem"] = RuntimeInformation.OSDescription,
                ["ProcessArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString()
            };

            AddState(fields, state);
            _scopeProvider.ForEachScope((scope, destination) => AddState(destination, scope), fields);
            if (exception is not null)
            {
                fields["ExceptionType"] = exception.GetType().FullName;
                fields["ExceptionMessage"] = SanitizeText(exception.Message);
                fields["ExceptionStackTrace"] = SanitizeText(exception.StackTrace ?? string.Empty);
            }

            var json = JsonSerializer.Serialize(fields);
            lock (_syncRoot)
            {
                if (_disposed) return;
                Directory.CreateDirectory(_options.LogDirectory);
                var path = ResolveCurrentFile(json.Length + Environment.NewLine.Length);
                File.AppendAllText(path, json + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                CleanupBestEffort();
            }
        }
        catch (Exception exceptionDuringLogging) when (exceptionDuringLogging is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or JsonException
            or CryptographicException)
        {
            // Logging is deliberately fail-open and cannot change a business result.
        }
    }

    public string CreatePathToken(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = Path.GetFullPath(path);
        using var hmac = new HMACSHA256(_pathTokenKey);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(normalized)))[..16];
    }

    public static string CreateDiagnosticId() =>
        "APX-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6));

    private void AddState(IDictionary<string, object?> destination, object? state)
    {
        if (state is not IEnumerable<KeyValuePair<string, object?>> values) return;
        foreach (var (key, value) in values)
        {
            if (key == "{OriginalFormat}" || value is null) continue;
            if (IsPathField(key))
            {
                var tokenKey = key.EndsWith("Token", StringComparison.Ordinal) ? key : key + "Token";
                destination[tokenKey] = CreatePathToken(value.ToString()!);
                continue;
            }

            destination[key] = value is string text ? SanitizeText(text) : value;
        }
    }

    private static bool IsPathField(string key) =>
        key.Contains("Path", StringComparison.OrdinalIgnoreCase)
        || key.Contains("FileName", StringComparison.OrdinalIgnoreCase);

    private static string SanitizeText(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var withoutWindowsPaths = Regex.Replace(
            value,
            @"(?i)(?<![A-Z0-9])[A-Z]:[\\/][^\s\""']+",
            "[path]");
        return Regex.Replace(
            withoutWindowsPaths,
            @"(?<![A-Za-z0-9])/(?:[^/\s\""']+/)*[^/\s\""']+",
            "[path]");
    }

    private string ResolveCurrentFile(int appendBytes)
    {
        var prefix = $"atompix-{DateTime.UtcNow:yyyyMMdd}-";
        for (var index = 0; ; index++)
        {
            var candidate = Path.Combine(_options.LogDirectory, $"{prefix}{index:D3}.jsonl");
            var length = File.Exists(candidate) ? new FileInfo(candidate).Length : 0;
            if (length == 0 || length + appendBytes <= _options.MaxFileSizeBytes) return candidate;
        }
    }

    private void TryPrepareStorage()
    {
        try
        {
            Directory.CreateDirectory(_options.LogDirectory);
            CleanupBestEffort();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }
    }

    private void CleanupBestEffort()
    {
        try
        {
            if (!Directory.Exists(_options.LogDirectory)) return;
            var files = new DirectoryInfo(_options.LogDirectory)
                .EnumerateFiles("atompix-*.jsonl", SearchOption.TopDirectoryOnly)
                .OrderBy(file => file.LastWriteTimeUtc)
                .ToList();
            var cutoff = DateTime.UtcNow.AddDays(-_options.RetentionDays);
            foreach (var expired in files.Where(file => file.LastWriteTimeUtc < cutoff).ToArray())
            {
                expired.Delete();
                files.Remove(expired);
            }

            long total = files.Sum(file => file.Exists ? file.Length : 0);
            foreach (var oldest in files)
            {
                if (total <= _options.MaxTotalSizeBytes) break;
                var length = oldest.Length;
                oldest.Delete();
                total -= length;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }
    }

    private sealed class LocalJsonLogger(LocalJsonLoggerProvider provider, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            provider._scopeProvider.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            ArgumentNullException.ThrowIfNull(formatter);
            provider.Write(categoryName, logLevel, eventId, state, exception, formatter);
        }
    }
}
