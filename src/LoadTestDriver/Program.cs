// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using CopilotStudioClientSample;
using CopilotStudioLoadTestDriver;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

SampleConnectionSettings connectionSettings = new(builder.Configuration.GetSection("CopilotStudioClientSettings"));
LoadTestSettings loadTestSettings = new();
builder.Configuration.GetSection("LoadTestSettings").Bind(loadTestSettings);

builder.Services
    .AddSingleton(connectionSettings)
    .AddSingleton(loadTestSettings)
    .AddSingleton<MultiUserAuthManager>()
    .AddSingleton<LoadTestRunner>();

// One named HttpClient per configured user, each wired to its own PerUserAuthHandler.
// Keeping these separate (rather than one shared client) is what lets each simulated
// user carry a distinct identity/token through the whole run.
foreach (string upn in loadTestSettings.Users)
{
    builder.Services.AddHttpClient(upn).ConfigurePrimaryHttpMessageHandler(sp =>
        new PerUserAuthHandler(sp.GetRequiredService<MultiUserAuthManager>(), upn));
}

IHost host = builder.Build();

LoadTestRunner runner = host.Services.GetRequiredService<LoadTestRunner>();
await runner.RunAsync(CancellationToken.None);
