// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace CopilotStudioLoadTestDriver
{
    /// <summary>
    /// One row of load-test output: a single turn (one question -> one or more response
    /// activities) within a single simulated conversation.
    /// </summary>
    internal record TurnResult
    {
        public required string User { get; init; }
        public required int ConversationIndex { get; init; }
        public required int TurnIndex { get; init; }
        public string ConversationId { get; init; } = "(none)";
        public required DateTime SendUtc { get; init; }
        public string UserMessage { get; init; } = string.Empty;
        public double? FirstActivityMs { get; init; }
        public double? CompleteMs { get; init; }
        public required string Status { get; init; } // "Answered", "Refused", "Error"
        public string ResponseText { get; init; } = string.Empty;
        public string? ErrorDetail { get; init; }

        /// <summary>
        /// Renders this result as one CSV row. Response text is truncated and quoted so a
        /// long/markdown-heavy answer can't break the row structure.
        /// </summary>
        public string ToCsvRow()
        {
            string Escape(string s) => "\"" + s.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";

            string truncatedResponse = ResponseText.Length > 500 ? ResponseText[..500] + "..." : ResponseText;

            return string.Join(',',
            [
                Escape(User),
                ConversationIndex.ToString(),
                TurnIndex.ToString(),
                Escape(ConversationId),
                SendUtc.ToString("O"),
                Escape(UserMessage),
                FirstActivityMs?.ToString("F1") ?? "",
                CompleteMs?.ToString("F1") ?? "",
                Status,
                Escape(truncatedResponse),
                Escape(ErrorDetail ?? "")
            ]);
        }

        public static string CsvHeader =>
            "User,ConversationIndex,TurnIndex,ConversationId,SendUtc,UserMessage,FirstActivityMs,CompleteMs,Status,ResponseTextTruncated,ErrorDetail";
    }
}
