using FfaasLite.WorkerSample;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<FlagClientSettings>(builder.Configuration.GetSection("FlagClient"));
builder.Services.AddHostedService<FlagClientWorker>();

var host = builder.Build();
host.Run();
