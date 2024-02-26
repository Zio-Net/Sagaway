using System.Net;
using System.Text;
using System.Text.Json;
using Xunit.Abstractions;

namespace Sagaway.IntegrationTests.TestProject;

public class IntegrationTests
{
    private readonly IntegrationTestSupportFixture _testServiceHelper;

    public IntegrationTests(ITestOutputHelper testOutputHelper, IntegrationTestSupportFixture testServiceHelper)
    {
        _testServiceHelper = testServiceHelper;
        _testServiceHelper.Initiate(testOutputHelper);
    }

    private ITestOutputHelper TestOutputHelper => _testServiceHelper.TestOutputHelper;
    private HttpClient HttpClient => _testServiceHelper.HttpClient;
    private JsonSerializerOptions SerializeOptions => _testServiceHelper.SerializeOptions;

    private ISignalRWrapper SignalR => _testServiceHelper.SignalR;

    [Trait("Integration Test", "Simple Saga")]
    [Fact]
    public async Task CreateSimpleSagaTest()
    {
        TestInfo testInfo = new()
        {
            TestName = "Simple Saga Test",
            Id = Guid.NewGuid(),
            ServiceACall = new ServiceTestInfo
            {
                CallId = Guid.NewGuid().ToString(),
                DelayOnCallInSeconds = [0],
                SuccessOnCall = 1,
                ShouldReturnCallbackResultOnCall = [true]
            },
            ServiceBCall = new ServiceTestInfo
            {
                CallId = Guid.NewGuid().ToString(),
                DelayOnCallInSeconds = [0],
                SuccessOnCall = 1,
                ShouldReturnCallbackResultOnCall = [true]
            }
        };

        SignalR.ListenToSignalR(testInfo.Id.ToString());

        var body = new StringContent(JsonSerializer.Serialize(testInfo, SerializeOptions), Encoding.UTF8, "application/json");

        var response = await HttpClient.PostAsync("run-test", body);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        TestOutputHelper.WriteLine(responseContent);

        var result = await SignalR.WaitForSignalREventAsync(50);

        var testResult = _testServiceHelper.GetTestResultFromSignalR(testInfo.Id);

        Assert.True(testResult.IsSuccess);
    }
}