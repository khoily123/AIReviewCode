using AIReviewerAPI.DTOs;
using Models;
using Repositories;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Services
{
    public class AIReviewService : IAIReviewService
    {
        // ✅ Tái sử dụng options
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IAIReviewRepository _repository;

        public AIReviewService(IAIReviewRepository repository)
        {
            _repository = repository;
        }

        public async Task<ReviewResponseDto> ReviewCode(ReviewRequestDto request)
        {
            try
            {
                string personaInstruction = request.Persona switch
                {
                    "GordonRamsay" => "You are Gordon Ramsay, but a senior C# code reviewer. You are extremely harsh, swear a bit (in a safe, funny way), criticize bad code ruthlessly, but your technical advice is excellent. You MUST write in Vietnamese.",
                    "AnimeWaifu" => "You are a cute, supportive Anime Waifu C# code reviewer. You use cute emoticons like (´• ω •`) and call the user 'Senpai'. You are very encouraging but still point out bugs accurately. You MUST write in Vietnamese.",
                    _ => "You are a senior C# code reviewer. You are professional and helpful. You MUST write in Vietnamese."
                };

                var customRulesSection = "";
                if (request.CustomRules != null && request.CustomRules.Count > 0)
                {
                    customRulesSection = "\n\nCUSTOM RULES FROM USER (you MUST check these and report violations as bugs):\n" +
                        string.Join("\n", request.CustomRules.Select((r, i) => $"{i + 1}. {r}"));
                }

                // Detect input type
                var codeToAnalyze = request.Code ?? "";
                var truncatedNote = "";

                bool isDiff = codeToAnalyze.Contains("// === ") && (codeToAnalyze.Contains("[modified]") || codeToAnalyze.Contains("[added]") || codeToAnalyze.Contains("@@"));
                bool isMultiFile = codeToAnalyze.Contains("// === FILE:") || (codeToAnalyze.Contains("// ===") && codeToAnalyze.IndexOf("// ===") != codeToAnalyze.LastIndexOf("// ==="));
                bool isGitHubPr = codeToAnalyze.TrimStart().StartsWith("// PR:") || codeToAnalyze.TrimStart().StartsWith("// Source: GitHub") || codeToAnalyze.TrimStart().StartsWith("// Compare:") || codeToAnalyze.TrimStart().StartsWith("// Branch:");

                // For large single files: strip blank lines + line comments to shrink before truncate
                int maxCodeChars = (isMultiFile || isDiff || isGitHubPr) ? 24_000 : 35_000;
                if (!isDiff && !isGitHubPr && codeToAnalyze.Length > maxCodeChars)
                    codeToAnalyze = CompressCode(codeToAnalyze);

                if (codeToAnalyze.Length > maxCodeChars)
                {
                    codeToAnalyze = codeToAnalyze[..maxCodeChars];
                    truncatedNote = $"\n[NOTE: Input was truncated to {maxCodeChars} chars — review the visible portion only.]\n";
                }

                var securitySection = codeToAnalyze.Length > 8000
                    ? "SECURITY (report each as bug): SQL/NoSQL/Command injection, hardcoded credentials/keys, weak hashing (MD5/SHA1/plain), insecure JWT, missing input validation, path traversal, XSS, open redirect, sensitive data in logs, CORS *, weak crypto (DES/RC4), Math.random() for tokens, XXE, ReDoS, mass assignment. CRITICAL issues -> securityScore <= 30, HIGH -> <= 55, MEDIUM -> <= 75.\nPERFORMANCE (report each): N+1 queries, missing async/await, string concat in loops, unbounded collections, missing cache, sync HTTP calls, no CancellationToken.\nMAINTAINABILITY: God class/long method, magic numbers, dead code, silent catch, missing IDisposable."
                    : BuildDetailedChecklist();

                // Context-specific instructions
                var inputContextNote = (isDiff || isGitHubPr)
                    ? @"INPUT FORMAT: This is a GIT DIFF / PATCH (lines starting with '+' are additions, '-' are deletions, ' ' are unchanged context).
IMPORTANT: Review ONLY the changed lines (+ lines). Do NOT report issues from unchanged context lines.
For 'fixedCode': return ONLY the corrected version of the changed lines, clearly indicating which file each block belongs to. If no meaningful fix can be generated, return empty string.
For line numbers in bugs: use the line number in the new file (count only + and space lines, not - lines)."
                    : isMultiFile
                    ? @"INPUT FORMAT: Multiple files concatenated (each starts with '// === FILE: filename ===').
IMPORTANT: Analyze ALL files together as a project. For each bug, prefix the description with the filename like '[filename.cs] description'.
For 'fixedCode': return only the most critical file's fixed version, or empty string if impractical to fix all files."
                    : "INPUT FORMAT: Single code file. Analyze normally.";

                var prompt = $@"
{personaInstruction}

{inputContextNote}

Analyze the following code (auto-detect the programming language). Find ALL bugs, security vulnerabilities, and performance issues. Rate 0–100 for Performance, Security, and Maintainability.

{securitySection}

━━━ SECURITY CHECKLIST (MANDATORY — report every violation as a bug) ━━━
INJECTION ATTACKS:
  • SQL Injection — string concatenation/interpolation in SQL (e.g. ""WHERE id='"" + input + ""'"") → CRITICAL, score ≤ 30
  • NoSQL Injection — unvalidated input passed to MongoDB/Redis queries
  • Command Injection — user input in Process.Start, shell exec, eval
  • LDAP/XPath/Template Injection — unsanitized input in LDAP filters or template engines
  • SSTI (Server-Side Template Injection) — user input rendered as template

AUTHENTICATION & SESSION:
  • Hardcoded credentials — passwords, API keys, tokens, connection strings in source code → CRITICAL
  • Weak/no password hashing — plain text, MD5, SHA1, SHA256 (without salt+iterations) for passwords
  • Insecure JWT — algorithm ""none"", weak secret, missing expiry, no signature validation
  • Broken session — predictable session IDs, missing HttpOnly/Secure flags on cookies
  • Missing authentication/authorization checks on sensitive operations

DATA EXPOSURE:
  • Sensitive data in logs — logging passwords, PII, credit cards, tokens
  • Stack traces / internal errors returned to clients
  • Overly broad CORS (Access-Control-Allow-Origin: *)
  • Sensitive fields in API responses (password hash, internal IDs)
  • Unencrypted sensitive data at rest or in transit (HTTP instead of HTTPS)

INPUT VALIDATION:
  • Missing null/empty checks on user input
  • Path traversal — user-controlled file paths without sanitization
  • XSS — unsanitized output in HTML, missing Content-Security-Policy hints
  • Open redirect — unvalidated URL redirect parameters
  • Integer overflow / type confusion on user-supplied numbers
  • File upload — missing extension whitelist, missing size limit, missing virus scan hint

CRYPTOGRAPHY:
  • Weak algorithms: DES, 3DES, RC4, MD5/SHA1 for security purposes
  • Hardcoded IV/salt/key
  • Insufficient randomness: Math.random() / new Random() for security tokens
  • Missing TLS certificate validation

RESOURCE & LOGIC:
  • Race conditions / TOCTOU (check-then-act on shared state)
  • Insecure deserialization (BinaryFormatter, unsafe JSON type handling)
  • XXE (XML External Entity) — XmlDocument without DisableExternalEntities
  • Regex DoS (ReDoS) — catastrophic backtracking patterns
  • Mass assignment — binding all request fields to model without whitelist

SCORING RULES:
  • Any CRITICAL issue (SQL injection, hardcoded secret, plain-text password) → securityScore ≤ 30
  • Any HIGH issue (XSS, command injection, broken auth) → securityScore ≤ 55
  • Any MEDIUM issue (missing validation, weak crypto) → securityScore ≤ 75
  • Zero vulnerabilities → securityScore may be 80–100

━━━ PERFORMANCE CHECKLIST (MANDATORY) ━━━
  • N+1 queries — loop containing a DB call instead of a JOIN/batch
  • Missing async/await — blocking I/O calls on synchronous thread
  • String concatenation in loops — use StringBuilder
  • Unnecessary object allocations in hot paths (closures, LINQ inside loops)
  • Missing database indexes hint — queries filtering on non-indexed columns
  • Unbounded collections — loading entire table into memory
  • Missing caching for expensive or repeated computations
  • Synchronous HTTP calls blocking thread pool
  • Missing cancellation token support in async methods
  • Excessive try/catch inside loops impacting performance

━━━ MAINTAINABILITY CHECKLIST ━━━
  • God class / method too long (>50 lines)
  • Magic numbers/strings not extracted to constants
  • Duplicated code blocks
  • Dead code / unreachable branches
  • Missing null checks leading to NullReferenceException
  • catch(Exception) swallowing errors silently
  • Mutable public fields / missing encapsulation
  • Missing IDisposable on classes owning unmanaged resources

Return ONLY valid JSON in this exact format:

{{
  ""message"": ""summary of the review"",
  ""fixedCode"": ""the complete fixed code snippet"",
  ""performanceScore"": 0,
  ""securityScore"": 0,
  ""maintainabilityScore"": 0,
  ""mermaidChart"": ""graph TD\n A-->B"",
  ""hackerExploit"": ""exploit code or explanation (if any security bug is found, else empty)"",
  ""unitTests"": ""// complete xUnit test class here"",
  ""bugs"": [
    {{
      ""line"": 0,
      ""description"": ""bug description""
    }}
  ]
}}

Rules:
- Do not include markdown
- Do not include explanations outside JSON
- Always return JSON
- LANGUAGE: You MUST write ALL text fields (message, description, hackerExploit, notes) in Vietnamese (Tiếng Việt) ONLY. Do NOT mix in Chinese, Japanese, Korean, or any other language. Variable names and code stay in their original language, but all explanatory text must be pure Vietnamese.
- CRITICAL: The 'fixedCode' MUST resolve all issues completely and be production-ready. You MUST preserve proper indentation and newlines (\n) within the 'fixedCode' string. Do not minify or one-line the code.
- CRITICAL: If the provided code is already perfect and has no bugs, DO NOT invent bugs. Return an empty array [] for 'bugs' and score 100 for all metrics.
- CRITICAL: 'mermaidChart': {((isDiff || isGitHubPr || isMultiFile) ? "Return empty string \"\" — flowchart is not applicable for diffs or multi-file input." : "MUST be a DETAILED, PROFESSIONAL flowchart using Mermaid.js 'flowchart TD' syntax. STRICT RULES:")}
  * First line MUST be exactly: flowchart TD
  * Node IDs: use only alphanumeric and underscore, no spaces (e.g. A1, start_node, getUserInfo)
  * Node labels with special chars MUST be wrapped in double quotes: A1[""GetUserInfo(userId)""] NOT A1[GetUserInfo(userId)]
  * Decision diamond nodes: A1{{""Is valid?""}} — always use double curly braces and quote the label
  * Terminal nodes: A1([""Start""]) and Z1([""End""])
  * Edge labels with spaces: A1 -->|""has error""| B1 — always quote multi-word labels
  * classDef MUST use this exact format on separate lines:
    classDef successStyle fill:#d1fae5,stroke:#10b981,color:#064e3b
    classDef errorStyle fill:#fee2e2,stroke:#ef4444,color:#7f1d1d
    classDef decisionStyle fill:#dbeafe,stroke:#3b82f6,color:#1e3a5f
    classDef processStyle fill:#f3f4f6,stroke:#6b7280,color:#1f2937
    classDef ioStyle fill:#fef3c7,stroke:#f59e0b,color:#78350f
  * class assignment: class nodeId successStyle
  * Every if/else/try/catch/loop MUST be shown with labeled edges
  * subgraph names must be simple words: subgraph UserService
  * Minimum 8 nodes for any non-trivial code
  * Do NOT use backticks anywhere in the value
  * Escape newlines as \n in the JSON string
- CRITICAL: If a security vulnerability is found (like SQL Injection), provide a specific attack payload example in 'hackerExploit'. If no security vulnerability exists, return an empty string """".
- CRITICAL: 'unitTests' MUST contain a complete, compilable xUnit test class (using Xunit; namespace) that covers all public methods. Include proper using statements. Use \n for newlines inside the JSON string value.

Code to analyze:
{customRulesSection}

{codeToAnalyze}{truncatedNote}
";

                var aiResult = await _repository.CallAI(prompt);

                var modelResult = JsonSerializer.Deserialize<ReviewResponse>(aiResult, _jsonOptions)
                    ?? new ReviewResponse();

                var dtoResult = new ReviewResponseDto
                {
                    Summary = modelResult.Message,
                    FixedCode = modelResult.FixedCode,
                    PerformanceScore = modelResult.PerformanceScore,
                    SecurityScore = modelResult.SecurityScore,
                    MaintainabilityScore = modelResult.MaintainabilityScore,
                    MermaidChart = SanitizeMermaid(modelResult.MermaidChart),
                    HackerExploit = modelResult.HackerExploit,
                    UnitTests = modelResult.UnitTests,
                    ReviewedAt = DateTime.UtcNow,
                    DetectedBugs = modelResult.Bugs?.Select(b => new BugDto
                    {
                        Line = b.Line,
                        Description = b.Description,
                        Severity = "High"
                    }).ToList() ?? []
                };

                return dtoResult;
            }
            catch (Exception ex)
            {
                string errorMessage = ex.Message;
                if (errorMessage.Contains("TooManyRequests") || errorMessage.Contains("429"))
                    errorMessage = "Hệ thống AI đang quá tải. Vui lòng chờ khoảng 1 phút rồi thử lại!";
                else if (errorMessage.Contains("too large") || errorMessage.Contains("RequestEntityTooLarge") || errorMessage.Contains("tokens"))
                    errorMessage = "Code quá dài, AI không thể xử lý. Hãy thử paste một đoạn code ngắn hơn.";
                else
                    errorMessage = $"Lỗi từ AI (có thể do nhập code không hợp lệ): {ex.Message}";

                return new ReviewResponseDto
                {
                    ErrorMessage = errorMessage,
                    ReviewedAt = DateTime.UtcNow,
                    DetectedBugs = null
                };
            }
        }

        public async Task<ChatResponseDto> ChatWithCode(ChatRequestDto request)
        {
            try
            {
                var prompt = BuildChatPrompt(request);
                var aiResult = await _repository.CallAIText(prompt);
                return new ChatResponseDto { Reply = aiResult };
            }
            catch (Exception ex)
            {
                string errorMessage = ex.Message;
                if (errorMessage.Contains("TooManyRequests") || errorMessage.Contains("429"))
                    errorMessage = "Hệ thống AI đang bận. Vui lòng đợi 1 phút và hỏi lại nhé!";
                else
                    errorMessage = $"Lỗi từ AI: {ex.Message}";

                return new ChatResponseDto { Reply = errorMessage };
            }
        }

        public async IAsyncEnumerable<string> ChatWithCodeStream(ChatRequestDto request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            var prompt = BuildChatPrompt(request);

            bool streamYieldedSomething = false;
            await foreach (var chunk in _repository.CallAIStream(prompt, ct))
            {
                streamYieldedSomething = true;
                yield return chunk;
            }

            // If Gemini stream returned nothing (rate-limited / error), fall back to blocking call
            if (!streamYieldedSomething)
            {
                string fallback;
                try { fallback = await _repository.CallAIText(prompt); }
                catch (Exception ex) { fallback = $"Lỗi từ AI: {ex.Message}"; }
                yield return fallback;
            }
        }

        public async Task<TranslateResponseDto> TranslateCode(TranslateRequestDto request)
        {
            try
            {
                var prompt = $@"
You are an expert multilingual programmer. Translate the following code to {request.TargetLanguage}.

Return ONLY valid JSON in this exact format:
{{
  ""translatedCode"": ""the complete translated code"",
  ""sourceLanguage"": ""auto-detected source language name"",
  ""notes"": ""brief explanation of key translation decisions""
}}

Rules:
- Preserve exact logic and structure
- Use idiomatic {request.TargetLanguage} patterns, naming conventions, and type system
- Include all required imports / package declarations at the top
- Adapt C#-specific patterns (properties, LINQ, delegates) to {request.TargetLanguage} equivalents
- You MUST write the 'notes' field in Vietnamese (Tiếng Việt)
- CRITICAL: Preserve proper indentation and newlines (\n) inside the JSON string values
- Do not include markdown, do not include anything outside the JSON

Code to translate:
{request.Code}
";
                var aiResult = await _repository.CallAI(prompt);
                var model = JsonSerializer.Deserialize<TranslateResponse>(aiResult, _jsonOptions) ?? new TranslateResponse();

                return new TranslateResponseDto
                {
                    TranslatedCode = model.TranslatedCode,
                    SourceLanguage = model.SourceLanguage,
                    Notes = model.Notes
                };
            }
            catch (Exception ex)
            {
                var errorMessage = ex.Message.Contains("TooManyRequests") || ex.Message.Contains("429")
                    ? "Hệ thống AI đang quá tải. Vui lòng thử lại sau 1 phút!"
                    : $"Lỗi khi dịch code: {ex.Message}";

                return new TranslateResponseDto { ErrorMessage = errorMessage };
            }
        }

        private static string BuildDetailedChecklist() => @"SECURITY CHECKLIST (MANDATORY — report every violation as a bug):
INJECTION: SQL injection (string concat in query -> CRITICAL score<=30), NoSQL injection, Command injection (Process.Start/eval), LDAP/XPath injection, SSTI.
AUTH: Hardcoded credentials/API keys/tokens (CRITICAL), plain-text/MD5/SHA1 passwords, JWT alg=none/weak secret, predictable session IDs, missing auth checks.
DATA EXPOSURE: Passwords in logs, stack traces to client, CORS *, sensitive fields in responses, HTTP instead of HTTPS.
INPUT: Missing null checks, path traversal, XSS, open redirect, file upload without extension whitelist.
CRYPTO: DES/3DES/RC4, hardcoded IV/key, Math.random() for tokens, no TLS validation.
LOGIC: Race conditions, BinaryFormatter deserialization, XXE (XmlDocument without DisableExternalEntities), ReDoS, mass assignment.
SCORING: CRITICAL->score<=30, HIGH-><=55, MEDIUM-><=75, Clean->80-100.

PERFORMANCE CHECKLIST (MANDATORY):
N+1 queries (loop+DB call), missing async/await on I/O, string concat in loops (use StringBuilder),
unbounded collections in memory, missing cache, sync HTTP calls, no CancellationToken, try/catch in loops.

MAINTAINABILITY: God class/long method(>50 lines), magic numbers, dead code, silent catch(Exception), missing IDisposable.";

        private static string SanitizeMermaid(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw ?? "";

            // Strip markdown fences
            raw = raw.Replace("```mermaid", "").Replace("```", "").Trim();

            // Fix invalid edge syntax: -->|"label"|> Node  →  -->|"label"| Node
            raw = raw.Replace("\"|>", "\"|");
            // Also fix unquoted edge labels: --|label|> → --|label|
            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"\|([^|""]+)\|>", "|$1|");

            // Fix spaces around edge label quotes: -->| "label" | → -->|"label"|
            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"\|\s+""([^""]*)""\s+\|", "|\"$1\"|");
            // Fix spaces around unquoted edge labels: -->| label | → -->|label|
            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"\|\s+([^|""\n]+?)\s+\|", "|$1|");

            // Ensure proper header
            if (!raw.StartsWith("flowchart") && !raw.StartsWith("graph"))
                raw = "flowchart TD\n" + raw;

            // Normalise hexagon {{text}} → diamond {"text"} — more stable in Mermaid 10.4
            raw = System.Text.RegularExpressions.Regex.Replace(
                raw,
                @"\{\{([^}]*)\}\}",
                m =>
                {
                    var inner = m.Groups[1].Value.Trim();
                    var quoted = inner.StartsWith("\"") && inner.EndsWith("\"")
                        ? inner : "\"" + inner + "\"";
                    return "{" + quoted + "}";
                });

            // Fix unquoted single-brace decisions {label} → {"label"}
            // Matches {something} but NOT {{something}} (already handled above)
            raw = System.Text.RegularExpressions.Regex.Replace(
                raw,
                @"\{(?!\{)([^}""]+)\}(?!\})",
                m =>
                {
                    var inner = m.Groups[1].Value.Trim();
                    return "{\"" + inner + "\"}";
                });

            // Fix unquoted node labels with special chars: [label(x)] → ["label(x)"]
            raw = System.Text.RegularExpressions.Regex.Replace(
                raw,
                @"\[(?!"")([^\]]*[(),:][^\]]*)\]",
                m =>
                {
                    var inner = m.Groups[1].Value.Trim();
                    return "[\"" + inner + "\"]";
                });

            // Fix bare class assignments missing the 'class' keyword
            // e.g. "    nodeId successStyle" → "    class nodeId successStyle"
            raw = System.Text.RegularExpressions.Regex.Replace(
                raw,
                @"^(\s*)(\w+)\s+(successStyle|errorStyle|decisionStyle|processStyle|ioStyle)\s*$",
                "$1class $2 $3",
                System.Text.RegularExpressions.RegexOptions.Multiline);

            // Fix literal newlines inside quoted strings (node/edge labels)
            // Use a state machine to only replace \n that appear between a pair of "..."
            var sb = new System.Text.StringBuilder(raw.Length);
            bool inQuote = false;
            foreach (char ch in raw)
            {
                if (ch == '"') inQuote = !inQuote;
                if (ch == '\n' && inQuote) sb.Append(' ');
                else sb.Append(ch);
            }
            raw = sb.ToString();

            return raw;
        }

        // Reduce file size by stripping blank lines and single-line comments before truncation.
        // Preserves block structure so the AI still sees meaningful code.
        private static string CompressCode(string code)
        {
            var lines = code.Split('\n');
            var result = new System.Text.StringBuilder(code.Length);
            bool prevBlank = false;
            foreach (var raw in lines)
            {
                var trimmed = raw.TrimStart();
                // Skip pure blank lines (allow one blank between blocks)
                bool isBlank = trimmed.Length == 0;
                if (isBlank) { if (!prevBlank) result.Append('\n'); prevBlank = true; continue; }
                prevBlank = false;
                // Skip standalone single-line comments (// ...) — not inline comments
                if (trimmed.StartsWith("//") && !trimmed.Contains("===")) continue;
                result.Append(raw); result.Append('\n');
            }
            return result.ToString();
        }

        private static string BuildChatPrompt(ChatRequestDto request)
        {
            var codeContext = string.IsNullOrWhiteSpace(request.OriginalCode)
                ? ""
                : $"\n\nContext — code the user is working on:\n```\n{request.OriginalCode}\n```\n";

            return $@"You are a helpful, knowledgeable AI assistant. You can answer questions about ANY topic: programming, technology, science, math, history, language, everyday life — anything the user asks.
{codeContext}
Conversation so far:
{request.ChatHistory}

User: {request.UserMessage}

Reply directly and concisely in Vietnamese. If the question is about the code above, reference it specifically. If it's a general question, just answer it naturally.";
        }
    }
}
