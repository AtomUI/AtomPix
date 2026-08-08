namespace AtomPix.Infrastructure.Tests;

using System.Text.Json;
using AtomPix.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;

public sealed class DiagnosticsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AtomPixDiagnosticsTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Provider_writes_scoped_json_and_redacts_paths_from_state_message_and_exception()
    {
        var logDirectory = Path.Combine(_root, "logs");
        using var provider = new LocalJsonLoggerProvider(new LocalJsonLoggerOptions(logDirectory));
        var logger = provider.CreateLogger("AtomPix.Tests");
        var secretPath = Path.Combine(_root, "private", "holiday.jpg");

        using (logger.BeginScope(new Dictionary<string, object?> { ["OperationId"] = "op-1", ["InputPath"] = secretPath }))
        {
            logger.Log(
                LogLevel.Error,
                new EventId(9001, "UnexpectedFailure"),
                new Dictionary<string, object?> { ["OutputPath"] = secretPath, ["ErrorCode"] = "Unknown" },
                new InvalidOperationException($"Cannot read {secretPath}"),
                (state, error) => $"Failed at {secretPath}");
        }

        var line = File.ReadLines(Assert.Single(Directory.GetFiles(logDirectory, "*.jsonl"))).Single();
        Assert.DoesNotContain(secretPath, line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("holiday.jpg", line, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        Assert.Equal("UnexpectedFailure", root.GetProperty("EventName").GetString());
        Assert.Equal("op-1", root.GetProperty("OperationId").GetString());
        Assert.Equal("Failed at [path]", root.GetProperty("Message").GetString());
        Assert.Equal(root.GetProperty("InputPathToken").GetString(), root.GetProperty("OutputPathToken").GetString());
        Assert.Equal("System.InvalidOperationException", root.GetProperty("ExceptionType").GetString());
    }

    [Fact]
    public void Path_tokens_are_stable_only_within_one_session()
    {
        using var first = new LocalJsonLoggerProvider(new LocalJsonLoggerOptions(Path.Combine(_root, "one")));
        using var second = new LocalJsonLoggerProvider(new LocalJsonLoggerOptions(Path.Combine(_root, "two")));
        var path = Path.Combine(_root, "sample.jpg");

        Assert.Equal(first.CreatePathToken(path), first.CreatePathToken(path));
        Assert.NotEqual(first.CreatePathToken(path), second.CreatePathToken(path));
        Assert.Matches("^APX-[0-9A-F]{12}$", LocalJsonLoggerProvider.CreateDiagnosticId());
    }

    [Fact]
    public void Provider_rolls_small_files_and_logging_failure_is_fail_open()
    {
        var logDirectory = Path.Combine(_root, "rolling");
        using (var provider = new LocalJsonLoggerProvider(new LocalJsonLoggerOptions(logDirectory, 700, 2_100, 7)))
        {
            var logger = provider.CreateLogger("AtomPix.Tests");
            for (var index = 0; index < 12; index++)
            {
                logger.LogInformation(new EventId(1, "Rolling"), "Message {Index} {Payload}", index, new string('x', 180));
            }
        }

        var files = Directory.GetFiles(logDirectory, "*.jsonl");
        Assert.InRange(files.Length, 2, 4);
        Assert.True(files.Sum(path => new FileInfo(path).Length) <= 2_100);

        Directory.CreateDirectory(_root);
        var fileInsteadOfDirectory = Path.Combine(_root, "not-a-directory");
        File.WriteAllText(fileInsteadOfDirectory, "occupied");
        using var failing = new LocalJsonLoggerProvider(new LocalJsonLoggerOptions(fileInsteadOfDirectory));
        var exception = Record.Exception(() => failing.CreateLogger("AtomPix.Tests").LogInformation("Still a business success"));
        Assert.Null(exception);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
