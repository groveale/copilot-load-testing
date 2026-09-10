// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace CopilotStudioLoadTestDriver
{
    /// <summary>
    /// Configuration for the concurrent load-test run, bound from the "LoadTestSettings"
    /// section of appsettings.json.
    /// </summary>
    internal class LoadTestSettings
    {
        /// <summary>
        /// UPNs/emails of the licensed test identities to load test with. Each gets its
        /// own MSAL token cache and its own sequential sign-in (device code) at startup -
        /// signing in 5 users concurrently would show 5 device codes at once with no way
        /// to tell which belongs to which account, so sign-in always happens one at a
        /// time before the load test itself starts.
        /// </summary>
        public List<string> Users { get; set; } = [];

        /// <summary>
        /// Caps how many users run their message sessions at the same time. 0 (default)
        /// means all successfully signed-in users run in parallel. Each user's own
        /// messages are sent sequentially (one conversation at a time, waiting for each
        /// response) - only the users themselves run concurrently with each other.
        /// </summary>
        public int MaxConcurrentUsers { get; set; } = 0;

        /// <summary>
        /// Number of separate, concurrent conversations each signed-in user spins up.
        /// The user's ONE authenticated token is reused across all of them - only the
        /// sign-in is per-user, conversations are just parallel uses of that same token.
        /// This isn't how a real single human uses the agent (nobody has 5 chats going
        /// at once), but it's a deliberate lever to multiply effective concurrency
        /// without needing a proportionally larger pool of licensed test accounts: e.g.
        /// 5 users x 5 conversations each x 20 messages = 100 messages in flight from
        /// just 5 real identities. Within each conversation, messages are still sent
        /// sequentially (one at a time, waiting for each response), matching a real
        /// conversational back-and-forth - only the conversations themselves run in
        /// parallel with each other.
        /// </summary>
        public int ConcurrentConversationsPerUser { get; set; } = 1;

        /// <summary>
        /// Number of messages each user sends within their conversation.
        /// </summary>
        public int MessagesPerUser { get; set; } = 20;

        /// <summary>
        /// Pool of prompts to pick from (uniformly at random) for each message, so
        /// repeated turns aren't all identical. If empty, falls back to <see cref="TestPrompt"/>.
        /// </summary>
        public List<string> PromptBank { get; set; } = [];

        /// <summary>
        /// Fallback single prompt used only when <see cref="PromptBank"/> is empty. Keep
        /// answerable from the model's general knowledge (no SharePoint/tool-triggering
        /// content) if you want to isolate pure generative latency.
        /// </summary>
        public string TestPrompt { get; set; } = "What is the best movie right now?";
    }
}
