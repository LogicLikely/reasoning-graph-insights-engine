using System.Text.Json;
using Backend.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace backend.Tests.Controllers;

[TestClass]
public class HealthControllerTests
{
    [TestMethod]
    public void Get_ReturnsSuccessPayload()
    {
        var controller = new HealthController();

        var result = controller.Get();

        var okResult = result as OkObjectResult;

        Assert.IsNotNull(okResult);
        Assert.AreEqual("""{"status":"ok"}""", JsonSerializer.Serialize(okResult.Value));
    }
}
