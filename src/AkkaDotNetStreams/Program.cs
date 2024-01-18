// TODO

using Akka.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAkka("AkkaDotNetStreamsSample", b => { });

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();