using CodeReviewer.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Extend SignalR timeouts so long AI calls (up to ~90s) don't trigger client reconnect.
// Default ClientTimeoutInterval is 30s — too short for AI reviews.
builder.Services.AddSignalR(o =>
{
    o.ClientTimeoutInterval    = TimeSpan.FromSeconds(300); // client disconnects after 5 min idle
    o.HandshakeTimeout         = TimeSpan.FromSeconds(30);
    o.KeepAliveInterval        = TimeSpan.FromSeconds(15);  // server pings every 15s
    o.MaximumReceiveMessageSize = 10 * 1024 * 1024;         // 10 MB — allows large file uploads
});

// Dùng IHttpClientFactory thay vì new HttpClient
builder.Services.AddHttpClient("ReviewApi", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var baseUrl = config["ApiBaseUrl"] ?? "https://localhost:7041/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(180);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", true);
    app.UseHsts();
}

if (!app.Environment.IsProduction())
    app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();