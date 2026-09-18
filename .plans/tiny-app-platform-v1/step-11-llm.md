# Step 11 â€” LLM text parsing

**Phase:** 1 â€” Core engine
**Depends on:** Step 10
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Port the generic typed-LLM-parsing helper. Despite living under `App/StockData/` in the
source, it contains no finance knowledge (analysis Â§1) and is one of the more useful things
a small app gets for free.

## Reference material (read-only)

| Source | Use |
|---|---|
| `App\StockData\UserInput\LlmTextParserBase.cs` (180 lines) | Port; generalise |
| `App\StockData\UserInput\ILlmTextParseClient.cs` | Port |
| `App\StockData\UserInput\LlmTextParseClient.cs` | Port the Azure OpenAI implementation |
| `App\Host\ServiceBuilder.cs` (the `AzureOpenAIClient` lambda) | Move here â€” it belongs with the LLM module, not with hosting |

**Do not port** `AbnAmroShareOwnershipParser.cs`, `ShareOwnershipItem.cs`,
`StockNameMatcher*.cs`, `StockMatchingService.cs`, `IStockNameMatcherClient.cs` â€” all domain.

Read `LlmTextParserBase.cs` in full before starting; the retry and JSON-repair logic is the
value here and should be preserved rather than reinvented.

## Tasks

All files go in `src/PS.AppPlatform/Llm/`, namespace `PS.AppPlatform.Llm`.

### 1. `ILlmTextParseClient.cs`

Port the interface. Make it `public`. If the source signature mentions any stock concept in
a parameter name, rename to neutral terms (`input`, `systemPrompt`, `userPrompt`).

### 2. `LlmTextParseClient.cs`

Port the Azure OpenAI implementation. It takes `AzureOpenAIClient` and the deployment name
from configuration.

Configuration shape â€” keep the source's `azureOpenAI` section:

```json
"azureOpenAI": {
  "endpoint": "https://<account>.openai.azure.com/",
  "deployment": "gpt-4o-mini",
  "authentication": { "type": "managedIdentity" }
}
```

### 3. `LlmTextParserBase.cs`

Port as `public abstract class LlmTextParserBase<T>`. Preserve:

- the typed round trip: prompt â†’ completion â†’ `JsonSerializer.Deserialize<T>`
- the retry loop with the parse error fed back into the next attempt
- the max-attempts cap from configuration

Changes:
- Make everything the subclass needs `protected` or `public`; the source is `internal`.
- Move the attempt count to `azureOpenAI:maxParseAttempts`, default `3`.
- Add a `CancellationToken` parameter to the public entry point if the source lacks one.
- Strip a fenced code block (` ```json â€¦ ``` `) from the completion before deserialising if
  the source does not already â€” it is the single most common cause of a retry.

### 4. `HtmlToXhtmlConverter`

The analysis lists this as reusable. Locate it in the source (`App/StockData/`), and:
- if it has no domain knowledge, port it to `Llm/HtmlToXhtmlConverter.cs` as `public`
- if it turns out to be stock-specific, **skip it** and note that in the commit message

Do not invent it if it does not exist.

### 5. `LlmServiceBuilder.cs`

Move the `AzureOpenAIClient` registration out of `App\Host\ServiceBuilder.cs` and into this
module. Port the lambda as-is: it switches on `azureOpenAI:authentication:type`, using an
`AzureKeyCredential` for `apiKey` and the `IAzureIdentityProvider` credential otherwise.

**Two changes:**

- **Make the whole module conditional.** The source throws
  `"Missing 'azureOpenAI' configuration section."` from the resolver. Most tiny apps will
  not use an LLM at all, so registering nothing when the section is absent is correct:

  ```csharp
  if (!configuration.GetSection("azureOpenAI").Exists()) return;
  ```

  Keep the throw inside the resolver for the case where the section exists but is malformed.

- **Log a warning when `type` is `apiKey`.** A live Azure Cognitive Services key is committed
  at `C:\Dev\StockAnalysis\App\appsettings.json:25` and is in that repo's git history
  (analysis Â§6). The platform should nudge away from that path:
  `"azureOpenAI is using apiKey authentication. Prefer managed identity; if a key is required, store it in Function App settings or a Key Vault reference, never in appsettings.json."`

## Tests to add

`tests/PS.AppPlatform.Tests/LlmTests.cs`, with a fake `ILlmTextParseClient` â€” **no
network calls**:

1. A well-formed JSON completion deserialises to `T` on the first attempt, and the client is
   called once.
2. A malformed completion followed by a well-formed one succeeds on attempt 2, and the
   second prompt contains the parse error text.
3. Three malformed completions throw (or return the failure result the source defines) after
   exactly `maxParseAttempts` calls.
4. A completion wrapped in a ` ```json ` fence parses without a retry.
5. `LlmServiceBuilder` registers nothing when `azureOpenAI` is absent, and the service
   collection contains no `AzureOpenAIClient` descriptor.

## Verification

```powershell
cd C:\Dev\AppPlatform
dotnet build --configuration Release
dotnet test
```

**Expected:** zero warnings, zero errors, all tests pass. No test may make a network call â€”
if one hangs, that is the bug.

## Done when

- [ ] Build clean, all tests pass
- [ ] Only the four domain-free files were ported; no stock types came along
- [ ] An app with no `azureOpenAI` section starts fine and registers no LLM services
- [ ] `apiKey` authentication logs a warning pointing at Key Vault / app settings
- [ ] The `AzureOpenAIClient` registration no longer lives in the hosting module

## Commit

```powershell
git add -A
git commit -m "Step 11: generic LLM text parsing with retry; Azure OpenAI client registration made optional"
```

