# Step 09 — Static content and SPA hosting

**Phase:** 1 — Core engine
**Depends on:** Step 08
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Port the static file providers and rebuild the SPA fallback handler, fixing the deep-link
bug that makes React Router (and Angular routing on hard refresh) unusable today.

## The problems being solved (analysis §4)

`C:\Dev\StockAnalysis\App\StaticContent\Static.cs:28-41` falls back to `index.html` only for
the **empty** path. Any deep link — `/dashboard`, `/management` — returns 404 on a hard
refresh. Also missing: `.ico`, `.woff2`, `.map` content types, and any `ETag`/`Cache-Control`.

## Tasks

### 1. `StaticContent/IFilesProvider.cs`

Extend the source interface — the caller now needs metadata for `ETag` and content length,
not just a stream.

```csharp
namespace Wisdi.AppPlatform.StaticContent;

public interface IFilesProvider
{
    /// <summary>Returns null when the file does not exist. Never throws for a missing file.</summary>
    Task<StaticFile?> GetFileAsync(string relativePath, CancellationToken ct = default);
}

public sealed record StaticFile(Stream Content, string? ETag, DateTimeOffset? LastModified, long? Length);
```

**Change from the source:** returning `null` replaces throwing `FileNotFoundException`.
The handler needs to probe for a file and fall back to `index.html`, and exceptions are the
wrong control flow for something that happens on every deep link.

### 2. `StaticContent/LocalFilesProvider.cs`

Port from the source. Keep the path-traversal guard — it is the security-relevant line:

```csharp
string fullPath = Path.GetFullPath(Path.Combine(_rootPath, relativePath.TrimStart('/', '\\')));
if (!fullPath.StartsWith(_rootPath, StringComparison.Ordinal)) return null;
```

**Harden it slightly:** ensure `_rootPath` ends with a directory separator before the
`StartsWith` check, otherwise a sibling directory named `wwwroot-evil` passes a
`wwwroot` prefix test.

Populate `StaticFile` from `FileInfo`: `ETag` as a quoted `"{LastWriteTimeUtc.Ticks:x}-{Length:x}"`,
`LastModified`, `Length`.

Make the class `public` (the source has it `internal`).

### 3. `StaticContent/BlobProvider.cs`

Port from the source. It builds a `BlobContainerClient` from the `staticContent:blob:uri`
config value and the `IAzureIdentityProvider` credential.

Changes:
- Delete the commented-out connection-string block. Dead code does not get ported.
- Replace `ExistsAsync` + `DownloadStreamingAsync` (two round trips) with a single
  `DownloadStreamingAsync` in a `try`/`catch (RequestFailedException ex) when (ex.Status == 404)`
  returning `null`. Halving the round trips matters for a cold-start-sensitive SPA.
- Fill `ETag` from `response.Value.Details.ETag.ToString()`, `LastModified` and
  `Length` from `Details`.
- Make the class `public`.

### 4. `StaticContent/StaticContentHandler.cs` — the rewrite

This is **not** a `[Function]` class. It is a plain injectable service; the shim comes in
Step 13.

```csharp
public class StaticContentHandler
{
    public StaticContentHandler(IFilesProvider filesProvider, IConfiguration configuration, ILogger<StaticContentHandler> logger);

    public Task<IActionResult> HandleAsync(HttpRequest request, string path, CancellationToken ct = default);
}
```

Algorithm:

1. Normalise: trim leading `/`; if empty, use `index.html`.
2. Reject any path containing `..` with `BadRequestResult` before touching the provider.
3. Try `GetFileAsync(path)`.
4. **If not found, apply the SPA fallback** — the §4 fix:
   ```csharp
   if (file is null && !Path.HasExtension(path))
       file = await _filesProvider.GetFileAsync("index.html", ct);
   ```
   The "no extension" rule is what makes `/dashboard` serve the app while
   `/assets/missing.png` correctly 404s.
5. Still null → `NotFoundResult`.
6. If the request carries `If-None-Match` matching the file's `ETag`, dispose the stream and
   return `StatusCode(304)`.
7. Set headers, then return `new FileStreamResult(file.Content, contentType)`.

Header policy — this is the caching fix:

| Resource | `Cache-Control` |
|---|---|
| `index.html`, or any fallback-served path | `no-cache, must-revalidate` |
| Anything else | `public, max-age=31536000, immutable` |

Fingerprinted bundles are immutable; `index.html` must never be, or a deploy is invisible
until the browser cache expires. Make this overridable via
`staticContent:cacheControl:immutableMaxAge`.

Always set `ETag` when the provider supplied one.

### 5. `StaticContent/ContentTypes.cs`

Replace the source's chained ternary with a static dictionary, and add the missing types
(analysis §4):

```
.html text/html                      .css  text/css
.js   text/javascript                .mjs  text/javascript
.json application/json               .map  application/json
.png  image/png                      .jpg/.jpeg image/jpeg
.gif  image/gif                      .svg  image/svg+xml
.ico  image/x-icon                   .webp image/webp
.woff font/woff                      .woff2 font/woff2
.ttf  font/ttf                       .txt  text/plain
.wasm application/wasm               .xml  application/xml
```

Default `application/octet-stream`. Look up case-insensitively on `Path.GetExtension`.
Append `; charset=utf-8` to the `text/*`, `application/json` and `image/svg+xml` entries.

Note `.js` moves from the source's `application/javascript` to `text/javascript`, which is
the current IANA registration.

### 6. `StaticContent/StaticContentServiceBuilder.cs`

Port `App\StaticContent\ServiceBuilder.cs`. Keep the resolver-lambda pattern and the comment
explaining it — provider selection is deferred to first use because throwing during Functions
host startup produces an unreadable failure.

```csharp
services.AddTransient<LocalFilesProvider>();
services.AddTransient<BlobProvider>();
services.AddSingleton<IFilesProvider>(sp => /* files -> Local, blob -> Blob, else throw */);
services.AddScoped<StaticContentHandler>();
```

Fix the misleading error message: the source says `Expected either 'files' or 'blobs' section`
but the key it checks is `blob`.

## Tests to add

`tests/Wisdi.AppPlatform.Tests/StaticContentTests.cs`, with a fake `IFilesProvider` backed by
a dictionary:

1. `""` serves `index.html`.
2. **`"dashboard"` serves `index.html`** — the §4 deep-link fix. Name it
   `Extensionless_path_falls_back_to_index_html`.
3. `"app/settings/advanced"` also serves `index.html`.
4. `"assets/missing.png"` returns 404 — extensions do **not** fall back.
5. `"../secrets.txt"` returns 400.
6. `LocalFilesProvider` rejects a path escaping the root, including the
   `root` / `root-evil` sibling case.
7. `index.html` gets `no-cache`; `main.a1b2c3.js` gets `max-age=31536000`.
8. A matching `If-None-Match` yields 304 with no body.
9. `ContentTypes.For("x.woff2")` is `font/woff2`; `.ico`, `.map`, `.wasm` all resolve;
   an unknown extension is `application/octet-stream`.

## Verification

```powershell
cd C:\Dev\AppPlatform
dotnet build --configuration Release
dotnet test
```

**Expected:** zero warnings, zero errors, all tests pass.

## Done when

- [ ] Build clean, all tests pass
- [ ] `Extensionless_path_falls_back_to_index_html` passes — §4 is fixed and guarded
- [ ] `.ico`, `.woff2`, `.map`, `.wasm`, `.webp` all resolve
- [ ] `ETag`/`If-None-Match` handling works; `index.html` is `no-cache`
- [ ] `StaticContentHandler` carries **no** `[Function]` attribute
- [ ] Providers return `null` for missing files rather than throwing
- [ ] The commented-out connection-string block was not ported

## Commit

```powershell
git add -A
git commit -m "Step 09: static content with SPA deep-link fallback, ETag caching and full content types (fixes section 4)"
```
