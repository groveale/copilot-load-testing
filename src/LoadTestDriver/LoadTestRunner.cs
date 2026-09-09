// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics;
using CopilotStudioClientSample;
using Microsoft.Agents.CopilotStudio.Client;
using Microsoft.Extensions.Logging;
using Activity = Microsoft.Agents.Core.Models.Activity;

namespace CopilotStudioLoadTestDriver
{
    /// <summary>
    /// Runs one session per configured user, each sending its own sequence of messages,
    /// with multiple users' sessions running concurrently with each other.
    ///
    /// Within a single user's session, messages are sent sequentially - one conversation
    /// turn at a time, waiting for each response - which mirrors how a real person
    /// actually chats rather than firing N messages from one identity simultaneously.
    /// Concurrency comes from running several distinct, separately-authenticated users at
    /// once, not from parallelising a single user's own messages.
    ///
    /// Client-side timings here (FirstActivityMs/CompleteMs) are a quick sanity check
    /// only; treat service-side telemetry (Application Insights / Copilot Studio
    /// analytics) as the source of truth for real latency figures.
    /// </summary>
    internal class LoadTestRunner(
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        SampleConnectionSettings connectionSettings,
        LoadTestSettings loadTestSettings,
        MultiUserAuthManager authManager,
        ILogger<LoadTestRunner> logger)
    {
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            if (loadTestSettings.Users.Count == 0)
            {
                Console.WriteLine("No users configured under LoadTestSettings:Users in appsettings.json - add at least one UPN and re-run.");
                return;
            }

            logger.LogInformation(
                "Signing in {UserCount} user(s) sequentially (one device code at a time)...",
                loadTestSettings.Users.Count);
            IReadOnlyList<string> readyUsers = await authManager.SignInAllAsync(loadTestSettings.Users, cancellationToken);

            if (readyUsers.Count == 0)
            {
                Console.WriteLine("No users signed in successfully - aborting load test.");
                return;
            }

            int maxConcurrentUsers = loadTestSettings.MaxConcurrentUsers > 0
                ? Math.Min(loadTestSettings.MaxConcurrentUsers, readyUsers.Count)
                : readyUsers.Count;

            logger.LogInformation(
                "Starting load test: {UserCount} signed-in user(s), up to {MaxConcurrent} running in parallel, {MessagesPerUser} message(s) each",
                readyUsers.Count, maxConcurrentUsers, loadTestSettings.MessagesPerUser);

            var results = new ConcurrentBag<TurnResult>();
            using SemaphoreSlim gate = new(maxConcurrentUsers, maxConcurrentUsers);

            Stopwatch overallStopwatch = Stopwatch.StartNew();
            IEnumerable<Task> tasks = readyUsers.Select(upn => RunUserSessionAsync(upn, gate, results, cancellationToken));
            await Task.WhenAll(tasks);
            overallStopwatch.Stop();

            WriteResultsCsv(results);
            PrintSummary(results, overallStopwatch.Elapsed);
        }

        private async Task RunUserSessionAsync(string upn, SemaphoreSlim gate, ConcurrentBag<TurnResult> results, CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                ILogger<CopilotClient> clientLogger = loggerFactory.CreateLogger<CopilotClient>();
                CopilotClient copilotClient = new(connectionSettings, httpClientFactory, clientLogger, upn);

                string conversationId = "(unknown)";
                try
                {
                    DateTime sendUtc = DateTime.UtcNow;
                    Stopwatch sw = Stopwatch.StartNew();
                    double? firstActivityMs = null;
                    string lastMessageText = string.Empty;

                    await foreach (Activity act in copilotClient.StartConversationAsync(emitStartConversationEvent: true, cancellationToken: cancellationToken))
                    {
                        firstActivityMs ??= sw.Elapsed.TotalMilliseconds;
                        if (!string.IsNullOrEmpty(act.Conversation?.Id))
                        {
                            conversationId = act.Conversation.Id;
                        }
                        if (act.Type == "message" && !string.IsNullOrEmpty(act.Text))
                        {
                            lastMessageText = act.Text;
                        }
                    }

                    results.Add(BuildResult(upn, 0, conversationId, sendUtc, userMessage: "(start conversation)", firstActivityMs, sw.Elapsed.TotalMilliseconds, lastMessageText, errorDetail: null));

                    for (int turn = 1; turn <= loadTestSettings.MessagesPerUser; turn++)
                    {
                        string prompt = PickPrompt();
                        sendUtc = DateTime.UtcNow;
                        sw.Restart();
                        firstActivityMs = null;
                        lastMessageText = string.Empty;

                        await foreach (Activity act in copilotClient.AskQuestionAsync(prompt, null, cancellationToken))
                        {
                            firstActivityMs ??= sw.Elapsed.TotalMilliseconds;
                            if (act.Type == "message" && !string.IsNullOrEmpty(act.Text))
                            {
                                lastMessageText = act.Text;
                            }
                        }

                        results.Add(BuildResult(upn, turn, conversationId, sendUtc, userMessage: prompt, firstActivityMs, sw.Elapsed.TotalMilliseconds, lastMessageText, errorDetail: null));
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "User session {Upn} failed", upn);
                    results.Add(new TurnResult
                    {
                        User = upn,
                        TurnIndex = -1,
                        ConversationId = conversationId,
                        SendUtc = DateTime.UtcNow,
                        UserMessage = loadTestSettings.TestPrompt,
                        Status = "Error",
                        ErrorDetail = ex.Message
                    });
                }
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// Picks a random prompt from the configured PromptBank (uniform distribution) so
        /// repeated turns within/across users aren't all identical. Falls back to the
        /// single TestPrompt if no bank is configured. Random.Shared is thread-safe, so
        /// this is safe to call from many concurrent user sessions.
        /// </summary>
        private string PickPrompt()
        {
            List<string> bank = loadTestSettings.PromptBank;
            return bank.Count > 0 ? bank[Random.Shared.Next(bank.Count)] : loadTestSettings.TestPrompt;
        }

        /// <summary>
        /// Classifies a completed turn as Answered/Refused based on the response text.
        /// NOTE: this is a best-effort heuristic. Copilot Studio throttling/refusals can
        /// arrive as an ordinary-looking successful activity whose body contains an error
        /// code rather than an HTTP error status - inspect a real refusal in the output
        /// CSV and tighten this check with the actual marker text/error code your agent
        /// returns before relying on refusal counts at scale.
        /// </summary>
        private static TurnResult BuildResult(string user, int turnIndex, string conversationId, DateTime sendUtc, string userMessage, double? firstActivityMs, double completeMs, string responseText, string? errorDetail)
        {
            bool looksRefused = !string.IsNullOrEmpty(responseText) &&
                (responseText.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                 responseText.Contains("throttle", StringComparison.OrdinalIgnoreCase) ||
                 responseText.Contains("quota", StringComparison.OrdinalIgnoreCase) ||
                 responseText.Contains("try again", StringComparison.OrdinalIgnoreCase));

            return new TurnResult
            {
                User = user,
                TurnIndex = turnIndex,
                ConversationId = conversationId,
                SendUtc = sendUtc,
                UserMessage = userMessage,
                FirstActivityMs = firstActivityMs,
                CompleteMs = completeMs,
                Status = looksRefused ? "Refused" : "Answered",
                ResponseText = responseText,
                ErrorDetail = errorDetail
            };
        }

        private static void WriteResultsCsv(ConcurrentBag<TurnResult> results)
        {
            string outputDir = GetOutputDirectory();
            Directory.CreateDirectory(outputDir);
            string fileName = $"loadtest-results_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            string path = Path.Combine(outputDir, fileName);
            using StreamWriter writer = new(path, append: false);
            writer.WriteLine(TurnResult.CsvHeader);
            foreach (TurnResult result in results.OrderBy(r => r.User).ThenBy(r => r.TurnIndex))
            {
                writer.WriteLine(result.ToCsvRow());
            }
            Console.WriteLine($"\nResults written to {path}");
        }

        /// <summary>
        /// Resolves the repo-root "output" folder so results don't get buried in bin\ and
        /// don't get wiped out by a clean/rebuild. Walks up from the build output directory
        /// looking for the repo's ".gitignore" as a repo-root marker rather than assuming a
        /// fixed number of parent directories, so it keeps working if the build config
        /// (Debug/Release, TFM) changes.
        /// </summary>
        private static string GetOutputDirectory()
        {
            DirectoryInfo? dir = new(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, ".gitignore")))
                {
                    return Path.Combine(dir.FullName, "output");
                }
                dir = dir.Parent;
            }

            // Fallback: couldn't find the repo root marker, just use the build output dir.
            return Path.Combine(AppContext.BaseDirectory, "output");
        }

        private static void PrintSummary(ConcurrentBag<TurnResult> results, TimeSpan wallClockElapsed)
        {
            List<TurnResult> completed = results.Where(r => r.Status is "Answered" or "Refused").ToList();
            int answered = completed.Count(r => r.Status == "Answered");
            int refused = completed.Count(r => r.Status == "Refused");
            int errored = results.Count(r => r.Status == "Error");

            Console.WriteLine();
            Console.WriteLine("==== Load Test Summary ====");
            Console.WriteLine($"Wall clock time:   {wallClockElapsed.TotalSeconds:F1}s");
            Console.WriteLine($"Total turns:       {results.Count}");
            Console.WriteLine($"Answered:          {answered}");
            Console.WriteLine($"Refused (heuristic): {refused}");
            Console.WriteLine($"Errored:           {errored}");

            List<double> completeTimes = completed.Where(r => r.Status == "Answered" && r.CompleteMs.HasValue)
                .Select(r => r.CompleteMs!.Value)
                .OrderBy(x => x)
                .ToList();

            if (completeTimes.Count > 0)
            {
                Console.WriteLine($"Client-observed latency (answered turns only, sanity-check only - not the source of truth):");
                Console.WriteLine($"  p50: {Percentile(completeTimes, 0.50):F0} ms");
                Console.WriteLine($"  p90: {Percentile(completeTimes, 0.90):F0} ms");
                Console.WriteLine($"  p99: {Percentile(completeTimes, 0.99):F0} ms");
            }
            Console.WriteLine("Cross-reference ConversationId values above against Application Insights / Copilot Studio analytics for real latency figures.");
        }

        private static double Percentile(List<double> sortedValues, double percentile)
        {
            if (sortedValues.Count == 0) return 0;
            int index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
            index = Math.Clamp(index, 0, sortedValues.Count - 1);
            return sortedValues[index];
        }
    }
}
