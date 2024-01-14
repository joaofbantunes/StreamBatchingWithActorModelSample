using Akka.Actor;
using Akka.Hosting;
using AkkaDotNetSimplified;
using Shared.Persistence;

/*
 * TODO: clustering
 * - have a router actor, that get the batch id and routes the message to the correct actor, creating it if needed
 * - make this router per node, to minimize serialization and network hops when possible
 */

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddSimpleConsole(options => { options.TimestampFormat = "HH:mm:ss"; });
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<MongoGroupItemRepository.Settings>(builder.Configuration.GetSection("MongoDb"));
builder.Services.AddSingleton<MongoGroupItemRepository>();
builder.Services.AddSingleton<IGroupItemRepository>(s => s.GetRequiredService<MongoGroupItemRepository>());
builder.Services.AddHostedService<DatabaseInitializerHostedService>();
builder.Services.AddAkka(
    "AkkaDotNetSimplified",
    b =>
    {
        b.WithActors((system, registry) =>
        {
            var distributor = system.ActorOf(Props.Create<AggregatorDirectoryActor>());
            registry.TryRegister<AggregatorDirectory>(distributor);
        });
    });
builder.Services.AddHostedService<KafkaConsumerHostedService>();

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();