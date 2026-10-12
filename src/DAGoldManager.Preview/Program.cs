using DAGoldManager.UI;
using DAGoldManager.Updates;
using DAGoldManager.Preview;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders().AddConsole();
builder.WebHost.UseStaticWebAssets();
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.Configure<KeyManagementOptions>(options => options.XmlRepository = new MemoryKeyRepository());
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped(_ => new ApplicationUpdates(null, "", UpdateBuildInfo.Version));
// This host is intentionally simulation-only, even if a real data directory is configured.
builder.Services.AddScoped(_ => new DashboardModel(true,
    Path.Combine(Path.GetTempPath(), "DAGoldManagerPreview", Guid.NewGuid().ToString("N")), ""));
var app = builder.Build();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
