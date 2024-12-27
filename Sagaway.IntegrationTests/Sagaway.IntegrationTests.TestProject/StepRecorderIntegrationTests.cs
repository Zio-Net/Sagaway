using System.Net;
using Xunit.Abstractions;

namespace Sagaway.IntegrationTests.TestProject;

[Collection("Step Recorder Integration Tests")]
public class StepRecorderIntegrationTests
{
    public StepRecorderIntegrationTests(ITestOutputHelper testOutputHelper, IntegrationTestSupportFixture testServiceHelper)
    {
        testServiceHelper.Initiate(testOutputHelper);
        HttpClient = new HttpClient();
        HttpClient.BaseAddress = new Uri(testServiceHelper.TestRecorderServiceUrl);
    }

    private HttpClient HttpClient { get; }

    [Trait("Integration Test", "StepRecorder")]
    [Theory]
    [InlineData("empty")]
    [InlineData("internal")]
    [InlineData("statestore")]
    public async Task TestStepRecorderAsync(string stepRecorderType)
    {
        // Act
        var response = await HttpClient.GetAsync($"/run-test?stepRecorderType={stepRecorderType}");

        // Assert that the status code is OK (200)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Read the response content
        var responseBody = (await response.Content.ReadAsStringAsync()).Trim('"');

        // Assert that the response body matches the expected content
        Assert.Equal("pass", responseBody);
    }
}