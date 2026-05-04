using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WorkTimeTracker.Agent;
using WorkTimeTracker.Agent.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));

builder.Services.AddSingleton<IRdpSessionMonitor, RdpSessionMonitor>();
builder.Services.AddSingleton<IScreenshotService, ScreenshotService>();
builder.Services.AddSingleton<IProcessMonitor, ProcessMonitor>();

builder.Services.AddHttpClient<IEventUploader, EventUploader>((sp, http) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AgentOptions>>().Value;
    http.BaseAddress = new Uri(opts.ServerUrl);
    http.DefaultRequestHeaders.Add("X-Agent-Token", opts.AgentToken);
    http.DefaultRequestHeaders.Add("X-Agent-Hostname", Environment.MachineName);
    http.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHostedService<AgentWorker>();

builder.Services.AddWindowsService(opts =>
{
    opts.ServiceName = "WorkTimeTrackerAgent";
});

var host = builder.Build();
await host.RunAsync();
