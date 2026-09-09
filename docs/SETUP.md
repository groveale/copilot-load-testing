# Copilot Studio Load Testing — Setup Guide

This project uses the **Microsoft 365 Agents SDK `CopilotStudioClient`** (DirectToEngine
channel) to talk directly to a published Copilot Studio agent as a licensed Microsoft 365
Copilot user, bypassing Direct Line (which consumes Copilot credits) and bypassing browser
automation (which doesn't scale for high-concurrency load testing).

The `src/CopilotStudioClientSample` project is Microsoft's official interactive console
sample, wired up with this tenant/agent's connection details. It's the fastest way to prove
the whole auth + connectivity chain works end-to-end for **one identity** before scaling up
to a real load test (100–200 concurrent, then 1000).

---

## 1. Prerequisites

### 1.1 Microsoft 365 / licensing
- One (eventually a pool of, for the real load test) **Microsoft 365 Copilot licensed
  seat**. The signed-in user must hold this license for the agent invocation to stay on the
  credit-free path.

### 1.2 Power Platform API service principal (one-time, per tenant)
The delegated permission this sample needs (`Copilot Studio.Copilots.Invoke`) lives under an
Entra app called **Power Platform API**. On tenants that haven't used Power Platform
API–backed tooling before, that app has no service principal yet and **will not show up** in
the "APIs my organization uses" search — no matter how you search for it.

Provision it once via Microsoft Graph (Graph Explorer is the fastest, and sidesteps local
tooling issues like Azure CLI tenant-selection errors or PowerShell `Microsoft.Graph` module
version conflicts):

1. Go to https://developer.microsoft.com/en-us/graph/graph-explorer
2. Sign in with an account that is **Application Administrator**, **Cloud Application
   Administrator**, or **Global Administrator**.
3. Consent to `Application.ReadWrite.All` when prompted.
4. Run:
   - Method: `POST`
   - URL: `https://graph.microsoft.com/v1.0/servicePrincipals`
   - Body:
     ```json
     { "appId": "8578e004-a5c6-46e7-913e-12f58912df43" }
     ```
5. `201 Created` = done. Wait a couple of minutes for propagation before continuing.

(PowerShell/Azure CLI equivalents exist too — see the official doc linked below — but Graph
Explorer avoids the module/tenant-selection issues we hit.)

### 1.3 Entra ID app registration
1. Portal → **Microsoft Entra ID** → **App registrations** → **New registration**.
2. Name it, choose **"Accounts in this organizational directory only"**.
3. Platform: **Public client/native (mobile & desktop)**.
4. Redirect URI: `http://localhost` (**http**, not https).
5. Register.
6. Note the **Application (client) ID** and **Directory (tenant) ID** from the Overview page.
7. **API permissions** → **Add a permission** → **APIs my organization uses** → search
   `Power Platform API` (now visible thanks to step 1.2) → **Delegated permissions** →
   **Copilot Studio** → check **`Copilot Studio.Copilots.Invoke`** → **Add permissions**.
8. Click **Grant admin consent** for the tenant.
9. **Authentication** blade → scroll to **Advanced settings** → set **"Allow public
   client flows"** → **Yes** → **Save**. This is required for the `LoadTestDriver`'s
   device-code sign-in (`AcquireTokenWithDeviceCode`) — without it you'll hit
   `AADSTS7000218: invalid_client ... must contain client_assertion or client_secret`,
   because the token endpoint otherwise treats the app as a confidential client. The
   smoke test's interactive/loopback flow doesn't need this toggle, which is why it can
   work even if you skip this step — but the load-test driver will fail without it.

No client secret is required — this is a public client using the interactive/device
auth flows, not a confidential client.

### 1.4 Copilot Studio agent
1. The agent must be **published** — DirectToEngine talks to the published endpoint, not the
   test canvas/draft.
2. Agent → **Settings** → **Advanced** → **Metadata** → note:
   - **Environment Id**
   - **Schema name**
3. Confirm the signed-in test user has whatever environment/security-group access is needed
   to see this agent (same access a real end user would need).

### 1.5 Dev machine
- **.NET 8 SDK** installed.
- That's it for this sample — NuGet restores `Microsoft.Extensions.Hosting`,
  `Microsoft.Identity.Client.Extensions.Msal`, and `Microsoft.Agents.CopilotStudio.Client`
  automatically on first build.

---

## 2. Configuration

All connection details live in
`src/CopilotStudioClientSample/appsettings.json`, under `CopilotStudioClientSettings`:

```json
{
  "CopilotStudioClientSettings": {
    "EnvironmentId": "17689662-d743-eb91-9e33-25fe8d537f15",
    "SchemaName": "cr1e9_Test1SharePointasAgentKnowledge",
    "TenantId": "d8464949-bb1d-476f-bbe8-a46293833f6d",
    "AppClientId": "97fc4309-90a1-463c-ab85-80a523ffe284",
    "AppClientSecret": "",
    "UseS2SConnection": false
  }
}
```

This has already been filled in with your agent/app registration values above. Leave
`AppClientSecret` empty and `UseS2SConnection` as `false` — this sample uses interactive
user auth (device/browser sign-in + cached token), not service-to-service auth (S2S isn't
currently supported for Copilot Studio anyway).

> If you ever need a *different* set of values (e.g. testing against another agent or app
> registration) without touching this file, an `appsettings.Development.json` sitting next
> to it will override the same keys and is a good place to keep anything you don't want
> committed — it's already excluded via `.gitignore`.

---

## 3. Running the smoke test

```powershell
cd src\CopilotStudioClientSample
dotnet run
```

What happens:
1. On first run, MSAL has no cached token for this app, so it opens a browser window for you
   to sign in as the licensed test user and consent to the `Copilot Studio.Copilots.Invoke`
   permission.
2. The token is cached to disk (`mcs_client_console/` next to the built exe), so subsequent
   runs reuse it silently until it expires — no repeated interactive logins.
3. The console starts a conversation with the agent (`agent>` prompt) and then lets you type
   messages back and forth (`user>` prompt).
4. Each turn's round-trip duration is written to the Trace output (`>>>>MessageLoop
   Duration: ...`) — useful for a quick sanity check, though your source of truth for real
   load-test latency numbers should be Application Insights / Copilot Studio analytics, not
   this client-side timing.

If this works end-to-end for one identity, you've validated: the app registration, the
`Copilot Studio.Copilots.Invoke` permission + admin consent, the Environment Id/Schema name,
and that the agent is reachable via DirectToEngine. That's the baseline before scaling to
100–200 concurrent identities for the real pilot.

---

## 4. Troubleshooting quick reference

| Symptom | Likely cause |
|---|---|
| `Power Platform API` not found when adding API permission | Service principal not provisioned in tenant — see §1.2 |
| `AADSTS...` consent/permission errors on sign-in | Admin consent (§1.3 step 8) not granted yet, or wrong tenant/account signed in |
| `New-MgServicePrincipal` assembly load error in PowerShell | `Microsoft.Graph` module version conflict — use Graph Explorer instead (§1.2), or start a fresh PowerShell session and only import `Microsoft.Graph.Authentication` / `Microsoft.Graph.Applications` submodules, never the full `Microsoft.Graph` umbrella module |
| `az ad sp create` → "Invalid selection" | Ambiguous tenant/subscription context — run `az account show`, then `az login --tenant <tenantId> --allow-no-subscriptions` |
| `AppClientId not found in config` / `TenantId not found in config` | `appsettings.json` values missing/empty for those keys |
| `AADSTS7000218: invalid_client ... must contain client_assertion or client_secret` (LoadTestDriver only) | App registration isn't marked as a public client — enable **Authentication → Advanced settings → Allow public client flows → Yes** (§1.3 step 9) |

---

## 5. Running a load-test pilot

`src/LoadTestDriver` is a separate console app that runs **one session per configured
user**, each user sending its own sequence of messages, with multiple users' sessions
running **concurrently with each other**. Within one user's own session, messages are
sent sequentially (one turn at a time, waiting for each response) — that mirrors how a
real person actually chats. Concurrency comes from running several distinct,
separately-authenticated users at once, not from one identity firing many messages in
parallel.

```powershell
.\scripts\run-loadtest.ps1
```

**Auth — one identity, one token cache, per configured user.** The smoke test's
`AddTokenHandler` builds a fresh MSAL `PublicClientApplication` per HTTP request and
falls back to an interactive browser login — fine for one conversation, but wrong for
concurrent users (it would either pop up N browser windows at once, or blend everyone
onto a single shared identity). `LoadTestDriver` instead uses:

- `MultiUserAuthManager` — builds **one `PublicClientApplication` and one on-disk token
  cache per user** (under `mcs_loadtest_cache/<sanitized-upn>/`), so each simulated user
  keeps a fully separate identity/token. `SignInAllAsync` signs users in **one at a time,
  sequentially** — never concurrently — because showing several device codes at once
  would make it impossible to tell which code belongs to which account.
- `PerUserAuthHandler` — a thin `DelegatingHandler`, one instance per user, that just
  attaches that user's cached token to outgoing requests.
- `Program.cs` registers one named `HttpClient` per configured user (`Users` in
  `appsettings.json`), each wired to its own `PerUserAuthHandler`.

**What you'll see when you run it:** a `=== Signing in: <upn> ===` heading followed by a
device-code prompt for each user in `Users`, one after another — sign into each with the
matching test account when its code appears. Once every user has been attempted, a
status table prints (`OK`/`FAILED` per user) before the concurrent message-sending phase
starts. Each user's token cache is independent, so a user you've already signed in on a
previous run skips straight to a silent (no-prompt) token refresh.

Once the message-sending phase starts, each user's session prints its own progress
(`[user@contoso.com] Sending message 3/20...` / `... Received response for message 3/20
in 4231ms`), interleaved across all concurrently-running users — this is just to
confirm the run is progressing rather than hung, since a big pool can otherwise sit
silent for a while.

**Conversation model:** each user has **one ongoing conversation** for their whole
session — `StartConversationAsync` runs once, then all of that user's
`MessagesPerUser` prompts are sent as turns within that same conversation (like a real
back-and-forth chat), not as separate one-off conversations. Concurrency comes from
running multiple distinct users at once, each in their own conversation, not from
restarting a fresh conversation per message.

Configuration lives in `src/LoadTestDriver/appsettings.json` under `LoadTestSettings`:

```json
"LoadTestSettings": {
  "Users": [
    "user1@contoso.com",
    "user2@contoso.com"
  ],
  "MaxConcurrentUsers": 0,
  "MessagesPerUser": 20,
  "PromptBank": [
    "What is the best movie right now?",
    "Who is Tom Hanks?",
    "I want to move from italy what should i do"
  ],
  "TestPrompt": "What is the best movie right now?"
}
```

- **Users** — UPNs/emails of the licensed test identities to run. Each needs its own
  M365 Copilot license and its own one-time sign-in the first time you use it. Start with
  2–5 before scaling toward the pool size in your plan.
- **MaxConcurrentUsers** — caps how many users' sessions run at the same time; `0` means
  "all configured users run in parallel." Useful once `Users` grows larger than you want
  to run all-at-once (e.g. 50 users configured, only 10 running concurrently).
- **MessagesPerUser** — how many messages each user sends, one at a time, within their
  session (default 20).
- **PromptBank** — a pool of prompts; each message picks one uniformly at random, so
  repeated turns aren't all identical. Keep prompts answerable from general knowledge if
  you want to isolate pure generative latency (no SharePoint/tool triggering).
- **TestPrompt** — fallback used only if `PromptBank` is empty.

**Scaling up:** every entry in `Users` needs a real, individually licensed M365 Copilot
account and a one-time interactive/device-code sign-in — there's no way around signing
each one in at least once. Plan your pool size (and the time to sign each one in) before
a big run; consider signing accounts in over several sessions ahead of time, since
`mcs_loadtest_cache/` persists each user's token/refresh token between runs.

**Output:** `output/loadtest-results_<timestamp>.csv` at the **repo root** (not in `bin\`,
so `dotnet clean`/rebuilds never wipe your results) — one row per turn, with `User`
(the UPN), `ConversationId`, the `UserMessage` actually sent, client-observed
`FirstActivityMs`/`CompleteMs`, and a best-effort `Answered`/`Refused`/`Error`
classification. The refusal heuristic just string-matches the response text for words
like "error"/"throttle"/"quota" — **inspect a real refusal in the CSV and tighten this
once you see your agent's actual error format**, since Copilot Studio throttling can
arrive as an ordinary-looking successful activity whose body contains an error code
rather than an HTTP error status. Client-side timing here is a quick sanity check
only — cross-reference `ConversationId` against Application Insights/Copilot Studio
analytics for the latency numbers you actually report.

A console summary (answered/refused/errored counts, p50/p90/p99 of client-observed
latency) prints at the end of each run.

## 6. Next steps (beyond the multi-user pilot)

- A pool of ~100–200 licensed test identities with pre-captured/cached tokens (device code
  flow works well here — no browser needed per identity after the first sign-in), so
  concurrency isn't bottlenecked by one identity/token.
- Application Insights wired into the agent (Settings → Advanced → Application Insights) so
  latency is measured server-side, not from this client.
- A ramp controller (50 → 200 → 500 → 1000) that watches for throttling responses and steps
  up gradually rather than jumping straight to full scale.
- Tightening the refusal-detection heuristic in `LoadTestRunner.BuildResult` once you've
  seen a real refusal payload from your agent.

Reference: official sample and docs —
[Microsoft 365 Agents SDK — Integrate with web or native apps](https://learn.microsoft.com/en-us/microsoft-copilot-studio/publication-integrate-web-or-native-app-m365-agents-sdk),
[Power Platform API authentication](https://learn.microsoft.com/en-us/power-platform/admin/programmability-authentication-v2?tabs=powershell#step-2-configure-api-permissions).
