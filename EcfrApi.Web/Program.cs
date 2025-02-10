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

app.Run();
