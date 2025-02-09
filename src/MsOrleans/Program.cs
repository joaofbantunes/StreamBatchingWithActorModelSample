using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MsOrleans;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Orleans.Clustering.Kubernetes;
using Shared.Persistence;

BsonDefaults.GuidRepresentationMode = GuidRepresentationMode.V3;
BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddSimpleConsole(options => { options.TimestampFormat = "HH:mm:ss"; });
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<MongoGroupItemRepository.Settings>(builder.Configuration.GetSection("MongoDb"));
builder.Services.AddSingleton<MongoGroupItemRepository>();
builder.Services.AddSingleton<IGroupItemRepository>(s => s.GetRequiredService<MongoGroupItemRepository>());
builder.Services.AddHostedService<DatabaseInitializerHostedService>();

builder.Host.UseOrleans(siloBuilder =>
{
    if (builder.Configuration.GetValue<bool>("RunningInKubernetes"))
    {
        siloBuilder
            .UseKubernetesHosting() // sets up the silo to use Kubernetes hosting (not clustering) - https://learn.microsoft.com/en-us/dotnet/orleans/deployment/kubernetes
            .UseKubeMembership(); // sets up the silo to use Kubernetes clustering - https://github.com/OrleansContrib/Orleans.Clustering.Kubernetes/tree/master
    }
    else
    {
        siloBuilder.UseLocalhostClustering();
    }
    
    // siloBuilder.AddMemoryGrainStorage("StreamBatching");
    
    // Optional: Adds Orleans Dashboard
    siloBuilder.UseDashboard(options =>
    {
        options.HostSelf = false;
    });
    
    siloBuilder.AddActivityPropagation();
});

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("OrleansSample"))
    .WithTracing(b => b
        .AddAspNetCoreInstrumentation()
        //.AddSource("Microsoft.Orleans.Runtime")
        .AddSource("Microsoft.Orleans.Application")
        .AddOtlpExporter())
    .WithMetrics(b => b
        .AddAspNetCoreInstrumentation()
        .AddMeter("Microsoft.Orleans")
        .AddOtlpExporter());

builder.Services.AddHostedService<KafkaConsumerHostedService>();

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Map("/orleans-dashboard", x => x.UseOrleansDashboard());

app.Run();