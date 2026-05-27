using Repositories;
using Services;

var builder = WebApplication.CreateBuilder(args);

// Allow long-running AI review requests (up to 5 min) without Kestrel closing the connection.
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.KeepAliveTimeout       = TimeSpan.FromMinutes(5);
    o.Limits.RequestHeadersTimeout  = TimeSpan.FromMinutes(2);
});
builder.Services.AddMemoryCache();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazor", policy =>
        policy.WithOrigins("https://localhost:7249") // Chỉ cho phép Blazor app
              .AllowAnyMethod()
              .AllowAnyHeader());
});

builder.Services.AddControllers();
builder.Services.AddHttpClient();

builder.Services.AddScoped<IAIReviewRepository, AIReviewRepository>();
builder.Services.AddScoped<IAIReviewService, AIReviewService>();
builder.Services.AddScoped<IPdfReportService, PdfReportService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddSingleton<IVerificationService, VerificationService>();
builder.Services.AddScoped<IShareService, ShareService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsProduction())
    app.UseHttpsRedirection();

app.UseCors("AllowBlazor");

app.UseAuthorization();

app.MapControllers();

app.Run();