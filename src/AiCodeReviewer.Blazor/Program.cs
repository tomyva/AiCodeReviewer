using AiCodeReviewer.Blazor.Components;
using AiCodeReviewer.Blazor.Services;
using AiCodeReviewer.Application;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddSingleton<IDataProtectionProvider, EphemeralDataProtectionProvider>();
builder.Services.AddSingleton<ApplicationRuntime>();
builder.Services.AddSingleton(sp => sp.GetRequiredService<ApplicationRuntime>().Application);
builder.Services.AddSingleton(sp => sp.GetRequiredService<ApplicationRuntime>().UserState);
builder.Services.AddTransient<ReviewPageModel>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
