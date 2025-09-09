using Lamdat.ADOAutomationTool.Auth;
using Lamdat.ADOAutomationTool.Entities;
using Lamdat.ADOAutomationTool.Service;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Net;
using Serilog.Sinks.File;
using Serilog;
using Lamdat.ADOAutomationTool.ScriptEngine;
using System;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddLogging(opt =>
{
    //opt.AddConsole(c =>
    //{
    //    c.TimestampFormat = "[dd-MM-yyyy HH:mm:ss] ";
    //});
    opt.ClearProviders();

});

builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false) // Set to false in production
                    .AddEnvironmentVariables()
                    .AddCommandLine(args);

// Move settings loading before authentication configuration
var settings = builder.Configuration.GetSection("Settings").Get<Settings>();
if (settings == null)
{
    Console.WriteLine("An error has occured during parse of appsettings.json");
    return;
}

// Validate SharedKey early
if (string.IsNullOrWhiteSpace(settings.SharedKey))
{
    Console.WriteLine("ERROR: Shared key is not defined or null, please set the shared key");
    return; // Don't start the application if SharedKey is missing
}

builder.Services.Configure<Settings>(builder.Configuration.GetSection("Settings"));
builder.WebHost.UseKestrel().UseUrls("http://*:5000");

builder.Services.AddAuthentication("BasicAuthentication")
    .AddScheme<BasicAuthenticationOptions, BasicAuthenticationHandler>("BasicAuthentication", options => { });

// Use the already loaded settings to avoid race conditions
builder.Services.Configure<BasicAuthenticationOptions>(options =>
{
    options.SharedKey = settings.SharedKey; // Use the validated settings
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins", cors =>
    {
        cors.WithOrigins(
                "https://dev.azure.com", 
                "https://*.visualstudio.com",
                settings.AllowedCorsOrigin
            )
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}]: {Level:u4} | {Message:l}{NewLine}{Exception}")
    .WriteTo.File($"{AppDomain.CurrentDomain.BaseDirectory}/logs/logfile.log", rollingInterval: RollingInterval.Day)
    .CreateBootstrapLogger();

builder.Services.AddSingleton(Log.Logger);
builder.Services.AddSingleton<WebHookContextQueue>(c => new WebHookContextQueue(Log.Logger, settings));
builder.Services.AddSingleton<CSharpScriptEngine>(c => new CSharpScriptEngine(Log.Logger));
builder.Services.AddSingleton<ScheduledScriptEngine>(c => new ScheduledScriptEngine(Log.Logger, settings));
builder.Services.AddTransient<IContext, Context>();
builder.Services.AddSingleton<IAzureDevOpsClient, AzureDevOpsClient>(c => new AzureDevOpsClient(Log.Logger, settings.CollectionURL, settings.PAT, settings.BypassRules, settings.NotValidCertificates));
builder.Services.AddTransient<IS3StorageClient, S3StorageClient>(c => new S3StorageClient(Log.Logger, settings.RulesStorageType, settings.S3StorageRegion, settings.S3Endpoint, settings.S3BucketName, settings.S3SecretKey, settings.S3AccessKey, settings.S3FolderPath)); ;
builder.Services.AddTransient<IWebHookHandlerService, WebHookHandlerService>();
builder.Services.AddSingleton<IMemoryCleaner>(m => new MemoryCleaner(Log.Logger, settings.MemoryCleanupMinutes));
builder.Services.AddSingleton<IScheduledTaskService>(s => new ScheduledTaskService(
    Log.Logger, 
    s.GetRequiredService<ScheduledScriptEngine>(), 
    settings, 
    s.GetRequiredService<IAzureDevOpsClient>()));


builder.Host.UseSerilog();
var app = builder.Build();

var contextRequestProcessingService = new WebHoolsContextQueueProcessorService(
      app.Services.GetRequiredService<WebHookContextQueue>(),
      app.Services.GetRequiredService<CSharpScriptEngine>(),
      app.Services.GetRequiredService<Serilog.ILogger>()
  );

var logger = app.Services.GetRequiredService<ILogger<Program>>();
var memoryCleaner = app.Services.GetRequiredService<IMemoryCleaner>();
memoryCleaner.Activate();

var scheduledTaskService = app.Services.GetRequiredService<IScheduledTaskService>();
await scheduledTaskService.Start();

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

/// <summary>
/// Sanitizes user input for logging by removing newlines and carriage returns to prevent log injection
/// </summary>
/// <param name="input">The input string to sanitize</param>
/// <returns>Sanitized string safe for logging</returns>
static string SanitizeForLogging(string input)
{
    if (string.IsNullOrEmpty(input))
        return input;
    
    return input.Replace("\n", "").Replace("\r", "");
}

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/webhook"))
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        // Sanitize the remote IP address before logging to prevent log injection
        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        var sanitizedRemoteIp = SanitizeForLogging(remoteIp);
        
        // Log detailed request information for debugging
        logger.LogDebug("Webhook request received: {Method} {Path} from {RemoteIP}, Content-Length: {ContentLength}, Content-Type: {ContentType}", 
            context.Request.Method, 
            context.Request.Path, 
            sanitizedRemoteIp,
            context.Request.ContentLength?.ToString() ?? "null",
            context.Request.ContentType ?? "null");
            
        // Check for potential issues before processing
        if (context.Request.Method == "POST")
        {
            var contentLength = context.Request.ContentLength;
            var hasAuthHeader = context.Request.Headers.ContainsKey("Authorization");
            
            logger.LogDebug("POST webhook details - ContentLength: {ContentLength}, HasAuthHeader: {HasAuth}, UserAgent: {UserAgent}", 
                contentLength?.ToString() ?? "null", 
                hasAuthHeader,
                SanitizeForLogging(context.Request.Headers.UserAgent.ToString()));
                
            // Warn about potential issues
            if (!hasAuthHeader)
            {
                logger.LogDebug("Webhook POST request missing Authorization header from {RemoteIP}", sanitizedRemoteIp);
            }
            
            if (contentLength == null)
            {
                logger.LogWarning("Webhook POST request missing Content-Length header from {RemoteIP}", sanitizedRemoteIp);
            }
            else if (contentLength == 0)
            {
                logger.LogWarning("Webhook POST request has Content-Length 0 from {RemoteIP}", sanitizedRemoteIp);
            }
        }
    }
    
    try
    {
        await next();
    }
    catch (Microsoft.AspNetCore.Server.Kestrel.Core.BadHttpRequestException ex) when (ex.Message.Contains("Unexpected end of request content"))
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        var sanitizedRemoteIp = SanitizeForLogging(remoteIp);
        
        logger.LogWarning("Bad HTTP request from {RemoteIP}: {Error}. Request: {Method} {Path}, Content-Length: {ContentLength}", 
            sanitizedRemoteIp, 
            ex.Message,
            context.Request.Method,
            context.Request.Path,
            context.Request.ContentLength?.ToString() ?? "null");
            
        // Return a proper HTTP response instead of letting it bubble up
        context.Response.StatusCode = 400;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"error\":\"Bad request - incomplete content\",\"details\":\"Request content was incomplete or malformed\"}");
    }
    catch (Exception ex)
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        var sanitizedRemoteIp = SanitizeForLogging(remoteIp);
        
        logger.LogError(ex, "Unhandled exception in request pipeline from {RemoteIP}: {Method} {Path}", 
            sanitizedRemoteIp, context.Request.Method, context.Request.Path);
        
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"Internal server error\"}");
        }
    }
});

app.UseAuthentication();
app.UseCors("AllowSpecificOrigins"); // Add this line - CRITICAL!
app.UseAuthorization();

app.MapControllers();


if (settings?.EnableAutoHttpsRedirect == true)
    app.UseHttpsRedirection();

if (string.IsNullOrWhiteSpace(settings.CollectionURL))
    logger.LogWarning("Azure DevOps Collection URL not set in configuration");
else
    logger.LogInformation($"Azure DevOps Collection URL: {settings.CollectionURL}");

if (string.IsNullOrWhiteSpace(settings.AllowedCorsOrigin))
    logger.LogWarning("Azure DevOps allowed CORS not set in configuration");
else
    logger.LogInformation($"Azure DevOps allowed CORS origin: {settings.AllowedCorsOrigin}");

logger.LogInformation($"If to allow not valid Azure Devops Certificates: {settings.NotValidCertificates}");

if (string.IsNullOrWhiteSpace(settings.PAT))
    logger.LogWarning("PAT not set in configuration");

if (string.IsNullOrWhiteSpace(settings.SharedKey))
    logger.LogWarning("Shared Key not set in configuration");

if (settings.ScriptExecutionTimeoutSeconds == 0)
    settings.ScriptExecutionTimeoutSeconds = 60; // Default to 60 seconds if not set

if (settings.MaxQueueWebHookRequestCount == 0)
    settings.ScriptExecutionTimeoutSeconds = 1000; // Default to 1000 seconds if not set

logger.LogInformation($"Max Webhook Queue Count is {settings.MaxQueueWebHookRequestCount}");
logger.LogInformation($"Script Execution timeout is {settings.ScriptExecutionTimeoutSeconds} seconds");
logger.LogInformation($"Scheduled Task interval is {settings.ScheduledTaskIntervalMinutes} minutes");

var csScriptEngine = app.Services.GetRequiredService<CSharpScriptEngine>();

var webHandler = app.Services.GetRequiredService<IWebHookHandlerService>();
webHandler.Init();

if (settings.RulesStorageType != RulesStorageType.Disk)
{
    var s3ClientHandle = app.Services.GetRequiredService<IS3StorageClient>();
    s3ClientHandle.DownloadRules().Wait();
}

app.Run();
