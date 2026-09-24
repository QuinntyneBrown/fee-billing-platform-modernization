using FeeBilling.Accounts.Api.Accounts;
using FeeBilling.Accounts.Api.Households;
using FeeBilling.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddFeeBillingInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();

// PascalCase JSON, same as Web API 2 + Newtonsoft. The AngularJS app reads account.AccountNumber etc.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.PropertyNamingPolicy = null);

// Login is still owned by the legacy app (Forms auth). Each request resolves the user by calling
// back into legacy (SystemWebAdapters remote authentication), so this API is down whenever legacy is.
builder.Services.AddSystemWebAdapters()
    .AddRemoteAppClient(options =>
    {
        options.RemoteAppUrl = new Uri(builder.Configuration["RemoteApp:Url"]
            ?? throw new InvalidOperationException("RemoteApp:Url is not configured."));
        options.ApiKey = builder.Configuration["RemoteApp:ApiKey"]
            ?? throw new InvalidOperationException("RemoteApp:ApiKey is not configured.");
    })
    .AddAuthenticationClient(true);

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.UseSystemWebAdapters();

app.MapDefaultEndpoints();
app.MapAccountsEndpoints();
app.MapHouseholdsEndpoints();

app.Run();

public partial class Program;
