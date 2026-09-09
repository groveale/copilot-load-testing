// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Net.Http.Headers;

namespace CopilotStudioLoadTestDriver
{
    /// <summary>
    /// Adds the bearer token for ONE specific user to every outgoing request on this
    /// HttpClient. A separate named HttpClient (and therefore a separate instance of this
    /// handler) is registered per configured user in Program.cs, which is what keeps each
    /// simulated user's identity/token isolated from the others.
    /// </summary>
    internal class PerUserAuthHandler(MultiUserAuthManager authManager, string upn) : DelegatingHandler(new HttpClientHandler())
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Authorization is null)
            {
                string accessToken = await authManager.GetAccessTokenAsync(upn, cancellationToken);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }
            return await base.SendAsync(request, cancellationToken);
        }
    }
}
