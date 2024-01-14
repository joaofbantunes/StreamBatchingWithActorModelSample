using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using Proto;
using Proto.Cluster;
using Proto.Cluster.Kubernetes;
using Proto.Cluster.Partition;
using Proto.Cluster.Testing;
using Proto.DependencyInjection;
using Proto.Remote;
using Proto.Remote.GrpcNet;
using ProtoActorSimplifiedWithBatchingOnForwarder.Messages;
using ProtoActorSimplifiedWithBatchingOnForwarder;
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
builder.Services.AddTransient<AggregatorActor>();
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
    var runningInKubernetes = configuration.GetValue<bool>("RunningInKubernetes");
    var clusterName = "ProtoActorSimplifiedCluster";
    var systemConfig = ActorSystemConfig.Setup().WithDeveloperSupervisionLogging(true);
    var system = new ActorSystem(systemConfig).WithServiceProvider(services);
    var remoteConfig = runningInKubernetes
        ? GrpcNetRemoteConfig
            .BindToAllInterfaces(advertisedHost: configuration["ProtoActor:AdvertisedHost"])
            .WithProtoMessages(MessagesReflection.Descriptor)
        : GrpcNetRemoteConfig
            .BindToLocalhost()
            .WithProtoMessages(MessagesReflection.Descriptor);

    var clusterConfig = ClusterConfig
        .Setup(clusterName,
            runningInKubernetes
                ? new KubernetesProvider()
                : new TestProvider(new TestProviderOptions(), new InMemAgent()),
            new PartitionIdentityLookup())
        .WithClusterKind(
            new ClusterKind(
                    "group-aggregator",
                    system
                        .DI()
                        .PropsFor<AggregatorActor>()
                        // making it a bit longer, but if we need more, maybe the state needs to be loaded elsewhere (or optimized, if possible)
                        .WithStartDeadline(TimeSpan.FromSeconds(1)))
                // use local affinity only when the Kafka keys are well defined, and all related messages are sent to the same partition
                .WithLocalAffinityRelocationStrategy()
        );

    return system
        .WithRemote(remoteConfig)
        .WithCluster(clusterConfig);
}