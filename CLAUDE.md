# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Google Auth Proxy is an Azure Functions (.NET 8.0) app that acts as an OIDC/OAuth2 compatibility shim between Microsoft Entra ID (Azure AD) and Google's authorization servers. Entra ID sends a `username` parameter in auth requests that Google rejects — this proxy strips it and rewrites the OIDC discovery document so Entra points to the proxy instead of Google directly.

## Build & Run

```bash
# Build
dotnet build GoogleAuthProxy/GoogleAuthProxy.csproj

# Run locally (starts on http://localhost:7136)
dotnet run --project GoogleAuthProxy/GoogleAuthProxy.csproj

# Release build
dotnet build -c Release GoogleAuthProxy/GoogleAuthProxy.csproj
```

There are no automated tests in this project.

## Architecture

Two Azure HTTP-triggered functions in `GoogleAuthProxy/GoogleAuthProxy.cs`:

**`OidcDiscovery`** — Route: `well-known/openid-configuration`
- Fetches Google's real OIDC discovery doc from `https://accounts.google.com/.well-known/openid-configuration`
- Rewrites `authorization_endpoint` to point to this proxy's `/api/auth` endpoint
- Entra ID uses this to discover where to send auth requests

**`Auth`** — Route: `api/auth`
- Receives the auth request forwarded by Entra ID
- Strips the `username` parameter (incompatible with Google OAuth2)
- Redirects (HTTP 302) to `https://accounts.google.com/o/oauth2/v2/auth` with the cleaned query string

Request flow:
```
Entra ID → GET /.well-known/openid-configuration → proxy returns modified OIDC config
Entra ID → GET /api/auth?...&username=... → proxy strips username → 302 to Google
Google → auth response → back to Entra ID
```

Both endpoints use anonymous authorization level (no API key) and inject `IHttpClientFactory` and `ILogger<GoogleAuthProxy>` via the Azure Functions DI container configured in `Program.cs`.
