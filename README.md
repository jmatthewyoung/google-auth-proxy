# Google Auth Proxy
### by John Matthew Young

A fix for the `Error 400: invalid_request — Parameter not allowed for this message type: username` error that occurs when using Google as an identity provider in Microsoft Entra External ID (formerly Azure AD B2C).

---

## The Problem

When you configure Google as a federated identity provider in Microsoft Entra External ID and a user tries to sign in through a SignInSignUp user flow, the login fails with:

> **Access blocked: Authorization Error**
> Error 400: invalid_request
> Parameter not allowed for this message type: username

This happens because Entra automatically appends a `username` parameter to the OAuth2 authorization request it sends to Google. Google's OAuth2 implementation does not accept this parameter and rejects the request outright.

Direct "Sign in with Google" flows work fine — the issue is specific to Entra-orchestrated flows.

---

## The Fix

Both solutions below work by placing a lightweight proxy between Entra and Google. The proxy intercepts the authorization request, strips the `username` parameter, and forwards it cleanly to Google.

The key change in both cases is pointing Entra's **Well-known endpoint** at the proxy instead of Google directly. Normally you would set this to Google's own discovery URL (`https://accounts.google.com/.well-known/openid-configuration`) — the proxy replaces that entry point while everything else stays the same.

---

## Option 1 — Easy: Use the Hosted Proxy (Development/Less Secure Applications)

A hosted instance of this proxy is already running. You just need to update your Entra Google identity provider configuration to point to it.

### Steps

1. In the [Azure Portal](https://portal.azure.com), go to **Entra External ID → External Identities → All identity providers**.
2. Find your **Google** identity provider and open it (or add one if you haven't yet).
3. Fill in the settings as follows:

| Field | Value |
|---|---|
| Well-known endpoint | `https://google-auth-proxy.jmatthewyoung.com/api/well-known/openid-configuration` |
| Issuer URI | `https://accounts.google.com` |
| Client ID | *(your Client ID from Google Cloud Console)* |
| Client Authentication Method | Client secret |
| Client Secret | *(your Client Secret from Google Cloud Console)* |
| Scope | `openid profile email` |
| Response Type | `code` |

> The only field that differs from a standard Google OIDC setup is the **Well-known endpoint** — instead of pointing directly at `https://accounts.google.com/.well-known/openid-configuration`, you point it at the proxy. Everything else is identical to what Google requires.

4. Save the configuration and test your sign-in flow.

> No code changes, no infrastructure to manage.

---

## Option 2 — Self-Hosted: Deploy Your Own Proxy (Production/Best Practice)

Use this repository to deploy your own instance of the proxy to Azure Functions. This gives you full control over the infrastructure.

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download)
- [Azure Functions Core Tools](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local)
- An Azure subscription with permissions to create a Function App

### Run Locally

```bash
git clone https://github.com/your-username/google-auth-proxy.git
cd google-auth-proxy
dotnet run --project GoogleAuthProxy/GoogleAuthProxy.csproj
```

The proxy will start on `http://localhost:7136`. You can verify it is working by visiting:

```
http://localhost:7136/api/well-known/openid-configuration
```

You should see Google's OIDC discovery document with the `authorization_endpoint` rewritten to point to the local proxy.

### Deploy to Azure

1. Create a Function App in Azure (Runtime: .NET 8, OS: Windows or Linux).
2. Deploy using the Azure Functions Core Tools:
   ```bash
   func azure functionapp publish <YOUR_FUNCTION_APP_NAME>
   ```
   Or publish directly from Visual Studio via **Right-click project → Publish**.
3. Note your deployed URL — but read the next section before using it.

### Important: You Must Use a Custom Domain

Do **not** use the default `*.azurewebsites.net` URL with Entra. When Google redirects the user back through the proxy after authentication, Chrome will display a "Dangerous site" warning for `azurewebsites.net` domains. Users will see a scary security interstitial before completing login.

Set up a custom domain on your Function App (e.g. `auth-proxy.yourdomain.com`) and use that as your proxy URL. Azure's documentation covers this under [App Service custom domains](https://learn.microsoft.com/en-us/azure/app-service/app-service-web-tutorial-custom-domain).

### Configure Entra to Use Your Proxy

Follow the same settings as Option 1, substituting your custom domain:

| Field | Value |
|---|---|
| Well-known endpoint | `https://auth-proxy.yourdomain.com/api/well-known/openid-configuration` |
| Issuer URI | `https://accounts.google.com` |
| Client ID | *(your Client ID from Google Cloud Console)* |
| Client Authentication Method | Client secret |
| Client Secret | *(your Client Secret from Google Cloud Console)* |
| Scope | `openid profile email` |
| Response Type | `code` |

### How It Works

The proxy exposes two endpoints that mirror Google's OIDC interface:

| Endpoint | What it does |
|---|---|
| `GET /api/well-known/openid-configuration` | Returns Google's OIDC discovery doc with `authorization_endpoint` rewritten to point to the proxy |
| `GET /api/auth` | Receives the auth request from Entra, strips the `username` parameter, and redirects to Google |

Entra reads the discovery doc, sees the proxy's `/api/auth` as the authorization endpoint, sends the request there, and the proxy silently removes `username` before forwarding to Google.
