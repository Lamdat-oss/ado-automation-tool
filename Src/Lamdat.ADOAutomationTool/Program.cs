using Lamdat.ADOAutomationTool.Auth;
using Lamdat.ADOAutomationTool.Entities;
using Lamdat.ADOAutomationTool.Service;
using Microsoft.AspNetCore.Authentication;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true).AddEnvironmentVariables().AddCommandLine(args);
builder.Services.Configure<Settings>(builder.Configuration.GetSection("Settings"));
builder.WebHost.UseKestrel().UseUrls("http://*:5000");
builder.Services.Configure<BasicAuthenticationOptions>(options =>
{
    var settings = builder.Configuration.GetSection("Settings").Get<Settings>();
    options.SharedKey = settings?.SharedKey;
});
builder.Services.AddAuthentication("BasicAuthentication")
    .AddScheme<BasicAuthenticationOptions, BasicAuthenticationHandler>("BasicAuthentication", options => { });

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins",
        cors =>
        {
            var settings = builder.Configuration.GetSection("Settings").Get<Settings>();
            cors.WithOrigins(settings.AllowedCorsOrigin);
        });
});

var app = builder.Build();



if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();


app.MapControllers();

var settings = app.Services.GetRequiredService<IOptions<Settings>>().Value;
var logger = app.Services.GetRequiredService<ILogger<Program>>();

var webHandler = new WebHookHandler(logger, settings);
webHandler.Init();

app.Run();


