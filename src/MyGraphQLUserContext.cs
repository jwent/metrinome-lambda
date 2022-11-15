using GraphQL;
using GraphQL.Validation;
// using GraphQL.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;



public class MyGraphQLUserContext : Dictionary<string, object?> {
	public ClaimsPrincipal User { get; set; }
	public string? UserId { get {
		if (User.Identity is ClaimsIdentity identity) {
			return identity.FindFirst("id")?.Value;
		}
		return null;
	} }

	public MyGraphQLUserContext(ClaimsPrincipal user) {
		User = user;
	}
}