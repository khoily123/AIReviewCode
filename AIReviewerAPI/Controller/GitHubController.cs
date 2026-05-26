using AIReviewerAPI.DTOs;
using Microsoft.AspNetCore.Mvc;
using Services;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIReviewerAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GitHubController : ControllerBase
    {
        private readonly IAIReviewService _aiService;
        private readonly IHttpClientFactory _httpFactory;

        public GitHubController(IAIReviewService aiService, IHttpClientFactory httpFactory)
        {
            _aiService = aiService;
            _httpFactory = httpFactory;
        }

        [HttpPost("pr-review")]
        public async Task<IActionResult> ReviewPr([FromBody] GitHubPrRequestDto request)
        {
            var url = (request.PrUrl ?? "").Trim().Split('#')[0]; // strip fragment

            var http = _httpFactory.CreateClient();
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AIReviewer", "1.0"));
            if (!string.IsNullOrWhiteSpace(request.GithubToken))
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", request.GithubToken);

            string code;
            string label;

            // 1. PR URL: /owner/repo/pull/123
            var prMatch = Regex.Match(url, @"github\.com/([^/]+)/([^/]+)/pull/(\d+)", RegexOptions.IgnoreCase);

            // 2. Compare URL: /owner/repo/compare/base...head
            var cmpMatch = Regex.Match(url, @"github\.com/([^/]+)/([^/]+)/compare/([^/]+)", RegexOptions.IgnoreCase);

            // 3. File URL: /owner/repo/blob/branch/path/to/file
            var fileMatch = Regex.Match(url, @"github\.com/([^/]+)/([^/]+)/blob/([^/]+)/(.+)", RegexOptions.IgnoreCase);

            // 4. Repo URL: /owner/repo  (review latest commit)
            var repoMatch = Regex.Match(url, @"github\.com/([^/]+)/([^/]+)/?$", RegexOptions.IgnoreCase);

            if (prMatch.Success)
            {
                var (owner, repo, prNum) = (prMatch.Groups[1].Value, prMatch.Groups[2].Value, prMatch.Groups[3].Value);
                label = $"GitHub PR #{prNum} — {owner}/{repo}";
                code = await FetchPrCode(http, owner, repo, prNum);
            }
            else if (cmpMatch.Success)
            {
                var (owner, repo, range) = (cmpMatch.Groups[1].Value, cmpMatch.Groups[2].Value, cmpMatch.Groups[3].Value);
                label = $"GitHub Compare {range} — {owner}/{repo}";
                code = await FetchCompareCode(http, owner, repo, range);
            }
            else if (fileMatch.Success)
            {
                var (owner, repo, branch, path) = (fileMatch.Groups[1].Value, fileMatch.Groups[2].Value,
                    fileMatch.Groups[3].Value, fileMatch.Groups[4].Value);
                label = $"GitHub File {path} — {owner}/{repo}@{branch}";
                code = await FetchFileCode(http, owner, repo, branch, path);
            }
            else if (repoMatch.Success)
            {
                var (owner, repo) = (repoMatch.Groups[1].Value, repoMatch.Groups[2].Value);
                label = $"GitHub Repo — {owner}/{repo} (latest commit)";
                code = await FetchLatestCommitCode(http, owner, repo);
            }
            else
            {
                return BadRequest(new
                {
                    message = "URL không đúng định dạng. Hỗ trợ:\n" +
                              "• PR:      https://github.com/owner/repo/pull/123\n" +
                              "• File:    https://github.com/owner/repo/blob/main/src/File.cs\n" +
                              "• Compare: https://github.com/owner/repo/compare/main...feature\n" +
                              "• Repo:    https://github.com/owner/repo"
                });
            }

            if (string.IsNullOrWhiteSpace(code) || code.Length < 20)
                return BadRequest(new { message = "Không tìm thấy code nào để review từ URL này." });

            var result = await _aiService.ReviewCode(new ReviewRequestDto
            {
                Code = $"// Source: {label}\n\n{code}",
                Persona = request.Persona
            });
            return Ok(result);
        }

        // ── PR ────────────────────────────────────────────────────────────
        private static async Task<string> FetchPrCode(HttpClient http, string owner, string repo, string prNum)
        {
            var prResp = await http.GetAsync($"https://api.github.com/repos/{owner}/{repo}/pulls/{prNum}");
            if (!prResp.IsSuccessStatusCode)
                throw new Exception($"Không lấy được PR: {prResp.StatusCode}. Nếu là private repo hãy nhập Token.");

            using var prDoc = JsonDocument.Parse(await prResp.Content.ReadAsStringAsync());
            var prTitle = prDoc.RootElement.TryGetProperty("title", out var t) ? t.GetString() : "PR";

            var filesResp = await http.GetAsync(
                $"https://api.github.com/repos/{owner}/{repo}/pulls/{prNum}/files?per_page=30");
            if (!filesResp.IsSuccessStatusCode)
                throw new Exception("Không lấy được danh sách file thay đổi.");

            using var filesDoc = JsonDocument.Parse(await filesResp.Content.ReadAsStringAsync());
            var sb = new StringBuilder();
            sb.AppendLine($"// PR: {prTitle}");
            foreach (var file in filesDoc.RootElement.EnumerateArray())
            {
                var fn    = file.TryGetProperty("filename", out var f) ? f.GetString() : "unknown";
                var st    = file.TryGetProperty("status",   out var s) ? s.GetString() : "";
                var patch = file.TryGetProperty("patch",    out var p) ? p.GetString() : "";
                if (string.IsNullOrEmpty(patch)) continue;
                sb.AppendLine($"// === {fn} [{st}] ===");
                sb.AppendLine(ParseDiffPatch(patch));
            }
            return sb.ToString();
        }

        // ── Compare ───────────────────────────────────────────────────────
        private static async Task<string> FetchCompareCode(HttpClient http, string owner, string repo, string range)
        {
            var resp = await http.GetAsync(
                $"https://api.github.com/repos/{owner}/{repo}/compare/{range}");
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Không lấy được comparison: {resp.StatusCode}");

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var sb = new StringBuilder();
            sb.AppendLine($"// Compare: {range}");
            if (doc.RootElement.TryGetProperty("files", out var files))
            {
                foreach (var file in files.EnumerateArray())
                {
                    var fn    = file.TryGetProperty("filename", out var f) ? f.GetString() : "unknown";
                    var patch = file.TryGetProperty("patch",    out var p) ? p.GetString() : "";
                    if (string.IsNullOrEmpty(patch)) continue;
                    sb.AppendLine($"// === {fn} ===");
                    sb.AppendLine(ParseDiffPatch(patch));
                }
            }
            return sb.ToString();
        }

        // ── Single file ───────────────────────────────────────────────────
        private static async Task<string> FetchFileCode(
            HttpClient http, string owner, string repo, string branch, string path)
        {
            var resp = await http.GetAsync(
                $"https://api.github.com/repos/{owner}/{repo}/contents/{path}?ref={branch}");
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Không lấy được file: {resp.StatusCode}");

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("content", out var content))
            {
                var base64 = content.GetString()?.Replace("\n", "") ?? "";
                return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            }
            return "";
        }

        // ── Repo latest commit ────────────────────────────────────────────
        private static async Task<string> FetchLatestCommitCode(HttpClient http, string owner, string repo)
        {
            // Get default branch
            var repoResp = await http.GetAsync($"https://api.github.com/repos/{owner}/{repo}");
            if (!repoResp.IsSuccessStatusCode)
                throw new Exception($"Không truy cập được repo: {repoResp.StatusCode}. Nếu là private repo hãy nhập Token.");

            using var repoDoc = JsonDocument.Parse(await repoResp.Content.ReadAsStringAsync());
            var defaultBranch = repoDoc.RootElement.TryGetProperty("default_branch", out var db)
                ? db.GetString() ?? "main" : "main";

            // Get latest commit SHA
            var commitsResp = await http.GetAsync(
                $"https://api.github.com/repos/{owner}/{repo}/commits?sha={defaultBranch}&per_page=1");
            if (!commitsResp.IsSuccessStatusCode)
                throw new Exception("Không lấy được danh sách commit.");

            using var commitsDoc = JsonDocument.Parse(await commitsResp.Content.ReadAsStringAsync());
            var sha = commitsDoc.RootElement[0].GetProperty("sha").GetString() ?? "";

            // Get files changed in that commit
            var commitResp = await http.GetAsync($"https://api.github.com/repos/{owner}/{repo}/commits/{sha}");
            if (!commitResp.IsSuccessStatusCode)
                throw new Exception("Không lấy được chi tiết commit.");

            using var commitDoc = JsonDocument.Parse(await commitResp.Content.ReadAsStringAsync());
            var sb = new StringBuilder();
            sb.AppendLine($"// Branch: {defaultBranch} — latest commit: {sha[..7]}");

            if (commitDoc.RootElement.TryGetProperty("files", out var files))
            {
                foreach (var file in files.EnumerateArray())
                {
                    var fn    = file.TryGetProperty("filename", out var f) ? f.GetString() : "unknown";
                    var patch = file.TryGetProperty("patch",    out var p) ? p.GetString() : "";
                    if (string.IsNullOrEmpty(patch)) continue;
                    sb.AppendLine($"// === {fn} ===");
                    sb.AppendLine(ParseDiffPatch(patch));
                }
            }
            return sb.ToString();
        }

        // ── Diff parser ───────────────────────────────────────────────────
        // Converts raw git patch into annotated format with real line numbers.
        // Input:  @@ -10,8 +12,6 @@ ...
        //          context line
        //         -removed line
        //         +added line
        // Output: [L12]   context line
        //         [---]   removed line
        //         [L13]+  added line
        private static string ParseDiffPatch(string patch)
        {
            var sb = new StringBuilder();
            var hunkHeaderRe = new Regex(@"^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@", RegexOptions.Compiled);
            int newLine = 0;

            foreach (var raw in patch.Split('\n'))
            {
                var m = hunkHeaderRe.Match(raw);
                if (m.Success)
                {
                    newLine = int.Parse(m.Groups[1].Value);
                    // Print hunk context (function signature after @@) if present
                    var context = raw.Substring(m.Length).Trim();
                    sb.AppendLine(string.IsNullOrEmpty(context)
                        ? $"// -- changed block starting at line {newLine} --"
                        : $"// -- line {newLine}: {context} --");
                    continue;
                }

                if (raw.Length == 0) continue;
                char marker = raw[0];
                var content = raw.Length > 1 ? raw[1..] : "";

                switch (marker)
                {
                    case '+':
                        sb.AppendLine($"[L{newLine,4}]+ {content}");
                        newLine++;
                        break;
                    case '-':
                        sb.AppendLine($"[----]-  {content}");
                        break;
                    case ' ':
                        sb.AppendLine($"[L{newLine,4}]  {content}");
                        newLine++;
                        break;
                    default:
                        sb.AppendLine(raw);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
