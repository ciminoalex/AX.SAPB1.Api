using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AX.SAPB1.Api.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	[AllowAnonymous]
	public class TestController : ControllerBase
	{
		/// <summary>
		/// Endpoint fittizio per testare POST (e CORS/preflight).
		/// Echo del payload ricevuto.
		/// </summary>
		[HttpPost]
		public ActionResult<object> Post([FromBody] object? payload)
		{
			var result = new
			{
				status = "ok",
				timestampUtc = DateTime.UtcNow,
				method = "POST",
				path = HttpContext.Request.Path.ToString(),
				headers = HttpContext.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString()),
				payload
			};
			return Ok(result);
		}

		/// <summary>
		/// Gestione esplicita dell'OPTIONS per vedere se arriva al server.
		/// </summary>
		[HttpOptions]
		public IActionResult Options()
		{
			return Ok();
		}
	}
}


