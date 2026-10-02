using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using DesignerIA.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7089";

builder.Services.AddScoped(sp => new HttpClient(new BrowserCredentialsHandler())
{
    BaseAddress = new Uri(apiBaseUrl),
});

await builder.Build().RunAsync();
