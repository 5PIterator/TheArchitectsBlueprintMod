#if DEBUG
#nullable enable

using System;
using System.IO;
using System.Text;

namespace TheArchitectsBlueprintMod.Core.TestCases
{
    /// <summary>Forwards Test Case reports to an inner reporter and appends the same lines to a dump log file.</summary>
    internal sealed class FileDumpTestCaseReporter : ITestCaseReporter, IDisposable
    {
        private readonly ITestCaseReporter _inner;
        private readonly StreamWriter _writer;
        private bool _disposed;

        public string LogPath { get; }

        /// <summary>Opens a new dump log under the tests directory and announces the path.</summary>
        public FileDumpTestCaseReporter(ITestCaseReporter inner, string runLabel)
        {
            // --- Create the dump file first so every subsequent Pass/Fail line is persisted.
            ArgumentNullException.ThrowIfNull(inner);
            _inner = inner;
            LogPath = TestCasePaths.CreateRunLogPath(runLabel);
            _writer = new StreamWriter(LogPath, append: false, Encoding.UTF8)
            {
                AutoFlush = true,
            };
            _writer.WriteLine($"# Test Case dump started {DateTime.Now:O}");
            _writer.WriteLine($"# Run label: {runLabel}");
            _inner.Info($"Test Case log dump: {LogPath}");
        }

        public void Info(string message)
        {
            Write("INFO", message);
            _inner.Info(message);
        }

        public void Pass(string caseTag, string message)
        {
            Write("PASS", $"{caseTag} | {message}");
            _inner.Pass(caseTag, message);
        }

        public void Fail(string caseTag, string message)
        {
            Write("FAIL", $"{caseTag} | {message}");
            _inner.Fail(caseTag, message);
        }

        public void Abandon(string caseTag, string message)
        {
            Write("ABANDON", $"{caseTag} | {message}");
            _inner.Abandon(caseTag, message);
        }

        public void Error(string caseTag, string message)
        {
            Write("ERROR", $"{caseTag} | {message}");
            _inner.Error(caseTag, message);
        }

        /// <summary>Closes the dump file and reports the final path once more.</summary>
        public void Dispose()
        {
            // --- Flush the footer, dispose the writer, and echo the path for the chat/stick sink.
            if (_disposed)
                return;

            _disposed = true;
            try
            {
                _writer.WriteLine($"# Test Case dump ended {DateTime.Now:O}");
                _writer.Dispose();
            }
            catch
            {
                // Ignore close errors; chat summary still matters more than the file handle.
            }

            _inner.Info($"Test Case log dump saved: {LogPath}");
        }

        private void Write(string level, string message)
        {
            if (_disposed)
                return;

            try
            {
                _writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}");
            }
            catch
            {
                // Keep the in-game reporter working even if disk write fails mid-run.
            }
        }
    }
}
#endif
