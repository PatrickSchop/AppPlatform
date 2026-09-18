# PS.AppPlatform.Functions

Platform endpoint shims for Azure Functions. This package **does not contain compiled assemblies** â€” instead, it injects endpoint source code (`.cs` files) into your app's compilation via an auto-imported `.targets` file.

## Why source injection?

Azure Functions worker indexing is source-generator based and only sees the current compilation. A `[Function]` attribute in a referenced assembly is invisible to the indexer. Source injection keeps the shims in your app's compilation where the worker generator can find them.

## What's included

- `GetNotificationTasks`, `GetTasks`, `GetTaskStatus`, `CreateTask`, `CheckTasks` â€” background task endpoints
- `StaticContent` â€” SPA hosting with deep-link fallback and caching
- `GetWebAppConfiguration` â€” public app config endpoint
- `GetHealth` â€” health check
- `InitializeDatabase` â€” one-time migration trigger
- `ScheduledTaskCheck` â€” timer-triggered task executor

All endpoints are wired to services from `PS.AppPlatform` â€” nothing special to configure beyond adding the platform modules to your host.

## Disabling shim injection

If you need to customize an endpoint, add this to your app's `.csproj` **before** any package reference to this package:

```xml
<PropertyGroup>
  <PSAppPlatformInjectEndpoints>false</PSAppPlatformInjectEndpoints>
</PropertyGroup>
```

Then copy the endpoint you want to modify from this package's `endpoints/` folder into your app and implement it however you like.

## ProjectReference warning

This package **requires PackageReference consumption**. If you use `ProjectReference` instead, the auto-import targets file doesn't run, and the shims won't compile. Only `PackageReference` triggers the targets-based injection.

(SampleApp in the repo uses `ProjectReference` + explicit `<Compile Include>` because it's a proof-of-concept, not a real consumer.)

## Docs

See [the repository](https://github.com/PatrickSchop/AppPlatform) for detailed architecture.

