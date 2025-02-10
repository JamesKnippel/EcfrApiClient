using EcfrApi.Web.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.EntityFrameworkCore;
using EcfrApi.Web.Data;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

// Add database
builder.Services.AddDbContext<EcfrDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("DefaultConnection") ?? 
        "Data Source=ecfr.db"));

// Add services to the container.
builder.Services.AddHttpClient<IEcfrClient, EcfrClient>(client =>
{
    client.BaseAddress = new Uri("https://www.ecfr.gov");
    client.DefaultRequestHeaders.Add("User-Agent", "EcfrApiClient/1.0");
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Add caching services
builder.Services.AddScoped<ITitleCacheService, TitleCacheService>();
builder.Services.AddHostedService<TitleCacheUpdateService>();

var app = builder.Build();

// Get base path from environment variable
var basePath = Environment.GetEnvironmentVariable("ASPNETCORE_BASEPATH") ?? "";

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}

// Use CORS before routing
app.UseCors();

// Use routing and endpoints
app.UseRouting();
app.UseAuthorization();

if (!string.IsNullOrEmpty(basePath))
{
    app.UsePathBase(basePath);
}

app.MapControllers();
app.MapHealthChecks("/health");

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<EcfrDbContext>();
    context.Database.EnsureCreated();
}

app.Run();
