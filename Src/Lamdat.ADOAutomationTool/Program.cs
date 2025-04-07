using Lamdat.ADOAutomationTool.Auth;
using Lamdat.ADOAutomationTool.Entities;
using Lamdat.ADOAutomationTool.Service;
using Microsoft.AspNetCore.Authentication;
using Microsoft.CodeAnalysis;
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

var settings = builder.Configuration.GetSection("Settings").Get<Settings>();
if (settings == null)
{
    Console.WriteLine("An error has occured during parse of appsettings.json");
    return;
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins", cors =>
    {
        cors.WithOrigins(settings.AllowedCorsOrigin);
    });
});

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}]: {Level:u4} | {Message:l}{NewLine}{Exception}")
    .WriteTo.File($"{AppDomain.CurrentDomain.BaseDirectory}/logs/logfile.log", rollingInterval: RollingInterval.Day)
    .CreateBootstrapLogger();

builder.Services.AddSingleton(Log.Logger);
builder.Services.AddSingleton<CSharpScriptEngine>(c => new CSharpScriptEngine(Log.Logger));
builder.Services.AddTransient<IContext, Context>();
builder.Services.AddSingleton<IAzureDevOpsClient, AzureDevOpsClient>(c => new AzureDevOpsClient(Log.Logger, settings.CollectionURL, settings.PAT, settings.BypassRules, settings.NotValidCertificates));
builder.Services.AddTransient<IS3StorageClient, S3StorageClient>(c => new S3StorageClient(Log.Logger, settings.RulesStorageType, settings.S3StorageRegion, settings.S3Endpoint, settings.S3BucketName, settings.S3SecretKey, settings.S3AccessKey, settings.S3FolderPath)); ;
builder.Services.AddTransient<IWebHookHandlerService, WebHookHandlerService>();
builder.Services.AddSingleton<IMemoryCleaner>(m => new MemoryCleaner(Log.Logger, settings.MemoryCleanupMinutes));

builder.Host.UseSerilog();
var app = builder.Build();


var logger = app.Services.GetRequiredService<ILogger<Program>>();
var memoryCleaner = app.Services.GetRequiredService<IMemoryCleaner>();
memoryCleaner.Activate();

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

var csScriptEngine = app.Services.GetRequiredService<CSharpScriptEngine>();

var webHandler = app.Services.GetRequiredService<IWebHookHandlerService>();
webHandler.Init();

if (settings.RulesStorageType != RulesStorageType.Disk)
{
    var s3ClientHandle = app.Services.GetRequiredService<IS3StorageClient>();
    s3ClientHandle.DownloadRules().Wait();
}

app.Run();
