using Lamdat.ADOAutomationTool.Auth;
using Lamdat.ADOAutomationTool.Entities;
using Lamdat.ADOAutomationTool.Service;
using Microsoft.AspNetCore.Authentication;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddLogging(opt =>
{
    opt.AddConsole(c =>
    {
        c.TimestampFormat = "[dd-MM-yyyy HH:mm:ss] ";
    });
});

builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables()
                    .AddCommandLine(args);

builder.Services.Configure<Settings>(builder.Configuration.GetSection("Settings"));
builder.WebHost.UseKestrel().UseUrls("http://*:5000");

builder.Services.AddAuthentication("BasicAuthentication")
    .AddScheme<BasicAuthenticationOptions, BasicAuthenticationHandler>("BasicAuthentication", options => { });

builder.Services.Configure<BasicAuthenticationOptions>(options =>
{
    var settings = builder.Configuration.GetSection("Settings").Get<Settings>();
    if (string.IsNullOrWhiteSpace(settings.SharedKey))
        Console.WriteLine($"Shared key is not defined or null, please set the shared key");
    else
        options.SharedKey = settings?.SharedKey;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins", cors =>
    {
        var settings = builder.Configuration.GetSection("Settings").Get<Settings>();
        cors.WithOrigins(settings.AllowedCorsOrigin);
    });
});

var app = builder.Build();

var settings = app.Services.GetRequiredService<IOptions<Settings>>().Value;
var logger = app.Services.GetRequiredService<ILogger<Program>>();

AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
{
    var exception = eventArgs.ExceptionObject as Exception;
    if (exception != null)
    {
        logger.LogError(exception, "An unhandled exception occurred in AppDomain.");
    }
    else
    {
        logger.LogError("An unhandled non-exception object was thrown in AppDomain.");
    }
};

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.UseHttpsRedirection();
app.MapControllers();

var appSettings = builder.Configuration.GetSection("Settings").Get<Settings>();

if (appSettings?.EnableAutoHttpsRedirect == true)
    app.UseHttpsRedirection();

if (string.IsNullOrWhiteSpace(settings.CollectionURL))
    logger.LogWarning("Azure DevOps Collection URL not set in configuration");
else
    logger.LogInformation($"Azure DevOps Collection URL: {appSettings.CollectionURL}");

if (string.IsNullOrWhiteSpace(settings.AllowedCorsOrigin))
    logger.LogWarning("Azure DevOps allowed CORS not set in configuration");
else
    logger.LogInformation($"Azure DevOps allowed CORS origin: {appSettings.AllowedCorsOrigin}");

logger.LogInformation($"If to allow not valid Azure Devops Certificates: {appSettings.NotValidCertificates}");

if (string.IsNullOrWhiteSpace(settings.PAT))
    logger.LogWarning("PAT not set in configuration");

if (string.IsNullOrWhiteSpace(settings.SharedKey))
    logger.LogWarning("Shared Key not set in configuration");

var webHandler = new WebHookHandler(logger, settings);
webHandler.Init();

app.Run();
