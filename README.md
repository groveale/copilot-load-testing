# Copilot Studio Load Testing

An experimental .NET 8 harness for smoke testing and running concurrent,
multi-user load-test pilots against a published Microsoft Copilot Studio agent
through the Microsoft 365 Agents SDK.

The repository contains:

- `src/CopilotStudioClientSample` - a single-user interactive smoke test based
  on Microsoft's Copilot Studio client sample.
- `src/LoadTestDriver` - a multi-user driver with separate authentication and
  token caches for each test identity.
- `scripts` - PowerShell entry points for the smoke test and load-test driver.
- `output` - timestamped CSV results produced by load-test runs.

## Getting started

Read [docs/SETUP.md](docs/SETUP.md) for prerequisites, Entra ID registration,
Copilot Studio configuration, authentication, and troubleshooting.

After completing the setup, run the single-user smoke test first:

```powershell
.\scripts\smoke-test.ps1
```

Then configure the test identities and prompts in
`src/LoadTestDriver/appsettings.json` and run the pilot:

```powershell
.\scripts\run-loadtest.ps1
```

Each test identity must be authorized to access the agent and must have the
required Microsoft 365 Copilot license. Results are written to `output`.

## Important notices

> [!IMPORTANT]
> This repository is an experimental sample, not a Microsoft product or
> service. It is not officially supported by Microsoft and has no service-level
> agreement. It is provided "as is," without warranties or guarantees of any
> kind. Use it at your own risk.

- The load-test driver in this repository is custom tooling. Its results are
  not official Microsoft benchmarks, capacity commitments, or performance
  guarantees.
- Run tests only against tenants, environments, agents, and user accounts that
  you own or are explicitly authorized to test. Obtain approval from the
  relevant tenant, environment, security, and service owners before generating
  load.
- You are responsible for complying with the applicable Microsoft product
  terms, licensing requirements, acceptable-use policies, service limits, and
  throttling guidance. Load tests may consume capacity or incur charges.
- Do not place client secrets, access tokens, production credentials, or
  sensitive personal or organizational data in configuration, prompts, logs,
  or committed result files. CSV output may contain user identifiers,
  conversation identifiers, prompts, responses, and timing data.
- Validate this code, its dependencies, and its security and compliance posture
  before using it beyond an isolated test environment. It is not intended for
  production use.
- Microsoft, Microsoft 365, Microsoft Copilot, Copilot Studio, Entra, and
  related names and logos are trademarks of the Microsoft group of companies.
  Use of those names does not imply Microsoft sponsorship or endorsement of
  this repository.

The Microsoft SDK and upstream sample remain subject to their own licenses,
terms, documentation, and support policies.