using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;

namespace AK.OAuthSamples.OpenIdDict.Server.Configuration;

internal static class OpenIdDictEvents
{
	internal static Func<OpenIddictServerEvents.ValidateAuthorizationRequestContext, ValueTask> ValidateAuthorizationRequestFunc(AppSettings.AuthCredentialsSettings authSettings) => 
		validateAuthorizationRequestContext =>
		{
			if (!string.Equals(validateAuthorizationRequestContext.ClientId, authSettings.ClientId, StringComparison.OrdinalIgnoreCase))
			{
				validateAuthorizationRequestContext.Reject(
					error: OpenIddictConstants.Errors.InvalidClient,
					description: "The specified 'client_id' doesn't match a registered application.");
				return default;
			}
			if (authSettings.RedirectUris?.Any() != true)
			{
				validateAuthorizationRequestContext.Reject(
					error: OpenIddictConstants.Errors.InvalidRequestUri,
					description: "Server has no configured allowed 'redirect_uri'.");
				return default;
			}
			if (!authSettings.RedirectUris.Contains(validateAuthorizationRequestContext.RedirectUri, StringComparer.OrdinalIgnoreCase))
			{
				validateAuthorizationRequestContext.Reject(
					error: OpenIddictConstants.Errors.InvalidRequestUri,
					description: "The specified 'redirect_uri' is not valid for this client application.");
				return default;
			}
			return default;
		};
	
	internal static Func<OpenIddictServerEvents.ValidateTokenRequestContext, ValueTask> ValidateTokenRequestFunc(AppSettings.AuthCredentialsSettings authSettings) => 
		validateTokenRequestContext =>
		{
			if (!string.Equals(validateTokenRequestContext.ClientId, authSettings.ClientId, StringComparison.OrdinalIgnoreCase))
			{
				validateTokenRequestContext.Reject(
					error: OpenIddictConstants.Errors.InvalidClient,
					description: "The specified 'client_id' doesn't match a registered application.");
				return default;
			}
			// No client secret validation, as this project is used by a public client application
			return default;
		};

	internal static Func<OpenIddictServerEvents.HandleAuthorizationRequestContext, ValueTask> HandleAuthorizationRequest(AppSettings.AuthCredentialsSettings authSettings) => 
		async context =>
		{
			var request = context.Transaction.GetHttpRequest() ??
			              throw new InvalidOperationException("The ASP.NET Core request cannot be retrieved.");
			
			// Retrieve the user principal stored in the authentication cookie.
			var authResult = await request.HttpContext.AuthenticateAsync(OpenIdConnectDefaults.AuthenticationScheme);
			// If the principal cannot be retrieved, this indicates that the user is not logged in.
			if (authResult?.Succeeded != true || authResult?.Principal == null) 
			{
				// Auth challenge is triggered to redirect the user to the provider's authentication end-point 
				var properties = new AuthenticationProperties { Items = { ["LoginProvider"] = "Microsoft" } };
				await request.HttpContext.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, properties);
				context.HandleRequest();
				return;
			}

			var (email, name) = ResolveAzureAdClaims(authResult.Principal);
			
			// Form new claims
			var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType /* sets it to 'Federated Authentication' */);
			// Mark all the added claims as being allowed to be persisted in the access token,
			// so that the API controllers can retrieve them from the ClaimsPrincipal instance.
			identity.AddClaim(OpenIddictConstants.Claims.Subject, email, OpenIddictConstants.Destinations.AccessToken);
			identity.AddClaim(OpenIddictConstants.Claims.Email, email, OpenIddictConstants.Destinations.AccessToken);
			identity.AddClaim(OpenIddictConstants.Claims.Name, name, OpenIddictConstants.Destinations.AccessToken);

			// Attach the principal to the authorization context, so that an OpenID Connect response
			// with an authorization code can be generated by the OpenIddict server services.
			context.Principal = new ClaimsPrincipal(identity)
									// Re-attach supported scopes, so downstream handlers don't reject 'unsupported' scopes or not issue a 'refresh_token' on the grounds of absent 'offline_access' scope
									.SetScopes(authSettings.ScopesFullSet.Keys);
		};

	private static (string email, string name) ResolveAzureAdClaims(ClaimsPrincipal claims)
		=> (
			claims.GetClaim(OpenIddictConstants.Claims.PreferredUsername) ?? "",
			claims.GetClaim(OpenIddictConstants.Claims.Name) ?? ""
		);
}