using GraphQL;
using GraphQL.Validation;
// using GraphQL.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;



public class MyGraphQLUserContext : Dictionary<string, object?> {
	public ClaimsPrincipal User { get; set; }
	public MyGraphQLUserContext(ClaimsPrincipal user) {
		User = user;
	}
}


