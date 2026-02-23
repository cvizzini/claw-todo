using Microsoft.JSInterop;
using TodoApp.Web.Components;
using TodoApp.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var apiBase = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5227";

// Named HttpClient shared between auth and todo services
builder.Services.AddHttpClient("TodoApi", client =>
{
    client.BaseAddress = new Uri(apiBase);
});

// AuthService — scoped per Blazor Server circuit so state is per-user
builder.Services.AddScoped<AuthService>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var client  = factory.CreateClient("TodoApi");
    var js      = sp.GetRequiredService<IJSRuntime>();
    return new AuthService(client, js);
});

// TodoApiService — scoped, uses the SAME named client
// (AuthService sets the Authorization header on the shared named client's instance
//  but since IHttpClientFactory creates a new instance per call we need a different approach:
//  inject AuthService into TodoApiService and set header per-request)
builder.Services.AddScoped<TodoApiService>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var client = factory.CreateClient("TodoApi");
    var auth = sp.GetRequiredService<AuthService>();
    return new TodoApiService(client, auth);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
