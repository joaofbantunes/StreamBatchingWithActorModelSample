using Proto;
using Proto.Cluster;
using Proto.Cluster.Kubernetes;
using Proto.Cluster.Partition;
using Proto.Cluster.Testing;
using Proto.DependencyInjection;
using Proto.Remote;
using Proto.Remote.GrpcNet;
using ProtoActorSimplified;
using ProtoActorSimplified.Messages;
using Shared.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddSimpleConsole(options => { options.TimestampFormat = "HH:mm:ss"; });
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<MongoBatchRepository.Settings>(builder.Configuration.GetSection("MongoDb"));
builder.Services.AddSingleton<MongoBatchRepository>();
builder.Services.AddSingleton<IBatchRepository>(s => s.GetRequiredService<MongoBatchRepository>());
builder.Services.AddHostedService<DatabaseInitializerHostedService>();
builder.Services.AddTransient<BatchAggregatorActor>();
builder.Services.AddSingleton<ActorSystem>(s => CreateActorSystem(s));
builder.Services.AddHostedService<ActorSystemHostedService>();
builder.Services.AddHostedService<KafkaConsumerHostedService>();

var app = builder.Build();
Proto.Log.SetLoggerFactory(app.Services.GetRequiredService<ILoggerFactory>());

app.MapGet("/", () => "Hello World!");

app.Run();

static ActorSystem CreateActorSystem(IServiceProvider services)
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var clusterName = "ProtoActorSimplifiedCluster";
    var systemConfig = ActorSystemConfig.Setup().WithDeveloperSupervisionLogging(true);
    var system = new ActorSystem(systemConfig).WithServiceProvider(services);
    var remoteConfig = GrpcNetRemoteConfig
        .BindTo("127.0.0.1")
        //   .WithAdvertisedHost("the hostname or ip of this pod")
        .WithProtoMessages(MessagesReflection.Descriptor);

    var clusterConfig = ClusterConfig
        .Setup(clusterName,
            configuration.GetValue<bool>("RunningInKubernetes")
                ? new KubernetesProvider()
                : new TestProvider(new TestProviderOptions(), new InMemAgent()),
            new PartitionIdentityLookup())
        .WithClusterKind(
            new ClusterKind(
                    "batch-aggregator",
                    system
                        .DI()
                        .PropsFor<BatchAggregatorActor>()
                        // making it a bit longer, but if we need more, maybe the state needs to be loaded elsewhere (or optimized, if possible)
                        .WithStartDeadline(TimeSpan.FromSeconds(1)))
                .WithLocalAffinityRelocationStrategy());

    return system
        .WithRemote(remoteConfig)
        .WithCluster(clusterConfig);
}