// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using CopilotStudioClientSample;
using Microsoft.Agents.CopilotStudio.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace CopilotStudioLoadTestDriver
{
    internal record UserSignInStatus
    {
        public required string Upn { get; init; }
        public bool Success { get; init; }
        public string? SignedInAs { get; init; }
        public string? Error { get; init; }
    }

    /// <summary>
    /// Manages one MSAL PublicClientApplication (and its own on-disk token cache) PER
    /// configured user, so each simulated user carries a distinct identity/token through
    /// the whole run - unlike the single-shared-identity pilot, which authenticated once
    /// and reused that one token everywhere.
    ///
    /// Sign-in for all users happens sequentially, one device code at a time, via
    /// <see cref="SignInAllAsync"/> - BEFORE the concurrent load test starts. Showing
    /// several device codes at once would make it impossible to tell which code belongs
    /// to which account, so users are signed in one after another and a status table is
    /// printed once every account has been attempted.
    /// </summary>
    internal class MultiUserAuthManager(SampleConnectionSettings settings, ILogger<MultiUserAuthManager> logger)
    {
        private readonly Dictionary<string, IPublicClientApplication> _apps = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Signs in each of the given UPNs one at a time (silent-first, falling back to a
        /// device-code prompt), prints a status table, and returns the subset that signed
        /// in successfully and are ready to use in the load test.
        /// </summary>
        public async Task<IReadOnlyList<string>> SignInAllAsync(IReadOnlyList<string> upns, CancellationToken cancellationToken)
        {
            List<UserSignInStatus> statuses = [];

            foreach (string upn in upns)
            {
                Console.WriteLine();
                Console.WriteLine($"=== Signing in: {upn} ===");

                IPublicClientApplication app = await BuildAppAsync(upn);
                _apps[upn] = app;

                try
                {
                    IAccount? account = (await app.GetAccountsAsync()).FirstOrDefault();
                    AuthenticationResult result;
                    try
                    {
                        result = await app.AcquireTokenSilent(Scopes(), account).ExecuteAsync(cancellationToken);
                    }
                    catch (MsalUiRequiredException)
                    {
                        result = await app.AcquireTokenWithDeviceCode(Scopes(), callback =>
                        {
                            Console.WriteLine();
                            Console.WriteLine(callback.Message);
                            Console.WriteLine();
                            return Task.CompletedTask;
                        }).ExecuteAsync(cancellationToken);
                    }

                    string signedInAs = result.Account?.Username ?? "(unknown)";
                    if (!string.Equals(signedInAs, upn, StringComparison.OrdinalIgnoreCase))
                    {
                        logger.LogWarning(
                            "Expected to sign in as {Expected} but the account used was {Actual} - double-check you picked the right account for this prompt",
                            upn, signedInAs);
                    }
                    statuses.Add(new UserSignInStatus { Upn = upn, Success = true, SignedInAs = signedInAs });
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Sign-in failed for {Upn}", upn);
                    statuses.Add(new UserSignInStatus { Upn = upn, Success = false, Error = ex.Message });
                }
            }

            PrintStatusTable(statuses);
            return statuses.Where(s => s.Success).Select(s => s.Upn).ToList();
        }

        /// <summary>
        /// Returns a valid access token for the given (already signed-in) user. Safe to
        /// call concurrently - each user has their own cached account, so this is always a
        /// fast AcquireTokenSilent call with no prompt.
        /// </summary>
        public async Task<string> GetAccessTokenAsync(string upn, CancellationToken cancellationToken)
        {
            if (!_apps.TryGetValue(upn, out IPublicClientApplication? app))
            {
                throw new InvalidOperationException($"No signed-in app found for user '{upn}' - SignInAllAsync must complete before the load test starts.");
            }

            IAccount? account = (await app.GetAccountsAsync()).FirstOrDefault();
            AuthenticationResult result = await app.AcquireTokenSilent(Scopes(), account).ExecuteAsync(cancellationToken);
            return result.AccessToken;
        }

        private string[] Scopes() => [CopilotClient.ScopeFromSettings(settings)];

        private async Task<IPublicClientApplication> BuildAppAsync(string upn)
        {
            IPublicClientApplication app = PublicClientApplicationBuilder.Create(settings.AppClientId)
                .WithAuthority(AadAuthorityAudience.AzureAdMyOrg)
                .WithTenantId(settings.TenantId)
                .WithRedirectUri("http://localhost")
                .Build();

            // Each user gets their own cache subfolder so N users' tokens never collide
            // or overwrite each other.
            string safeName = SanitizeForPath(upn);
            string cacheDir = Path.Combine(AppContext.BaseDirectory, "mcs_loadtest_cache", safeName);
            Directory.CreateDirectory(cacheDir);

            StorageCreationPropertiesBuilder storageProperties = new($"LoadTestTokenCache_{safeName}", cacheDir);
            if (OperatingSystem.IsLinux())
            {
                storageProperties.WithLinuxUnprotectedFile();
            }
            if (OperatingSystem.IsMacOS())
            {
                storageProperties.WithMacKeyChain($"copilot_studio_loadtest_{safeName}", safeName);
            }
            MsalCacheHelper cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties.Build());
            cacheHelper.RegisterCache(app.UserTokenCache);

            return app;
        }

        private static string SanitizeForPath(string upn)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                upn = upn.Replace(c, '_');
            }
            return upn;
        }

        private static void PrintStatusTable(List<UserSignInStatus> statuses)
        {
            Console.WriteLine();
            Console.WriteLine("==== User Sign-in Status ====");
            foreach (UserSignInStatus s in statuses)
            {
                Console.WriteLine(s.Success
                    ? $"  OK      {s.Upn}  (signed in as {s.SignedInAs})"
                    : $"  FAILED  {s.Upn}  - {s.Error}");
            }
            Console.WriteLine();
        }
    }
}
