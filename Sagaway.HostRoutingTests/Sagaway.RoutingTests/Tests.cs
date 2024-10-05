using System.Net;
using System.Text;
using System.Text.Json;
using Sagaway.Routing.Tracking;
using Xunit.Abstractions;

namespace Sagaway.RoutingTests
{
    [Collection("Sagaway Routing Tests")]
    public class Tests
    {
        private readonly RoutingTestSupportFixture _testServiceHelper;

        public Tests(ITestOutputHelper testOutputHelper, RoutingTestSupportFixture testServiceHelper)
        {
            _testServiceHelper = testServiceHelper;
            _testServiceHelper.Initiate(testOutputHelper);
        }

        private ITestOutputHelper TestOutputHelper => _testServiceHelper.TestOutputHelper;
        private HttpClient HttpClient => _testServiceHelper.HttpClient;
        private JsonSerializerOptions SerializeOptions => _testServiceHelper.SerializeOptions;
        private ISignalRWrapper SignalR => _testServiceHelper.SignalR;

        private Dictionary<string, int> _portMap = new()
        {
            {"TestActorA", 12100},
            {"TestServiceA", 12101}
        };

        //todo: add to the actors the ability to call sub-saga

        [Theory]
        [Trait("Routing Test", "Scenarios")]
        [InlineData("[TestSimpleChain]TestActorA->TestServiceA->Callback")]
        public async Task TestCallChainScenarios(string route)
        {

            var callChainInfo = new CallChainInfo(route);
            var firstServiceName = callChainInfo.PopNextInstruction();

            var body = new StringContent(JsonSerializer.Serialize(callChainInfo, SerializeOptions), Encoding.UTF8,
                "application/json");

 
            var response =
                await HttpClient.PostAsync($"http://localhost:{_portMap[firstServiceName]}/{firstServiceName}Queue",
                    body);

            var responseContent = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            TestOutputHelper.WriteLine(responseContent);

            var result = await SignalR.WaitForSignalREventAsync(60);

            Assert.True(result);

            var testResult = _testServiceHelper.GetTestResultFromSignalR(firstServiceName);

            Assert.Equal(route, testResult);
        }
    }
}