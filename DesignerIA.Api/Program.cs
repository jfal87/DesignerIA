using System.Text;
using Microsoft.AspNetCore.Authentication.Negotiate;
using DesignerIA.Api.Services;
using DesignerIA.Api.Services.ViewScripts;
using DesignerIA.Api.Services.CuratedKnowledge;
using DesignerIA.Api.Services.Knowledge;

// Requerido para que MsgReader pueda decodificar RTF en encodings legacy (p. ej.
// Windows-1252) de archivos .msg; .NET no los incluye por defecto.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

const string DesignerIAWebCorsPolicy = "DesignerIAWebCorsPolicy";

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services
    .AddAuthentication(NegotiateDefaults.AuthenticationScheme)
    .AddNegotiate();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddScoped<GestionEngineHealthService>();
builder.Services.AddScoped<StringConnectionCatalogService>();
builder.Services.AddScoped<ViewCreationPreflightService>();
builder.Services.AddScoped<CopilotSmokeTestService>();
builder.Services.AddScoped<CopilotMetadataTestService>();
builder.Services.AddScoped<CopilotChatService>();
builder.Services.AddScoped<KnowledgeIndexService>();
builder.Services.AddScoped<KnowledgeSearchService>();
// Singleton: mantiene un único lock de archivo que serializa correctamente las
// transiciones de estado (Pending/Approved/Rejected) entre requests concurrentes.
builder.Services.AddSingleton<CuratedKnowledgeService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy(DesignerIAWebCorsPolicy, policy =>
    {
        policy.WithOrigins("https://localhost:7276")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors(DesignerIAWebCorsPolicy);

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();
