using EcfrApi.Web.Services;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddHttpClient<IEcfrClient, EcfrClient>(client =>
{
    client.BaseAddress = new Uri("https://www.ecfr.gov");
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

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// Use CORS before routing
app.UseCors();

app.UseHttpsRedirection();
app.UseAuthorization();

// Serve static files from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

// Map controllers
app.MapControllers();
app.MapHealthChecks("/health");

// Fallback to index.html for SPA routes
app.MapFallbackToFile("index.html");

app.Run();
