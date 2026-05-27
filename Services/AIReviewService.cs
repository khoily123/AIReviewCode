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
                int fileMarkerCount = CountOccurrences(codeToAnalyze, "// === FILE:");
                bool isMultiFile = fileMarkerCount > 1 ||
                    (fileMarkerCount == 0 && codeToAnalyze.Contains("// ===") &&
                     codeToAnalyze.IndexOf("// ===") != codeToAnalyze.LastIndexOf("// ==="));
                bool isGitHubPr = codeToAnalyze.TrimStart().StartsWith("// PR:") || codeToAnalyze.TrimStart().StartsWith("// Source: GitHub") || codeToAnalyze.TrimStart().StartsWith("// Compare:") || codeToAnalyze.TrimStart().StartsWith("// Branch:");
                bool isSingleUpload = fileMarkerCount == 1 && !isMultiFile && !isDiff && !isGitHubPr;

                // For single uploaded file: strip the "// === FILE: name ===" header line so
                // AI line numbers match the actual file (header would offset everything by 1)
                if (isSingleUpload)
                {
                    var headerEnd = codeToAnalyze.IndexOf('\n');
                    if (headerEnd >= 0) codeToAnalyze = codeToAnalyze[(headerEnd + 1)..];
                }

                // Do NOT compress code — CompressCode removes blank lines which breaks line numbers.
                // Just truncate at the character limit if needed.
                int maxCodeChars = (isMultiFile || isDiff || isGitHubPr) ? 24_000 : 40_000;

                if (codeToAnalyze.Length > maxCodeChars)
                {
                    // Truncate at last complete line to avoid cutting mid-line
                    var cutOff = codeToAnalyze.LastIndexOf('\n', maxCodeChars);
                    codeToAnalyze = cutOff > 0 ? codeToAnalyze[..cutOff] : codeToAnalyze[..maxCodeChars];
                    truncatedNote = $"\n[NOTE: File truncated at line {codeToAnalyze.Count(c => c == '\n')} — review visible portion only.]\n";
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
- CRITICAL: 'fixedCode': {(truncatedNote.Length > 0 ? "The code was truncated so you CANNOT rewrite the full file. Return EXACTLY empty string \"\" — do NOT write any explanation, message, or partial code in this field." : "MUST contain the complete, production-ready fixed file. Preserve all indentation and newlines as \\n. Do not minify. Do NOT add any new imports, using statements, or dependencies that were not already present in the original code — only fix actual bugs.")}
- CRITICAL: If the provided code is already perfect and has no bugs, DO NOT invent bugs. Return an empty array [] for 'bugs' and score 100 for all metrics.
- CRITICAL: 'mermaidChart': Return EXACTLY empty string "" — the flowchart is generated in a dedicated separate pass.
- CRITICAL: If a security vulnerability is found (like SQL Injection), provide a specific attack payload example in 'hackerExploit'. If no security vulnerability exists, return an empty string """".
- CRITICAL: 'unitTests' MUST contain a complete, compilable xUnit test class (using Xunit; namespace) that covers all public methods. Include proper using statements. Use \n for newlines inside the JSON string value.

Code to analyze:
{customRulesSection}

{codeToAnalyze}{truncatedNote}
";

                // Flowchart is generated separately by the client after review completes.
                // Running both concurrently caused Gemini rate-limiting and hung the entire request.
                bool generateSeparateFlowchart = !isDiff && !isGitHubPr && !isMultiFile;

                var aiResult = await _repository.CallAI(prompt);
                var separateMermaid = "";

                var modelResult = JsonSerializer.Deserialize<ReviewResponse>(aiResult, _jsonOptions)
                    ?? new ReviewResponse();

                // Discard fixedCode when: (a) file was truncated, or (b) AI returned explanatory text
                var fixedCode = modelResult.FixedCode;
                if (!string.IsNullOrEmpty(fixedCode))
                {
                    // If input was truncated the AI can't produce a complete fix — always discard
                    if (truncatedNote.Length > 0)
                    {
                        fixedCode = null;
                    }
                    else
                    {
                        // Discard if it looks like a prose message: short, no newlines, no code keywords
                        var t = fixedCode.TrimStart();
                        bool hasCodeKeyword = t.StartsWith("//") || t.StartsWith("using ") || t.StartsWith("namespace ")
                            || t.StartsWith("public ") || t.StartsWith("private ") || t.StartsWith("protected ")
                            || t.StartsWith("class ") || t.StartsWith("import ") || t.StartsWith("def ")
                            || t.StartsWith("func ") || t.StartsWith("package ") || t.StartsWith("<!") || t.StartsWith("<?")
                            || (t.StartsWith("<") && t.Length > 1 && char.IsLetter(t[1]) && t.Contains('>'));
                        bool looksLikeMessage = !hasCodeKeyword && (t.Length < 800 || !t.Contains('\n'));
                        if (looksLikeMessage) fixedCode = null;
                    }
                }

                var dtoResult = new ReviewResponseDto
                {
                    Summary = modelResult.Message,
                    FixedCode = fixedCode,
                    PerformanceScore = modelResult.PerformanceScore,
                    SecurityScore = modelResult.SecurityScore,
                    MaintainabilityScore = modelResult.MaintainabilityScore,
                    MermaidChart = generateSeparateFlowchart
                        ? separateMermaid
                        : SanitizeMermaid(modelResult.MermaidChart),
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

        public async Task<string> GenerateFlowchart(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "";
            bool isTruncated = code.Length > 40_000;
            var snippet = isTruncated ? code[..40_000] : code;
            return await GenerateFlowchartAsync(snippet, isTruncated);
        }

        private static readonly string FlowchartStyleDefs = @"classDef successStyle fill:#d1fae5,stroke:#10b981,color:#064e3b
classDef errorStyle fill:#fee2e2,stroke:#ef4444,color:#7f1d1d
classDef decisionStyle fill:#dbeafe,stroke:#3b82f6,color:#1e3a5f
classDef processStyle fill:#f3f4f6,stroke:#6b7280,color:#1f2937
classDef ioStyle fill:#fef3c7,stroke:#f59e0b,color:#78350f
classDef startEnd fill:#1e293b,stroke:#475569,color:#f8fafc";

        private async Task<string> GenerateFlowchartAsync(string code, bool isTruncated)
        {
            try
            {
                var prompt = isTruncated
                    ? $@"You are a software architect. Analyze the code below and generate a HIGH-LEVEL ARCHITECTURE diagram.

Show ALL classes, ALL public and private methods, and how they call each other.
Group methods into subgraphs by class.

Output ONLY raw Mermaid syntax — no JSON, no markdown fences, no explanation.

RULES:
- First line MUST be exactly: flowchart TD
- Node IDs: ASCII letters/digits/underscore only (NO Vietnamese, NO spaces in IDs)
- Labels with special chars or spaces MUST be quoted: A[""GetUser(id)""]
- Decision diamonds: A{{""Is valid?""}}
- NEVER mix bracket types: rectangle nodes use [""label""] with square brackets on BOTH sides; diamond decisions use {{""label""}} with curly braces on BOTH sides. One open-square with close-curly is invalid.
- Terminals: START([""▶ Start""]) endNode([""⏹ End""]) — NEVER use 'end' as a node ID (reserved keyword)
- Edge labels quoted: -->|""yes""| -->|""no""| -->|""error""|
- MINIMUM 15 nodes
- Each class in its own subgraph block
- Apply styles at the end:
{FlowchartStyleDefs}

Code:
{code}"
                    : $@"You are a software architect. Analyze the code below and produce a COMPREHENSIVE execution-flow diagram.

Your job:
1. Find every class and method in the file.
2. For each method, trace the COMPLETE execution:
   - Entry point → validations → branching (if/else/try/catch) → calls → return/throw
3. Show every decision as a diamond node with yes/no/error edges.
4. Group each method's internal flow into a named subgraph.
5. Connect methods to show call chains.

Output ONLY raw Mermaid syntax — no JSON, no markdown fences, no explanation.

STRICT RULES:
- First line MUST be exactly: flowchart TD
- Node IDs: ASCII letters/digits/underscore ONLY — no spaces, no Vietnamese, no special chars
- Labels with special chars or spaces MUST be quoted: A[""ReviewCode(request)""]
- Decision diamonds with quoted label: check{{""Input null?""}}
- NEVER mix bracket types: rectangle nodes use [""label""] with square brackets on BOTH sides; diamond decisions use {{""label""}} with curly braces on BOTH sides. One open-square with close-curly is invalid.
- Terminals: START([""▶ Start""]) endNode([""⏹ End""]) — NEVER use 'end' as a node ID (reserved keyword)
- ALL edge labels with spaces quoted: -->|""on error""|
- Use one subgraph per class/major method group. CORRECT subgraph syntax:
    subgraph GroupName
        nodeA --> nodeB
    end
  NEVER put node IDs or lists inside subgraph labels. NEVER use [brackets] in subgraph declarations.
- EVERY if/else → diamond node with |""yes""| and |""no""| edges
- EVERY try/catch → diamond node with |""success""| and |""error""| edges
- ALL error/exception paths MUST be shown
- MINIMUM 20 nodes; complex files should have 30+ nodes
- Apply styles at end of chart:
{FlowchartStyleDefs}

Syntax reference (do NOT copy these node names into your output — analyze ONLY the code below):
flowchart TD
    START([""▶ Start""])
    START --> checkA{{""Condition A?""}}
    checkA -->|""yes""| doStep1[""Step 1""]
    checkA -->|""no""| doStep2[""Step 2""]
    doStep1 --> checkB{{""Condition B?""}}
    checkB -->|""success""| doStep3[""Step 3""]
    checkB -->|""error""| throwErr[""Throw Exception""]
    doStep2 --> doStep3
    doStep3 --> END([""⏹ End""])
    throwErr --> END
    classDef decisionStyle fill:#dbeafe,stroke:#3b82f6,color:#1e3a5f
    classDef errorStyle fill:#fee2e2,stroke:#ef4444,color:#7f1d1d
    classDef startEnd fill:#1e293b,stroke:#475569,color:#f8fafc
    class checkA,checkB decisionStyle
    class throwErr errorStyle
    class START,END startEnd

IMPORTANT: The nodes above (checkA, doStep1, etc.) are syntax examples ONLY. Your output must contain ONLY nodes that represent code from the file below. Do not include any node from the example above.

Code to analyze:
{code}";

                var raw = await _repository.CallAIText(prompt);

                // Strip markdown fences in case AI added them
                if (raw.Contains("```"))
                    raw = System.Text.RegularExpressions.Regex.Replace(raw, @"```\w*\r?\n?", "").Trim();

                return SanitizeMermaid(raw);
            }
            catch
            {
                return "";
            }
        }

        // Known Mermaid classDef names used in prompts
        private static readonly System.Collections.Generic.HashSet<string> _knownStyles =
            new(System.StringComparer.OrdinalIgnoreCase)
            { "decisionStyle","errorStyle","successStyle","processStyle","ioStyle","startEnd" };

        private static string SanitizeMermaid(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw ?? "";

            // Strip markdown fences
            raw = raw.Replace("```mermaid", "").Replace("```", "").Trim();

            // Step 0: Rename Mermaid reserved keywords used as node IDs.
            // "end" closes subgraph blocks, so it cannot be a node ID.
            // Strategy: replace every word-boundary \bend\b → endNode,
            // then restore lines that are standalone "end" (subgraph closers).
            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"\bend\b", "endNode");
            raw = System.Text.RegularExpressions.Regex.Replace(
                raw, @"^\s*endNode\s*$",
                m => m.Value.Replace("endNode", "end"),
                System.Text.RegularExpressions.RegexOptions.Multiline);

            // Step 0b: Fix broken subgraph declarations from AI.
            // AI sometimes puts node-ID lists in subgraph labels:
            //   subgraph MyGroup ["node1  node2  node3"]  → subgraph MyGroup
            //   subgraph CreateHostBuilder [              → subgraph CreateHostBuilder
            // Strip anything after "subgraph <id>" on the same line.
            raw = System.Text.RegularExpressions.Regex.Replace(
                raw, @"^(\s*subgraph\s+\w+)\s*\[.*$", "$1",
                System.Text.RegularExpressions.RegexOptions.Multiline);

            // Remove orphan "]" lines left by the block-body pattern "subgraph Name [\n...\n]"
            raw = System.Text.RegularExpressions.Regex.Replace(
                raw, @"^\s*\]\s*$", "",
                System.Text.RegularExpressions.RegexOptions.Multiline);

            // Close any unclosed subgraph blocks (count subgraph vs end).
            {
                var lines2 = raw.Split('\n').ToList();
                int depth2 = 0;
                foreach (var ln in lines2)
                {
                    var t = ln.Trim();
                    if (t.StartsWith("subgraph ") || t == "subgraph") depth2++;
                    else if (t == "end") depth2 = Math.Max(0, depth2 - 1);
                }
                while (depth2-- > 0) lines2.Add("end");
                raw = string.Join('\n', lines2);
            }

            // Step 0c: Fix mismatched bracket/brace closers in node labels.
            // AI sometimes writes: nodeId["label"} or nodeId{"label"]
            // ["label"} → ["label"]   {"label"] → {"label"}
            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"\[""([^""]*)""\}", "[\"$1\"]");
            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"\{""([^""]*)""\]", "{\"$1\"}");
            // Also handle unquoted label variants: [label} → [label]  {label] → {label}
            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"\[([^\[\]{}""\n]+)\}", "[$1]");
            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"\{([^\[\]{}""\n]+)\]", "{$1}");

            // Step 1: Replace non-ASCII chars in node IDs (outside label brackets / quotes)
            // Labels are inside [..], {..}, (..) or ".."; node IDs are outside these
            raw = SanitizeNodeIds(raw);

            // Step 2: Fix "class A B C styleName" → "class A,B,C styleName"
            raw = System.Text.RegularExpressions.Regex.Replace(
                raw,
                @"^(\s*class\s+)(\w[\w, ]*?)(\s+\w+\s*)$",
                m =>
                {
                    var prefix = m.Groups[1].Value;
                    var nodesPart = m.Groups[2].Value;
                    var stylePart = m.Groups[3].Value.Trim();
                    // Split tokens; last token is style name, rest are node IDs
                    var tokens = nodesPart.Split(new[]{' ',','}, System.StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length > 1 && _knownStyles.Contains(stylePart))
                    {
                        return prefix + string.Join(",", tokens) + " " + stylePart;
                    }
                    if (tokens.Length > 1)
                    {
                        // Last token might be style, rest are node IDs
                        var nodeIds = string.Join(",", tokens);
                        return prefix + nodeIds + " " + stylePart;
                    }
                    return m.Value;
                },
                System.Text.RegularExpressions.RegexOptions.Multiline);

            // Fix invalid edge syntax: -->|"label"|> Node  →  -->|"label"| Node
            raw = raw.Replace("\"|>", "\"|");
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

        // Replace non-ASCII chars in node IDs (outside label brackets and quotes).
        // Also auto-quotes unquoted labels that contain spaces: [label text] → ["label text"]
        private static string SanitizeNodeIds(string raw)
        {
            var sb = new System.Text.StringBuilder(raw.Length);
            int depth = 0;   // bracket depth: [, {, (
            bool inQuote = false;

            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];

                if (c == '"' && depth == 0) { inQuote = !inQuote; sb.Append(c); continue; }
                if (inQuote) { sb.Append(c); continue; }

                if (c is '[' or '{' or '(') { depth++; sb.Append(c); continue; }
                if (c is ']' or '}' or ')') { depth = Math.Max(0, depth - 1); sb.Append(c); continue; }

                // Outside labels: replace non-ASCII identifier chars with underscore
                if (depth == 0 && c > 127 && (char.IsLetterOrDigit(c) || c == '_'))
                    sb.Append('_');
                else
                    sb.Append(c);
            }

            var result = sb.ToString();

            // Auto-quote unquoted node labels that contain spaces: [label text] → ["label text"]
            result = System.Text.RegularExpressions.Regex.Replace(
                result,
                @"\[(?!"")([^\]""]+)\]",
                m =>
                {
                    var inner = m.Groups[1].Value.Trim();
                    // Only quote if it has spaces or special chars (already-single-word labels are fine)
                    return inner.Contains(' ') || inner.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '_')
                        ? "[\"" + inner + "\"]"
                        : m.Value;
                });

            return result;
        }

        private static int CountOccurrences(string text, string pattern)
        {
            int count = 0, idx = 0;
            while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
            { count++; idx += pattern.Length; }
            return count;
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
                : $"\n\n--- CODE ĐANG XEM XÉT ---\n```\n{request.OriginalCode}\n```\n";

            var reviewContext = string.IsNullOrWhiteSpace(request.ReviewContext)
                ? ""
                : $"\n\n--- KẾT QUẢ REVIEW VỪA THỰC HIỆN ---\n{request.ReviewContext}\n";

            return $@"You are a helpful AI assistant embedded in an AI Code Review tool. You can answer ANY question, but you have full context of the code and review results shown below — use them when the user asks about their code or the review.
{codeContext}{reviewContext}
Conversation so far:
{request.ChatHistory}

User: {request.UserMessage}

Reply in Vietnamese. When referencing bugs or review findings, be specific (mention line numbers, bug descriptions). If it's a general question unrelated to the code, answer naturally.";
        }
    }
}
