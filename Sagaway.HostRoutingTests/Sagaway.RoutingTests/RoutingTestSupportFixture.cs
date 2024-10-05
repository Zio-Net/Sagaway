using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;
using Polly.Extensions.Http;
using Xunit.Abstractions;

namespace Sagaway.RoutingTests;

// ReSharper disable once ClassNeverInstantiated.Global
public class RoutingTestSupportFixture : IDisposable
{
    private HttpClient _testHttpClient;
    private ISignalRWrapper? _signalR;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Random _jitterer = new();
    private bool _isDisposed;
    private readonly TracerProvider _tracerProvider;
    

    public RoutingTestSupportFixture()
    {
        var services = new ServiceCollection();

        //if (services.All(x => x.ServiceType != typeof(IConfigurationRoot)))
        //{
        //    var config = new ConfigurationBuilder()
        //        .AddJsonFile("appsettings.json")
        //        .Build();

        //    services.AddSingleton(config);
        //}

        //var configuration = services.BuildServiceProvider().GetService<IConfigurationRoot>()
        //                    ?? throw new Exception("ConfigurationRoot is null");

        //var testServiceUrl = configuration["AppSettings:TestServiceUrl"] ?? throw new ArgumentException("AppSettings:TestServiceUrl is not configured");
        //SubSagaTestServiceUrl = configuration["AppSettings:SubSagaTestServiceUrl"] ?? throw new ArgumentException("AppSettings:SubSagaTestServiceUrl is not configured");
        
        AddRobustHttpClient<RoutingTestSupportFixture>(services, baseUrl:"http://localhost/");

        // Setup OpenTelemetry


        _tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddHttpClientInstrumentation() // Instrument outgoing HTTP requests
            .AddAspNetCoreInstrumentation(options =>
            {
                options.Filter = (httpContext) => httpContext.Request.Path != "/healthz";
            }) //Instrument incoming HTTP requests
            .AddZipkinExporter(options =>
            {
                options.Endpoint = new Uri("http://localhost:9411/api/v2/spans");
            })
            .SetSampler(new AlwaysOnSampler())
            .SetResourceBuilder(
                ResourceBuilder.CreateDefault().AddService("TestProject"))
            .Build();


        var serviceProvider = services.BuildServiceProvider();
        _httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        _testHttpClient = _httpClientFactory.CreateClient("IntegrationTestSupportFixture");
    }

    // ReSharper disable MemberCanBePrivate.Global
    // ReSharper disable UnusedAutoPropertyAccessor.Global
    public ITestOutputHelper TestOutputHelper { get; private set; } = null!; //must be set by each test class

    // ReSharper disable once MemberCanBePrivate.Global
    public HttpClient HttpClient => _testHttpClient;

   // public string SubSagaTestServiceUrl { get; }

    // ReSharper disable MemberCanBePrivate.Global
    public JsonSerializerOptions SerializeOptions => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };

    public ISignalRWrapper SignalR
    {
        get
        {
            if (_signalR == null)
            {
                throw new Exception($"SignalR is null. Did you forget to set call {nameof(Initiate)} in the test class ctor?");
            }
            return _signalR;
        }
    }

    public string GetTestResultFromSignalR(string messageStartWith)
    {
        var testResult = SignalR.Messages.FirstOrDefault(m => m.StartsWith(messageStartWith));
        if (testResult == null)
        {
            throw new Exception($"Test result not found");
        }

        return testResult;
    }

    private void AddRobustHttpClient<TClient>(
        IServiceCollection services, string baseUrl, int retryCount = 5,
        int handledEventsAllowedBeforeBreaking = 5, int durationOfBreakInSeconds = 30)
        where TClient : class
    {
        var httpClientBuilder =
            services.AddHttpClient<TClient>(typeof(TClient).Name, c => { c.BaseAddress = new Uri(baseUrl); });

        if (System.Diagnostics.Debugger.IsAttached)
            return;

        httpClientBuilder.AddPolicyHandler(GetRetryPolicy(retryCount))
            .AddPolicyHandler(GetCircuitBreakerPolicy(handledEventsAllowedBeforeBreaking, durationOfBreakInSeconds));
    }

    private IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(int retryCount)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(retryCount, // exponential back-off plus some jitter
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                                + TimeSpan.FromMilliseconds(_jitterer.Next(0, 100)));
    }

    private IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(
        int handledEventsAllowedBeforeBreaking, int durationOfBreakInSeconds)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(handledEventsAllowedBeforeBreaking, TimeSpan.FromSeconds(durationOfBreakInSeconds));
    }


    public void Initiate(ITestOutputHelper testOutputHelper)
    {
        if (_signalR != null!)
        {
            _signalR.Dispose();
        }
        _signalR = new SignalRWrapper(testOutputHelper);

        TestOutputHelper = testOutputHelper;
        _testHttpClient = _httpClientFactory.CreateClient("IntegrationTestSupportFixture");
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _tracerProvider.Dispose();
        _testHttpClient.Dispose();
        _isDisposed = true;
    }
}