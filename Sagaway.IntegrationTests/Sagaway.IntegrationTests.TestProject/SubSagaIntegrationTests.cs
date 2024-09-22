using System.Net;
using Xunit.Abstractions;

namespace Sagaway.IntegrationTests.TestProject;

[Collection("Sagaway Sub Saga Integration Tests")]
public class SubSagaIntegrationTests
{
    public SubSagaIntegrationTests(ITestOutputHelper testOutputHelper, IntegrationTestSupportFixture testServiceHelper)
    {
        testServiceHelper.Initiate(testOutputHelper);
        HttpClient = new HttpClient();
        HttpClient.BaseAddress = new Uri(testServiceHelper.SubSagaTestServiceUrl);
    }

    private HttpClient HttpClient { get; }

    [Trait("Integration Test", "SubSaga")]
    [Fact]
    public async Task TestSubSagaAsync()
    {
        // Act
        var response = await HttpClient.PostAsync("/run-test", null);

        // Assert that the status code is OK (200)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Read the response content
        var responseBody = (await response.Content.ReadAsStringAsync()).Trim('"');

        // Assert that the response body matches the expected content
        Assert.Equal("Test Succeeded", responseBody);
    }
}
