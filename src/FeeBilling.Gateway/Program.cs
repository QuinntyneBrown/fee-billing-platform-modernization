// Front door for all FeeBilling traffic (strangler fig, ADR-0003).
// Migrated routes go to the new services; everything else falls through to legacy IIS.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapReverseProxy();

app.Run();

public partial class Program;
